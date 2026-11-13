using System.Data;
using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class PoAbrechnungService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit)
{
    public record OffeneMeldung(Guid Id, string Laden, decimal Betrag, string? Referenz);
    public record PoKonto(Guid Id, string Name, DateTime BisExklusiv, List<PoZahlungseingang> Eingaenge,
        List<PoZahlungsanforderung> Anforderungen, List<PoGeldbewegung> Bewegungen,
        List<PoAbrechnungsEreignis> Historie, List<ProduktOwnerAuszahlung> AltProvisionen)
    {
        public decimal NeuAnzufordern => Eingaenge.Where(e => e.AnforderungId == null && e.EingangUtc < BisExklusiv).Sum(e => e.Betrag);
        public decimal Verrechnet => Anforderungen.Sum(a => a.VerrechneteProvision);
        public decimal Abzufuehren => Eingaenge.Sum(e => e.Betrag) - Verrechnet - Bewegungen.Where(b => !b.IstProvision && b.Status == PoBewegungsStatus.Bestaetigt).Sum(b => b.Betrag);
        public decimal ProvisionOffen => Eingaenge.Sum(e => e.Provision) - Verrechnet - Bewegungen.Where(b => b.IstProvision && b.Status == PoBewegungsStatus.Bestaetigt).Sum(b => b.Betrag);
        public decimal VerrechnungVorschau => Math.Max(0, Math.Min(NeuAnzufordern, Math.Min(
            Eingaenge.Where(e => e.AnforderungId == null && e.EingangUtc < BisExklusiv).Sum(e => e.Provision),
            ProvisionOffen - Bewegungen.Where(b => b.IstProvision && b.Status == PoBewegungsStatus.Gemeldet).Sum(b => b.Betrag))));
    }

    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;
    private static async Task<bool> Rolle(ApplicationDbContext db, Guid id, string rolle, CancellationToken ct)
        => await db.Users.AnyAsync(u => u.Id == id && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == rolle)), ct);

    private static async Task<bool> PruefeAsync(ApplicationDbContext db, Guid user, Guid? po, CancellationToken ct)
    {
        var admin = await Rolle(db, user, AppRollen.SuperAdmin, ct);
        if (!admin && (po != user || !await Rolle(db, user, AppRollen.ProduktOwner, ct))) throw new UnauthorizedAccessException();
        return admin;
    }

    private async Task AendereAsync(Func<ApplicationDbContext, Task> aktion, CancellationToken ct)
    {
        await using var vor = await factory.CreateDbContextAsync(ct);
        await vor.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await aktion(db);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        });
    }

    private async Task EreignisAsync(ApplicationDbContext db, Guid po, Guid user, Guid vorgang, string aktion, decimal betrag, string? notiz, CancellationToken ct)
    {
        var name = await db.Users.Where(u => u.Id == user).Select(u => u.FullName).SingleAsync(ct);
        db.PoAbrechnungsEreignisse.Add(new PoAbrechnungsEreignis { ProduktOwnerId = po, BenutzerId = user, Benutzer = name,
            VorgangId = vorgang, ZeitpunktUtc = Jetzt, Aktion = aktion, Betrag = betrag, Notiz = Text(notiz, 2000) });
    }

    private static string Text(string? text, int max)
    {
        var wert = text?.Trim() ?? "";
        if (wert.Length > max) throw new ArgumentException("Le texte est trop long.");
        return wert;
    }

    public async Task<List<OffeneMeldung>> GetMeldungenAsync(Guid user, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var admin = await PruefeAsync(db, user, user, ct);
        return await db.NutzungsAbos.AsNoTracking().Where(a => a.ZahlungStatus == AboZahlungStatus.Gemeldet
            && (admin || db.ProduktOwnerSalons.Any(p => p.ProduktOwnerId == user && p.SalonId == a.SalonId && p.GueltigBisUtc == null)))
            .OrderBy(a => a.ZahlungGemeldetUtc).Select(a => new OffeneMeldung(a.Id, a.Salon.Name, a.PreisSnapshot, a.ZahlungsNotiz)).ToListAsync(ct);
    }

    public Task BestaetigeEingangAsync(Guid user, Guid aboId, DateTime eingang, string? referenz, CancellationToken ct = default)
        => AendereAsync(async db =>
        {
            if (!await Rolle(db, user, AppRollen.ProduktOwner, ct)) throw new UnauthorizedAccessException();
            var abo = await db.NutzungsAbos.Include(a => a.Salon).SingleAsync(a => a.Id == aboId, ct);
            var zuordnung = await db.ProduktOwnerSalons.SingleOrDefaultAsync(p => p.ProduktOwnerId == user && p.SalonId == abo.SalonId && p.GueltigBisUtc == null, ct)
                ?? throw new UnauthorizedAccessException();
            if (await db.PoZahlungseingaenge.AnyAsync(e => e.NutzungsAboId == aboId && e.ProduktOwnerId == user, ct)) return;
            if (abo.ZahlungStatus != AboZahlungStatus.Gemeldet || abo.PreisSnapshot <= 0) throw new InvalidOperationException("Ce paiement n'est plus en attente.");
            var datum = DateTime.SpecifyKind(eingang.Date, DateTimeKind.Utc);
            if (datum > Jetzt.Date || datum < zuordnung.CreatedAtUtc.Date)
                throw new ArgumentException("La date doit être comprise entre votre affectation et aujourd'hui.");
            var besitzer = await db.UserSalons.Where(us => us.SalonId == abo.SalonId && db.UserRoles.Any(ur => ur.UserId == us.AppUserId
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer))).Select(us => us.AppUser.FullName).ToListAsync(ct);
            var monat = new DateTime(datum.Year, datum.Month, 1);
            var ende = monat.AddMonths(1);
            var festSchonErfasst = await db.PoZahlungseingaenge.AnyAsync(e => e.ProduktOwnerId == user && e.NutzungsAbo.SalonId == abo.SalonId
                && e.EingangUtc >= monat && e.EingangUtc < ende, ct)
                || await db.NutzungsAbos.AnyAsync(a => a.SalonId == abo.SalonId && a.ZahlungStatus == AboZahlungStatus.Bestaetigt
                    && a.ZahlungBestaetigtUtc >= monat && a.ZahlungBestaetigtUtc < ende && a.ZahlungBestaetigtUtc >= zuordnung.CreatedAtUtc, ct);
            var prozent = zuordnung.ProzentProvision ?? 0;
            var fest = festSchonErfasst ? 0 : zuordnung.FestbetragProvision ?? 0;
            var eintrag = new PoZahlungseingang { ProduktOwnerId = user, NutzungsAboId = aboId, Besitzer = string.Join(", ", besitzer),
                Laden = abo.Salon.Name, VonUtc = abo.ZeitraumVonUtc, BisUtc = abo.ZeitraumBisUtc, EingangUtc = datum, ErfasstUtc = Jetzt,
                Betrag = abo.PreisSnapshot, ProzentProvision = prozent, FestProvision = fest,
                Provision = Math.Round(abo.PreisSnapshot * prozent / 100 + fest, 2), Referenz = Text(referenz, 1000) };
            db.PoZahlungseingaenge.Add(eintrag);
            abo.ZahlungStatus = AboZahlungStatus.Bestaetigt; abo.ZahlungBestaetigtUtc = datum;
            db.VertragsHistorien.Add(new VertragsHistorie { NutzungsAboId = aboId, BenutzerId = user, ZeitpunktUtc = Jetzt,
                Aktion = "Paiement reçu par le responsable", Begruendung = eintrag.Referenz });
            await EreignisAsync(db, user, user, eintrag.Id, "Encaissement propriétaire confirmé", eintrag.Betrag, eintrag.Referenz, ct);
        }, ct);

    public async Task<List<PoKonto>> GetKontenAsync(Guid user, DateTime stichtag, CancellationToken ct = default)
    {
        var ende = Ende(stichtag);
        await using var db = await factory.CreateDbContextAsync(ct);
        var admin = await PruefeAsync(db, user, user, ct);
        var rollenIds = await db.UserRoles.Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.ProduktOwner)).Select(ur => ur.UserId).ToListAsync(ct);
        var pos = await db.Users.AsNoTracking().Where(u => (admin || u.Id == user) && (rollenIds.Contains(u.Id)
            || db.PoZahlungseingaenge.Any(e => e.ProduktOwnerId == u.Id))).OrderBy(u => u.FullName).ToListAsync(ct);
        var ids = pos.Select(p => p.Id).ToList();
        var eingaenge = await db.PoZahlungseingaenge.AsNoTracking().Where(e => ids.Contains(e.ProduktOwnerId)).OrderByDescending(e => e.EingangUtc).ToListAsync(ct);
        var anforderungen = await db.PoZahlungsanforderungen.AsNoTracking().Include(a => a.Positionen).Where(a => ids.Contains(a.ProduktOwnerId)).OrderByDescending(a => a.ErstelltUtc).ToListAsync(ct);
        var bewegungen = await db.PoGeldbewegungen.AsNoTracking().Where(b => ids.Contains(b.ProduktOwnerId)).OrderByDescending(b => b.ErfasstUtc).ToListAsync(ct);
        var historie = await db.PoAbrechnungsEreignisse.AsNoTracking().Where(h => ids.Contains(h.ProduktOwnerId)).OrderByDescending(h => h.ZeitpunktUtc).ToListAsync(ct);
        var alt = await db.ProduktOwnerAuszahlungen.AsNoTracking().Where(a => ids.Contains(a.ProduktOwnerId)).OrderByDescending(a => a.Jahr).ThenByDescending(a => a.Monat).ToListAsync(ct);
        return pos.Select(p => new PoKonto(p.Id, p.FullName, ende, eingaenge.Where(e => e.ProduktOwnerId == p.Id).ToList(),
            anforderungen.Where(a => a.ProduktOwnerId == p.Id).ToList(), bewegungen.Where(b => b.ProduktOwnerId == p.Id).ToList(),
            historie.Where(h => h.ProduktOwnerId == p.Id).ToList(), alt.Where(a => a.ProduktOwnerId == p.Id).ToList())).ToList();
    }

    private DateTime Ende(DateTime datum)
    {
        if (datum.Date > Jetzt.Date || datum.Date == DateTime.MaxValue.Date) throw new ArgumentException("Choisissez un arrêté au plus tard aujourd'hui.");
        return DateTime.SpecifyKind(datum.Date.AddDays(1), DateTimeKind.Utc);
    }

    public Task FordereAsync(Guid user, Guid lauf, DateTime stichtag, DateTime frist, IReadOnlyCollection<Guid> positionen, CancellationToken ct = default,
        IReadOnlyDictionary<Guid, decimal>? verrechnungen = null)
        => AendereAsync(async db =>
        {
            if (!await Rolle(db, user, AppRollen.SuperAdmin, ct)) throw new UnauthorizedAccessException();
            var ende = Ende(stichtag);
            if (frist.Date < Jetzt.Date) throw new ArgumentException("L'échéance ne peut pas être passée.");
            if (lauf == Guid.Empty || positionen.Count == 0) throw new ArgumentException("Aucune position sélectionnée.");
            if (await db.PoZahlungsanforderungen.AnyAsync(a => a.SammellaufId == lauf, ct)) return;
            var posten = await db.PoZahlungseingaenge.Where(e => positionen.Contains(e.Id)).ToListAsync(ct);
            if (posten.Count != positionen.Distinct().Count() || posten.Any(e => e.AnforderungId != null || e.EingangUtc >= ende))
                throw new InvalidOperationException("La sélection a changé. Actualisez la prévisualisation.");
            foreach (var gruppe in posten.GroupBy(e => e.ProduktOwnerId))
            {
                var verrechnung = 0m;
                if (verrechnungen is not null)
                {
                    var verfuegbar = await db.PoZahlungseingaenge.Where(e => e.ProduktOwnerId == gruppe.Key).SumAsync(e => e.Provision, ct)
                        - await db.PoZahlungsanforderungen.Where(a => a.ProduktOwnerId == gruppe.Key).SumAsync(a => a.VerrechneteProvision, ct)
                        - await db.PoGeldbewegungen.Where(b => b.ProduktOwnerId == gruppe.Key && b.IstProvision && b.Status != PoBewegungsStatus.Abgelehnt).SumAsync(b => b.Betrag, ct);
                    verrechnung = Math.Max(0, Math.Min(gruppe.Sum(e => e.Betrag), Math.Min(gruppe.Sum(e => e.Provision), verfuegbar)));
                    if (!verrechnungen.TryGetValue(gruppe.Key, out var erwartet) || erwartet != verrechnung)
                        throw new InvalidOperationException("La commission disponible a changé. Actualisez la prévisualisation avant de confirmer.");
                }
                var anforderung = new PoZahlungsanforderung { SammellaufId = lauf, ProduktOwnerId = gruppe.Key,
                    StichtagUtc = stichtag.Date, FaelligUtc = frist.Date, ErstelltUtc = Jetzt, Betrag = gruppe.Sum(e => e.Betrag), VerrechneteProvision = verrechnung };
                db.PoZahlungsanforderungen.Add(anforderung);
                foreach (var p in gruppe) p.Anforderung = anforderung;
                await EreignisAsync(db, gruppe.Key, user, anforderung.Id, "Demande de reversement créée", anforderung.Betrag, $"Arrêté au {stichtag:dd/MM/yyyy}", ct);
                if (verrechnung > 0)
                    await EreignisAsync(db, gruppe.Key, user, anforderung.Id, "Commission conservée par compensation", verrechnung,
                        $"Brut : {anforderung.Betrag:0.00} F ; commission compensée : {verrechnung:0.00} F ; net à reverser : {anforderung.NettoBetrag:0.00} F. Aucun encaissement bancaire.", ct);
            }
        }, ct);

    public Task MeldeBewegungAsync(Guid user, Guid po, Guid? anforderungId, bool provision, decimal betrag, DateTime datum, string referenz, CancellationToken ct = default)
        => AendereAsync(async db =>
        {
            var admin = await PruefeAsync(db, user, po, ct);
            if (provision != admin) throw new UnauthorizedAccessException();
            var text = Text(referenz, 200);
            if (betrag <= 0 || decimal.Round(betrag, 2) != betrag || string.IsNullOrWhiteSpace(text) || datum.Date > Jetzt.Date)
                throw new ArgumentException("Indiquez un montant positif, une date valide et une référence.");
            var vorhanden = await db.PoGeldbewegungen.SingleOrDefaultAsync(b => b.ProduktOwnerId == po && b.IstProvision == provision && b.Referenz == text, ct);
            if (vorhanden is not null)
            {
                if (vorhanden.AnforderungId == anforderungId && vorhanden.Betrag == betrag && vorhanden.ZahlungsdatumUtc == datum.Date
                    && vorhanden.Status != PoBewegungsStatus.Abgelehnt) return;
                throw new InvalidOperationException("Cette référence est déjà utilisée. Vérifiez le virement ou utilisez une nouvelle référence.");
            }
            decimal offen;
            if (provision)
            {
                if (anforderungId is not null) throw new ArgumentException("Commission séparée du reversement.");
                offen = await db.PoZahlungseingaenge.Where(e => e.ProduktOwnerId == po).SumAsync(e => e.Provision, ct)
                    - await db.PoZahlungsanforderungen.Where(a => a.ProduktOwnerId == po).SumAsync(a => a.VerrechneteProvision, ct)
                    - await db.PoGeldbewegungen.Where(b => b.ProduktOwnerId == po && b.IstProvision && b.Status != PoBewegungsStatus.Abgelehnt).SumAsync(b => b.Betrag, ct);
            }
            else
            {
                var a = await db.PoZahlungsanforderungen.SingleAsync(a => a.Id == anforderungId, ct);
                if (a.ProduktOwnerId != po) throw new UnauthorizedAccessException();
                offen = a.NettoBetrag - await db.PoGeldbewegungen.Where(b => b.AnforderungId == a.Id && b.Status != PoBewegungsStatus.Abgelehnt).SumAsync(b => b.Betrag, ct);
            }
            if (betrag > offen) throw new InvalidOperationException("Le montant dépasse le solde disponible, y compris les virements en attente.");
            var mouvement = new PoGeldbewegung { ProduktOwnerId = po, AnforderungId = anforderungId, IstProvision = provision,
                Betrag = betrag, ZahlungsdatumUtc = datum.Date, ErfasstUtc = Jetzt, Referenz = text, Status = PoBewegungsStatus.Gemeldet };
            db.PoGeldbewegungen.Add(mouvement);
            await EreignisAsync(db, po, user, mouvement.Id, provision ? "Versement de commission signalé" : "Reversement signalé", betrag, text, ct);
        }, ct);

    public Task EntscheideAsync(Guid user, Guid bewegungId, bool erhalten, string? notiz, CancellationToken ct = default)
        => AendereAsync(async db =>
        {
            var b = await db.PoGeldbewegungen.SingleAsync(b => b.Id == bewegungId, ct);
            var admin = await PruefeAsync(db, user, b.ProduktOwnerId, ct);
            if (b.IstProvision == admin) throw new UnauthorizedAccessException();
            if (b.Status != PoBewegungsStatus.Gemeldet) throw new InvalidOperationException("Ce virement a déjà été traité.");
            var text = Text(notiz, 2000);
            if (!erhalten && string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Un motif est obligatoire.");
            b.Status = erhalten ? PoBewegungsStatus.Bestaetigt : PoBewegungsStatus.Abgelehnt;
            await EreignisAsync(db, b.ProduktOwnerId, user, b.Id, erhalten ? "Réception confirmée" : "Virement non reçu", b.Betrag, text, ct);
        }, ct);
}
