using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.Services;
using ENote.Core.Services.Storage;
using ENote.WebApp.Data;
using ENote.WebApp.Services.Mails;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Abonnement kündigen und Konto löschen (nur der Besitzer des Ladens).
/// Kündigung: wirkt zum Ende der laufenden Periode, danach keine neuen Rechnungen und Kasse geschlossen;
/// jederzeit zurücknehmbar (nach Ablauf ohne Nachberechnung der Pause).
/// Löschung: nur ohne offene Rechnung und mit Passwort; Laden sofort gesperrt, nach 30 Tagen
/// (<see cref="FuehreFaelligeLoeschungenAusAsync"/>) werden Laden-, Kunden- und Personendaten gelöscht bzw.
/// anonymisiert – die eNOTE-Rechnungen (Perioden) bleiben ohne Personenbezug erhalten.
/// </summary>
public class KuendigungService(
    IDbContextFactory<ApplicationDbContext> factory,
    TimeProvider zeit,
    UserManager<AppUser> users,
    IMediaStorageService? media = null,
    MailBenachrichtigungen? mails = null,
    ILogger<KuendigungService>? logger = null)
{
    public sealed record Stand(
        bool ProbeLaeuft,
        DateTime WuerdeWirkenZumUtc,
        DateTime? GekuendigtAmUtc,
        DateTime? GekuendigtZumUtc,
        bool KuendigungWirksam,
        DateTime? LoeschungBeantragtUtc,
        DateTime? LoeschungAbUtc,
        int OffeneRechnungen,
        decimal OffenerBetrag)
    {
        public bool IstGekuendigt => GekuendigtZumUtc is not null;
        public bool LoeschungLaeuft => LoeschungBeantragtUtc is not null;
        public bool LoeschungMoeglich => OffeneRechnungen == 0 && !LoeschungLaeuft;
    }

    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    private static async Task PruefeBesitzerAsync(ApplicationDbContext db, Guid user, Guid salonId, CancellationToken ct)
    {
        var istBesitzer = await db.Users.AnyAsync(u => u.Id == user && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == user
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)), ct);
        if (!istBesitzer || !await db.UserSalons.AnyAsync(us => us.AppUserId == user && us.SalonId == salonId && us.Salon.GeloeschtUtc == null, ct))
            throw new UnauthorizedAccessException("Seul le propriétaire de l'établissement peut faire cela.");
    }

    /// <summary>Unbezahlt = offen, gemeldet (noch nicht bestätigt) oder abgelehnt.</summary>
    private static IQueryable<NutzungsAbo> OffeneRechnungen(ApplicationDbContext db, Guid salonId)
        => db.NutzungsAbos.Where(a => a.SalonId == salonId && a.ZahlungStatus != AboZahlungStatus.Bestaetigt);

    public async Task<Stand> GetStandAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using (var pruefung = await factory.CreateDbContextAsync(ct)) await PruefeBesitzerAsync(pruefung, user, salonId, ct);
        await SynchronisiereAsync(salonId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == salonId, ct);
        var jetzt = Jetzt;
        var perioden = await db.NutzungsAbos.AsNoTracking().Where(a => a.SalonId == salonId)
            .Select(a => new { a.ZeitraumVonUtc, a.ZeitraumBisUtc }).ToListAsync(ct);
        var offen = await OffeneRechnungen(db, salonId).Select(a => a.PreisSnapshot).ToListAsync(ct);
        return new Stand(salon.ProbeBisUtc > jetzt,
            KuendigungsRegeln.WirksamZum(jetzt, salon.ProbeBisUtc, perioden.Select(p => (p.ZeitraumVonUtc, p.ZeitraumBisUtc))),
            salon.GekuendigtAmUtc, salon.GekuendigtZumUtc, salon.GekuendigtZumUtc <= jetzt,
            salon.LoeschungBeantragtUtc, salon.LoeschungAbUtc, offen.Count, offen.Sum());
    }

    private async Task SynchronisiereAsync(Guid salonId, CancellationToken ct)
    {
        try { await new VertragsService(factory, zeit).SynchronisiereAsync(salonId, ct); }
        catch (InvalidOperationException) { /* kein Tarif konfiguriert: nichts zu berechnen */ }
    }

    /// <summary>Kündigt zum Ende der laufenden Periode. Gibt den Zeitpunkt zurück, ab dem die Kasse geschlossen ist.</summary>
    public async Task<DateTime> KuendigeAsync(Guid user, Guid salonId, string? grund, CancellationToken ct = default)
    {
        var bereinigt = string.IsNullOrWhiteSpace(grund) ? null : grund.Trim();
        if (bereinigt is { Length: > KuendigungsRegeln.GrundMax }) throw new ArgumentException($"Motif : {KuendigungsRegeln.GrundMax} caractères au maximum.");
        await using (var pruefung = await factory.CreateDbContextAsync(ct)) await PruefeBesitzerAsync(pruefung, user, salonId, ct);
        await SynchronisiereAsync(salonId, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        var salon = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
        if (salon.LoeschungBeantragtUtc is not null) throw new InvalidOperationException("La suppression du compte est déjà demandée.");
        if (salon.GekuendigtZumUtc is not null) throw new InvalidOperationException("L'abonnement est déjà résilié.");
        var jetzt = Jetzt;
        var perioden = await db.NutzungsAbos.Where(a => a.SalonId == salonId).Select(a => new { a.ZeitraumVonUtc, a.ZeitraumBisUtc }).ToListAsync(ct);
        var zum = KuendigungsRegeln.WirksamZum(jetzt, salon.ProbeBisUtc, perioden.Select(p => (p.ZeitraumVonUtc, p.ZeitraumBisUtc)));
        salon.GekuendigtAmUtc = jetzt;
        salon.GekuendigtZumUtc = zum;
        salon.KuendigungGrund = bereinigt;
        salon.NaechstesVertragsIntervall = null;
        salon.VertragsWechselAbUtc = null;
        salon.UpdatedAtUtc = jetzt;
        await db.SaveChangesAsync(ct);
        if (mails is not null) await mails.KuendigungAsync(salonId, user, ct);
        return zum;
    }

    /// <summary>Nimmt die Kündigung zurück. War sie schon wirksam, beginnt die Abrechnung ab heute neu (keine Nachberechnung).</summary>
    public async Task NimmKuendigungZurueckAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var salon = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
        if (salon.GekuendigtZumUtc is null) return;
        if (salon.LoeschungBeantragtUtc is not null) throw new InvalidOperationException("Annulez d'abord la suppression du compte.");
        var jetzt = Jetzt;
        await NeustartOhneNachberechnungAsync(db, salon, jetzt, ct);
        salon.GekuendigtAmUtc = null;
        salon.GekuendigtZumUtc = null;
        salon.KuendigungGrund = null;
        salon.UpdatedAtUtc = jetzt;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lag das Ende der letzten Periode in der Vergangenheit, startet die Abrechnung jetzt (Pause wird nicht berechnet).</summary>
    private static async Task NeustartOhneNachberechnungAsync(ApplicationDbContext db, Core.DataModels.Salons.Salon salon, DateTime jetzt, CancellationToken ct)
    {
        if (salon.ProbeBisUtc > jetzt) return; // Probezeit läuft noch: Start wie gewohnt danach
        var letztesEnde = await db.NutzungsAbos.Where(a => a.SalonId == salon.Id).Select(a => (DateTime?)a.ZeitraumBisUtc).MaxAsync(ct);
        // Läuft die bezahlte Periode noch, geht es nahtlos weiter; sonst beginnt die nächste Periode heute.
        if (letztesEnde is null || letztesEnde < jetzt) salon.VertragsBeginnUtc = jetzt;
    }

    /// <summary>Kontolöschung beantragen: Passwort, keine offene Rechnung. Laden ab sofort gesperrt.</summary>
    public async Task<DateTime> BeantrageLoeschungAsync(Guid user, Guid salonId, string passwort, CancellationToken ct = default)
    {
        await using (var pruefung = await factory.CreateDbContextAsync(ct)) await PruefeBesitzerAsync(pruefung, user, salonId, ct);
        var konto = await users.FindByIdAsync(user.ToString()) ?? throw new UnauthorizedAccessException();
        if (!await users.CheckPasswordAsync(konto, passwort ?? "")) throw new ArgumentException("Le mot de passe est incorrect.");
        await SynchronisiereAsync(salonId, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        var salon = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
        if (salon.LoeschungBeantragtUtc is not null) return salon.LoeschungAbUtc!.Value;
        var offen = await OffeneRechnungen(db, salonId).Select(a => a.PreisSnapshot).ToListAsync(ct);
        if (offen.Count > 0)
            throw new InvalidOperationException($"Réglez d'abord {(offen.Count == 1 ? "la facture ouverte" : $"les {offen.Count} factures ouvertes")} ({offen.Sum():N0} F) : la suppression n'est possible que sans facture impayée.");
        var jetzt = Jetzt;
        salon.LoeschungBeantragtUtc = jetzt;
        salon.LoeschungAbUtc = KuendigungsRegeln.LoeschungAb(jetzt);
        salon.UpdatedAtUtc = jetzt;
        await db.SaveChangesAsync(ct);
        if (mails is not null) await mails.LoeschungGeplantAsync(salonId, user, ct);
        return salon.LoeschungAbUtc.Value;
    }

    /// <summary>Löschung zurücknehmen (innerhalb der Bedenkzeit). Die gesperrte Zeit wird nicht berechnet.</summary>
    public async Task NimmLoeschungZurueckAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var salon = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
        if (salon.LoeschungBeantragtUtc is null) return;
        var jetzt = Jetzt;
        if (salon.GekuendigtZumUtc is null || salon.GekuendigtZumUtc > jetzt)
            await NeustartOhneNachberechnungAsync(db, salon, jetzt, ct);
        salon.LoeschungBeantragtUtc = null;
        salon.LoeschungAbUtc = null;
        salon.UpdatedAtUtc = jetzt;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ Ausführung nach der Bedenkzeit

    /// <summary>Hintergrunddienst: führt alle fälligen Löschungen aus. Gibt die Anzahl gelöschter Läden zurück.</summary>
    public async Task<int> FuehreFaelligeLoeschungenAusAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var jetzt = Jetzt;
        var faellig = await db.Salons.AsNoTracking()
            .Where(s => s.GeloeschtUtc == null && s.LoeschungAbUtc != null && s.LoeschungAbUtc <= jetzt)
            .Select(s => s.Id).ToListAsync(ct);
        var anzahl = 0;
        foreach (var id in faellig)
        {
            try
            {
                await LoescheLadenAsync(id, ct);
                anzahl++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogError(ex, "Löschung des Ladens {SalonId} fehlgeschlagen.", id);
            }
        }
        return anzahl;
    }

    private static async Task EntferneAsync<T>(ApplicationDbContext db, IQueryable<T> abfrage, CancellationToken ct) where T : class
    {
        if (db.Database.IsRelational())
        {
            await abfrage.ExecuteDeleteAsync(ct);
            return;
        }
        db.RemoveRange(await abfrage.ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Löscht alle Daten eines Ladens (Kunden, Verkäufe, Katalog, Ausgaben, Medien …), anonymisiert die
    /// Konten des Ladens und die Stammdaten. Die Perioden (eNOTE-Rechnungen) bleiben – ohne Personenbezug.
    /// </summary>
    internal async Task LoescheLadenAsync(Guid salonId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var salon = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
        if (salon.GeloeschtUtc is not null) return;
        var jetzt = Jetzt;

        var kontoIds = await db.UserSalons.Where(us => us.SalonId == salonId).Select(us => us.AppUserId).ToListAsync(ct);
        // Nur Konten, die ausschließlich zu diesem Laden gehören und keine Plattformrolle haben.
        var plattform = await db.UserRoles.Where(ur => kontoIds.Contains(ur.UserId)
            && db.Roles.Any(r => r.Id == ur.RoleId && (r.Name == AppRollen.SuperAdmin || r.Name == AppRollen.ProduktOwner)))
            .Select(ur => ur.UserId).ToListAsync(ct);
        var anderswo = await db.UserSalons.Where(us => kontoIds.Contains(us.AppUserId) && us.SalonId != salonId).Select(us => us.AppUserId).ToListAsync(ct);
        var zuAnonymisieren = kontoIds.Except(plattform).Except(anderswo).ToList();

        // 1) Kunden und ihr Verlauf
        await EntferneAsync(db, db.KundenNotizen.Where(n => n.SalonId == salonId), ct);
        await EntferneAsync(db, db.KundenBesuche.Where(b => b.SalonId == salonId), ct);
        // 2) Verkäufe (Positionen/Zahlungen zuerst)
        await EntferneAsync(db, db.VerkaufZahlungen.Where(z => db.Verkaeufe.Any(v => v.Id == z.VerkaufId && v.SalonId == salonId)), ct);
        await EntferneAsync(db, db.VerkaufsPositionen.Where(p => db.Verkaeufe.Any(v => v.Id == p.VerkaufId && v.SalonId == salonId)), ct);
        await EntferneAsync(db, db.Verkaeufe.Where(v => v.SalonId == salonId && v.StornoVonVerkaufId != null), ct);
        await EntferneAsync(db, db.Verkaeufe.Where(v => v.SalonId == salonId), ct);
        await EntferneAsync(db, db.Kunden.Where(k => k.SalonId == salonId), ct);
        // 3) Kasse, Lager, Geld
        await EntferneAsync(db, db.LagerAbgaenge.Where(a => a.SalonId == salonId), ct);
        await EntferneAsync(db, db.GeldBewegungen.Where(g => g.SalonId == salonId), ct);
        await EntferneAsync(db, db.KassenSitzungen.Where(k => k.SalonId == salonId), ct);
        // 4) Ausgaben, Katalog, Remises
        await EntferneAsync(db, db.Ausgaben.Where(a => a.SalonId == salonId), ct);
        await EntferneAsync(db, db.AusgabenKategorien.Where(k => k.SalonId == salonId), ct);
        await EntferneAsync(db, db.Rabatte.Where(r => r.SalonId == salonId), ct);
        await EntferneAsync(db, db.LeistungFavoriten.Where(f => f.SalonId == salonId), ct);
        await EntferneAsync(db, db.Leistungen.Where(l => l.SalonId == salonId && l.BasisLeistungId != null), ct);
        await EntferneAsync(db, db.Leistungen.Where(l => l.SalonId == salonId), ct);
        await EntferneAsync(db, db.LeistungKategorien.Where(k => k.SalonId == salonId && k.ParentId != null), ct);
        await EntferneAsync(db, db.LeistungKategorien.Where(k => k.SalonId == salonId), ct);
        // 5) Sonstiges am Laden
        await EntferneAsync(db, db.BesitzerHinweise.Where(h => h.SalonId == salonId), ct);
        await EntferneAsync(db, db.SalonMobileMoneyAnbieter.Where(a => a.SalonId == salonId), ct);
        await EntferneAsync(db, db.GeraetSalonFavoriten.Where(f => f.SalonId == salonId), ct);

        // 6) Medien (Logo, Bilder, Belege, Profilfotos der Konten)
        salon.LogoAssetId = null;
        await db.SaveChangesAsync(ct);
        var profilBlobs = await db.Users.Where(u => zuAnonymisieren.Contains(u.Id) && u.ProfilBildBlobName != null)
            .Select(u => u.ProfilBildBlobName!).ToListAsync(ct);
        var ladenBlobs = await db.BlobMediaAssets.Where(b => b.SalonId == salonId).Select(b => b.BlobName).ToListAsync(ct);
        foreach (var blob in ladenBlobs.Concat(profilBlobs).Distinct())
        {
            try { if (media is not null) await media.DeleteAsync(blob, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger?.LogWarning(ex, "Blob {Blob} konnte nicht gelöscht werden.", blob); }
        }
        await EntferneAsync(db, db.BlobMediaAssets.Where(b => b.SalonId == salonId || profilBlobs.Contains(b.BlobName)), ct);

        // 7) Konten des Ladens anonymisieren (Verweise in Rechnungshistorie bleiben gültig)
        var emails = await db.Users.Where(u => zuAnonymisieren.Contains(u.Id) && u.Email != null).Select(u => u.Email!).ToListAsync(ct);
        await EntferneAsync(db, db.MailNachrichten.Where(m => emails.Contains(m.Empfaenger)), ct);
        await EntferneAsync(db, db.UserSalons.Where(us => us.SalonId == salonId), ct);
        foreach (var konto in await db.Users.Where(u => zuAnonymisieren.Contains(u.Id)).ToListAsync(ct))
        {
            var kennung = $"supprime-{konto.Id:N}"[..17];
            konto.UserName = kennung;
            konto.NormalizedUserName = kennung.ToUpperInvariant();
            konto.Email = null;
            konto.NormalizedEmail = null;
            konto.EmailConfirmed = false;
            konto.PhoneNumber = null;
            konto.PasswordHash = null;
            konto.SecurityStamp = Guid.NewGuid().ToString();
            konto.Vorname = string.Empty;
            konto.Nachname = string.Empty;
            konto.FullName = "Compte supprimé";
            konto.TelefonNummer = null;
            konto.ProfilBildBlobName = null;
            konto.IsBlocked = true;
            konto.ActiveSessionId = null;
            konto.LockoutEnabled = true;
            konto.LockoutEnd = DateTimeOffset.MaxValue;
            konto.UpdatedAtUtc = jetzt;
        }

        // 8) Laden-Stammdaten anonymisieren; Perioden (Rechnungen) bleiben
        salon.Name = $"Établissement supprimé {salon.Id.ToString("N")[..8]}";
        salon.Standort = null;
        salon.Beschreibung = null;
        salon.TelefonNummer = null;
        salon.Adresse = null;
        salon.BonQrLink = null;
        salon.Slogan = null;
        salon.KuendigungGrund = null;
        salon.IstAktiv = false;
        salon.GekuendigtZumUtc ??= jetzt;
        salon.GeloeschtUtc = jetzt;
        salon.UpdatedAtUtc = jetzt;
        await db.SaveChangesAsync(ct);
        logger?.LogInformation("Laden {SalonId} gelöscht (Konten anonymisiert: {Anzahl}).", salonId, zuAnonymisieren.Count);
    }
}
