using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

public sealed record TarifEinstellung(long? Monatspreis, int? ZahlungszielTage, decimal? ResponsableProzent);
public sealed record SchulTarifZeile(Guid Id, string Name, string Ort, SchulStatus Status, long? Preis, decimal? Prozent, string? Responsable,
    string? LaufendesJahr, long? Eingefroren);

/// <summary>
/// Tarife (nur Admin): Preis je Schüler und Monat (seit 19.11.2026), Zahlungsziel, Standardanteil der Responsables und je Schule abweichender
/// Preis/Anteil. Der beim Jahresstart eingefrorene Preis eines Schuljahres bleibt unverändert. Jede Änderung wird protokolliert.
/// </summary>
public sealed class TarifService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit)
{
    public const long MaxPreis = 10_000_000;

    private async Task<Akteur> AdminAsync(Guid akteurId, bool schreiben, CancellationToken ct)
        => (await zugriff.PruefeAsync(akteurId, Faehigkeit.PlattformVerwalten, null, schreiben, ct)).Akteur;

    public async Task<TarifEinstellung> EinstellungAsync(Guid akteurId, CancellationToken ct = default)
    {
        await AdminAsync(akteurId, false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.PlattformEinstellungen.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PlattformEinstellung();
        return new TarifEinstellung(e.MonatspreisProSchueler, e.ZahlungszielTage, e.ResponsableProzentStandard);
    }

    public async Task EinstellungSpeichernAsync(Guid akteurId, TarifEinstellung t, CancellationToken ct = default)
    {
        var admin = await AdminAsync(akteurId, true, ct);
        var preis = Preis(t.Monatspreis) ?? throw new FachFehler("Indiquez le prix mensuel par élève.", "preis");
        if (t.ZahlungszielTage is not (>= 0 and <= 365)) throw new FachFehler("Le délai de paiement doit être compris entre 0 et 365 jours.", "ziel");
        var prozent = Prozent(t.ResponsableProzent) ?? 0;
        await dbFactory.TransaktionAsync(async db =>
        {
            var e = await db.PlattformEinstellungen.FirstOrDefaultAsync(ct);
            if (e is null)
            {
                e = new PlattformEinstellung { Id = PlattformEinstellung.EinzigeId };
                db.PlattformEinstellungen.Add(e);
            }
            var vorher = $"{SkoulouKultur.Fcfa(e.MonatspreisProSchueler)} / mois, {e.ZahlungszielTage} j, {e.ResponsableProzentStandard:0.##} %";
            (e.MonatspreisProSchueler, e.ZahlungszielTage, e.ResponsableProzentStandard) = (preis, t.ZahlungszielTage.Value, prozent);
            audit.Erfasse(db, admin.Id, null, FinanzAudit.Tarife, nameof(PlattformEinstellung), e.Id,
                $"Tarifs : {vorher} → {SkoulouKultur.Fcfa(preis)} / mois, {e.ZahlungszielTage} j, {prozent:0.##} %.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<Seite<SchulTarifZeile>> SchulenAsync(Guid akteurId, string? suche, int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        await AdminAsync(akteurId, false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Schulen.Where(s => s.Status == SchulStatus.Active || s.Status == SchulStatus.Suspendue || s.Status == SchulStatus.EnAttente);
        if (Eingabe.Suche(suche) is { } s)
        {
            var muster = $"%{s}%";
            q = q.Where(x => EF.Functions.Like(x.Name, muster) || EF.Functions.Like(x.Ort, muster));
        }
        var projektion = from x in q.OrderBy(x => x.Name)
                         join r in db.Users on x.ResponsableId equals r.Id into rj
                         from r in rj.DefaultIfEmpty()
                         let jahr = db.Schuljahre.Where(j => j.SchuleId == x.Id && j.Status == SchuljahrStatus.Laufend).FirstOrDefault()
                         select new SchulTarifZeile(x.Id, x.Name, x.Ort, x.Status, x.MonatspreisOverride, x.ResponsableProzent,
                             r == null ? null : r.Vorname + " " + r.Nachname, jahr == null ? null : jahr.Bezeichnung, jahr == null ? null : jahr.PlattformMonatspreis);
        return await Seite<SchulTarifZeile>.LadeAsync(projektion, ueberspringen, anzahl, ct);
    }

    /// <summary>Abweichender Preis/Anteil einer Schule (leer = Standard); wirkt ab dem nächsten Jahresstart.</summary>
    public async Task SchulTarifSpeichernAsync(Guid akteurId, Guid schuleId, long? preis, decimal? prozent, CancellationToken ct = default)
    {
        var admin = await AdminAsync(akteurId, true, ct);
        var neuPreis = Preis(preis);
        var neuProzent = Prozent(prozent);
        await dbFactory.TransaktionAsync(async db =>
        {
            var s = await db.Schulen.FirstOrDefaultAsync(x => x.Id == schuleId, ct) ?? throw new FachFehler("Cet établissement est introuvable.");
            static string P(long? p) => p is { } x ? SkoulouKultur.Fcfa(x) + " / mois" : "standard";
            static string R(decimal? r) => r is { } x ? $"{x:0.##} %" : "standard";
            audit.Erfasse(db, admin.Id, s.Id, FinanzAudit.Tarife, nameof(Schule), s.Id,
                $"Tarif de l'établissement : {P(s.MonatspreisOverride)} / {R(s.ResponsableProzent)} → {P(neuPreis)} / {R(neuProzent)}.");
            (s.MonatspreisOverride, s.ResponsableProzent) = (neuPreis, neuProzent);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private static long? Preis(long? preis) => preis switch
    {
        null => null,
        <= 0 or > MaxPreis => throw new FachFehler($"Le prix doit être compris entre 1 et {SkoulouKultur.Fcfa(MaxPreis)}.", "preis"),
        _ => preis
    };

    private static decimal? Prozent(decimal? prozent) => prozent switch
    {
        null => null,
        < 0 or > 100 => throw new FachFehler("La part du responsable doit être comprise entre 0 et 100 %.", "prozent"),
        _ => Math.Round(prozent.Value, 2)
    };
}
