using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Daten einer Schülerkarte (Token = Code im QR der Prüfseite).</summary>
public sealed record KarteDaten(Guid SchuelerId, string Vorname, string Nachname, string? Matrikel, DateOnly? Geburtsdatum, Guid? FotoMediumId, string Token);

/// <summary>Kartenbogen einer Klasse.</summary>
public sealed record KartenBogen(Guid SchuleId, string Schule, Guid? LogoMediumId, string Jahr, string Klasse, IReadOnlyList<KarteDaten> Karten);

/// <summary>Ergebnis der öffentlichen Kartenprüfung – nur Schule, Jahr und Gültigkeit, keine Personendaten.</summary>
public sealed record KartenPruefung(bool Gueltig, string? Schule, string? Jahr);

/// <summary>
/// Schülerkarten (Bauplan §11.9): Kartenbogen je Klasse (aktive Einschreibungen, aktive Konten) mit QR-Code,
/// der auf die Prüfseite <c>/verification/carte/{token}</c> führt. Die Prüfung zeigt keine Personendaten.
/// </summary>
public sealed class KartenService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff)
{
    /// <summary>Karten der Klasse; fehlende Kartencodes werden dabei erzeugt.</summary>
    public async Task<KartenBogen> KartenAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        var jahr = await db.Schuljahre.AsNoTracking().Where(j => j.Id == klasse.SchuljahrId).Select(j => j.Bezeichnung).FirstAsync(ct);
        var schule = await db.Schulen.AsNoTracking().Where(s => s.Id == a.SchuleId)
            .Select(s => new { Name = s.OffizielleBezeichnung ?? s.Name, s.LogoMediumId }).FirstAsync(ct);

        var zeilen = await (from e in SchulAbfragen.AktiveEinschreibungen(db)
                            join k in db.Users on e.SchuelerId equals k.Id
                            where e.KlasseId == klasse.Id && k.Status == KontoStatus.Actif
                            orderby k.Nachname, k.Vorname
                            select new { k.Id, k.Vorname, k.Nachname, k.Geburtsdatum, k.FotoMediumId }).ToListAsync(ct);
        var ids = zeilen.Select(z => z.Id).ToList();
        var profile = await db.SchuelerProfile.Where(p => ids.Contains(p.KontoId)).ToListAsync(ct);
        var neu = false;
        foreach (var id in ids)
        {
            var profil = profile.FirstOrDefault(p => p.KontoId == id);
            if (profil is null)
            {
                profil = new SchuelerProfil { KontoId = id, SchuleId = a.SchuleId };
                db.SchuelerProfile.Add(profil);
                profile.Add(profil);
            }
            if (profil.KartenToken is null)
            {
                profil.KartenToken = SchuelerService.NeuerKartenToken();
                neu = true;
            }
        }
        if (neu || db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        var karten = zeilen.Select(z =>
        {
            var p = profile.First(x => x.KontoId == z.Id);
            return new KarteDaten(z.Id, z.Vorname, z.Nachname, p.Matrikel, z.Geburtsdatum, z.FotoMediumId, p.KartenToken!);
        }).ToList();
        return new KartenBogen(a.SchuleId, schule.Name, schule.LogoMediumId, jahr, klasse.Name, karten);
    }

    /// <summary>
    /// Öffentliche Prüfung eines Kartencodes: gültig, wenn der Schüler aktiv und in einem laufenden oder
    /// vorbereiteten Schuljahr eingeschrieben ist. Unbekannter Code → ungültig ohne Schule.
    /// </summary>
    public async Task<KartenPruefung> PruefeKarteAsync(string? token, CancellationToken ct = default)
    {
        if (!IstTokenFormat(token)) return new KartenPruefung(false, null, null);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var karte = await (from p in db.SchuelerProfile.AsNoTracking()
                           join k in db.Users on p.KontoId equals k.Id
                           join s in db.Schulen on p.SchuleId equals s.Id
                           where p.KartenToken == token
                           select new { k.Id, k.Status, Schule = s.OffizielleBezeichnung ?? s.Name, SchulStatus = s.Status }).FirstOrDefaultAsync(ct);
        if (karte is null) return new KartenPruefung(false, null, null);

        var jahr = await (from e in SchulAbfragen.AktiveEinschreibungen(db)
                          join j in db.Schuljahre on e.SchuljahrId equals j.Id
                          where e.SchuelerId == karte.Id && (j.Status == SchuljahrStatus.Laufend || j.Status == SchuljahrStatus.Vorbereitung)
                          orderby j.Beginn descending
                          select j.Bezeichnung).FirstOrDefaultAsync(ct);
        var gueltig = karte.Status == KontoStatus.Actif && karte.SchulStatus == SchulStatus.Active && jahr is not null;
        return new KartenPruefung(gueltig, karte.Schule, jahr);
    }

    /// <summary>32 Kleinbuchstaben-Hexzeichen (Format von <see cref="SchuelerService.NeuerKartenToken"/>).</summary>
    public static bool IstTokenFormat(string? token)
        => token is { Length: 32 } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
