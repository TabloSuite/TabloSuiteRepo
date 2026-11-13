using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Organisation.Druck;

/// <summary>Fertige PDF-Datei mit Dateinamen.</summary>
public sealed record PdfDatei(byte[] Inhalt, string Dateiname);

/// <summary>
/// Erzeugt die PDFs des Bereichs: Zugangsliste (nur der Ersteller des Stapels, 30 Minuten), Schülerkarten
/// einer Klasse, Stundenplan einer Klasse und Woche einer Lehrkraft. Rechte prüfen die jeweiligen Dienste.
/// </summary>
public sealed class DruckService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, ZugangsAblage ablage, IMedienSpeicher speicher,
    KartenService karten, StundenplanService plaene, IOptions<AppOptionen> app, TimeProvider zeit)
{
    public const string MeldungAbgelaufen = "Cette liste des accès n'est plus disponible (30 minutes au maximum). Réinitialisez les mots de passe concernés pour en imprimer une nouvelle.";

    public async Task<PdfDatei> ZugangslisteAsync(Guid akteurId, Guid stapelId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        var stapel = ablage.Holen(stapelId, a.Id) ?? throw new FachFehler(MeldungAbgelaufen);
        a.PruefeSchule(stapel.SchuleId);
        var kopf = await SchulKopfAsync(stapel.SchuleId, ct);
        var pdf = ZugangslistePdf.Erzeuge(kopf, stapel.Zugaenge, app.Value.Url("/auth/connexion"), new SchulZeit(zeit).Heute);
        return new PdfDatei(pdf, $"acces-{SkoulouKultur.Datum(new SchulZeit(zeit).Heute).Replace('/', '-')}.pdf");
    }

    public async Task<PdfDatei> KartenAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var bogen = await karten.KartenAsync(akteurId, klasseId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var logo = DruckStil.PruefeBild(await BildDatei.LadeAsync(db, speicher, bogen.LogoMediumId, ct));
        var fotos = new Dictionary<Guid, byte[]>();
        foreach (var k in bogen.Karten)
            if (DruckStil.PruefeBild(await BildDatei.LadeAsync(db, speicher, k.FotoMediumId, ct)) is { } foto)
                fotos[k.SchuelerId] = foto;
        var pdf = SchuelerKartenPdf.Erzeuge(bogen, logo, fotos, token => app.Value.Url($"/verification/carte/{token}"));
        return new PdfDatei(pdf, $"cartes-{DateiTeil(bogen.Klasse)}.pdf");
    }

    public async Task<PdfDatei> PlanAsync(Guid akteurId, Guid planId, CancellationToken ct = default)
    {
        var (titel, untertitel, schule, stunden, mitKlasse) = await plaene.PlanPdfAsync(akteurId, planId, ct);
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        var kopf = akteur.SchuleId is { } s ? await SchulKopfAsync(s, ct) : new SchulKopf(schule, null);
        var pdf = StundenplanPdf.Erzeuge(kopf, titel, untertitel, stunden, mitKlasse, new SchulZeit(zeit).Heute);
        return new PdfDatei(pdf, $"{DateiTeil(titel)}.pdf");
    }

    public async Task<PdfDatei> LehrerWocheAsync(Guid akteurId, DateOnly tag, CancellationToken ct = default)
    {
        var (titel, untertitel, schule, stunden, mitKlasse) = await plaene.LehrerPdfAsync(akteurId, tag, ct);
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        var kopf = akteur.SchuleId is { } s ? await SchulKopfAsync(s, ct) : new SchulKopf(schule, null);
        var pdf = StundenplanPdf.Erzeuge(kopf, titel, untertitel, stunden, mitKlasse, new SchulZeit(zeit).Heute);
        return new PdfDatei(pdf, $"emploi-du-temps-{Wochentage.Montag(tag):yyyy-MM-dd}.pdf");
    }

    private async Task<SchulKopf> SchulKopfAsync(Guid schuleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.Schulen.AsNoTracking().Where(x => x.Id == schuleId)
            .Select(x => new { Name = x.OffizielleBezeichnung ?? x.Name, x.LogoMediumId }).FirstAsync(ct);
        return new SchulKopf(s.Name, DruckStil.PruefeBild(await BildDatei.LadeAsync(db, speicher, s.LogoMediumId, ct)));
    }

    /// <summary>Dateinamen-tauglich: « Emploi du temps · 6e A » → « emploi-du-temps-6e-a ».</summary>
    public static string DateiTeil(string text)
    {
        var schluessel = TextNormalisierung.Schluessel(text).Replace(' ', '-');
        return string.IsNullOrEmpty(schluessel) ? "document" : schluessel;
    }
}
