using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Organisation.Druck;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Kopf eines Finanzdokuments (Quittung, Bericht).</summary>
public sealed record SchulDokument(string Name, string? Slogan, string? Adresse, string? Kontakt, string Ort, byte[]? Logo);

public sealed record QuittungPosten(string Bezeichnung, string? Tranche, DateOnly? Faellig, long Betrag);

/// <summary>Inhalt einer Quittung (Beträge nach dieser Zahlung in der Verteilungsreihenfolge).</summary>
public sealed record QuittungDaten(
    SchulDokument Schule, string Nummer, DateOnly Datum, long Betrag, string Kanal, string? Referenz, string? Bemerkung,
    string Schueler, string? Matrikel, string? Klasse, string Schuljahr, IReadOnlyList<QuittungPosten> Verteilung, long Guthaben,
    long SollJahr, long BezahltBisher, long RestNachher, string ErfasstVon, bool Storniert, string? StornoGrund);

/// <summary>Quittungen als PDF: Personal mit Finanzrecht, der Schüler selbst, seine Eltern, Responsable (eigene Schulen), Admin.</summary>
public sealed class QuittungService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, IMedienSpeicher speicher, TimeProvider zeit)
{
    public async Task<PdfDatei> PdfAsync(Guid akteurId, Guid zahlungId, CancellationToken ct = default)
    {
        var daten = await DatenAsync(akteurId, zahlungId, ct);
        return new PdfDatei(QuittungPdf.Erzeuge(daten), $"recu-{daten.Nummer}.pdf");
    }

    public async Task<QuittungDaten> DatenAsync(Guid akteurId, Guid zahlungId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var z = await db.Zahlungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == zahlungId, ct) ?? throw new FachFehler("Ce reçu est introuvable.");
        await PruefeAsync(db, akteur, z.SchuleId, z.SchuelerId, ct);

        var heute = new SchulZeit(zeit).Heute;
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == z.SchuljahrId, ct);
        var eigene = new ZahlungDaten(z.Id, z.Betrag, z.Datum, z.ErstelltAmUtc);
        var konto = await KontoLader.KontoAsync(db, z.SchuleId, z.SchuljahrId, z.SchuelerId, heute, ct, z.StorniertAmUtc is null ? null : eigene);
        var kopf = (await KontoLader.KoepfeAsync(db, z.SchuljahrId, [z.SchuelerId], ct))[z.SchuelerId];

        // Bisher gezahlt = alle gültigen Zahlungen bis einschließlich dieser (gleiche Reihenfolge wie die Verteilung).
        var alle = await db.Zahlungen.AsNoTracking().Where(x => x.SchuljahrId == z.SchuljahrId && x.SchuelerId == z.SchuelerId && (x.StorniertAmUtc == null || x.Id == z.Id))
            .Select(x => new ZahlungDaten(x.Id, x.Betrag, x.Datum, x.ErstelltAmUtc)).ToListAsync(ct);
        var bisher = alle.OrderBy(x => x.Datum).ThenBy(x => x.ErfasstUtc).ThenBy(x => x.Id)
            .TakeWhile(x => x.Id != z.Id).Sum(x => x.Betrag) + z.Betrag;
        var zeilen = konto.Zeilen.ToDictionary(x => x.Schluessel);
        var verteilung = konto.Zuordnungen.Where(x => x.ZahlungId == z.Id).Select(x =>
        {
            var zeile = zeilen[x.Schluessel];
            return new QuittungPosten(zeile.Bezeichnung, zeile.RatenAnzahl > 1 ? $"Tranche {zeile.RateNr}/{zeile.RatenAnzahl}" : null, zeile.Faellig, x.Betrag);
        }).ToList();
        var erfasst = await db.Users.Where(k => k.Id == z.ErfasstVonId).Select(k => k.Vorname + " " + k.Nachname).FirstAsync(ct);
        return new QuittungDaten(
            await SchulDokumentAsync(db, speicher, z.SchuleId, ct), z.Quittungsnummer, z.Datum, z.Betrag, FinanzEingabe.KanalText(z.Kanal), z.Referenz,
            z.Bemerkung, kopf.Name, kopf.Matrikel, kopf.Klasse, jahr.Bezeichnung, verteilung, z.Betrag - verteilung.Sum(v => v.Betrag),
            konto.Soll, bisher, Math.Max(0, konto.Soll - bisher - konto.VortragGuthaben), erfasst, z.StorniertAmUtc is not null, z.StornoGrund);
    }

    /// <summary>Darf der Akteur Belege dieses Schülers sehen?</summary>
    internal async Task PruefeAsync(SkoulouDbContext db, Akteur akteur, Guid schuleId, Guid schuelerId, CancellationToken ct)
    {
        if (akteur.IstDirektion)
        {
            if (akteur.SchuleId != schuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
            if (!FinanzRechte.Hat(akteur, Faehigkeit.FinanzenEinsehen) && !FinanzRechte.Hat(akteur, Faehigkeit.ZahlungenErfassen))
                throw new KeinZugriffFehler();
            return;
        }
        Zugriff.Pruefe(akteur, Faehigkeit.FinanzenEinsehen, false);
        switch (akteur.Rolle)
        {
            case Rollen.Schueler when akteur.Id == schuelerId:
                return;
            case Rollen.Eltern when akteur.SchuleId == schuleId && await SchulAbfragen.IstKindAsync(db, akteur.Id, schuelerId, ct):
                return;
            case Rollen.Responsable or Rollen.SuperAdmin:
                await zugriff.PruefeSchuleAsync(akteur, schuleId, ct);
                return;
            default:
                throw new KeinZugriffFehler();
        }
    }

    /// <summary>Kopfdaten der Schule mit geprüftem Logo.</summary>
    internal static async Task<SchulDokument> SchulDokumentAsync(SkoulouDbContext db, IMedienSpeicher speicher, Guid schuleId, CancellationToken ct)
    {
        var s = await db.Schulen.AsNoTracking().Where(x => x.Id == schuleId)
            .Select(x => new { Name = x.OffizielleBezeichnung ?? x.Name, x.Slogan, x.Adresse, x.Telefon, x.Email, x.Ort, x.LogoMediumId }).FirstAsync(ct);
        var kontakt = string.Join(" · ", new[] { s.Telefon, s.Email }.Where(t => !string.IsNullOrWhiteSpace(t)));
        return new SchulDokument(s.Name, s.Slogan, s.Adresse, kontakt.Length == 0 ? null : kontakt, s.Ort,
            DruckStil.PruefeBild(await BildDatei.LadeAsync(db, speicher, s.LogoMediumId, ct)));
    }
}
