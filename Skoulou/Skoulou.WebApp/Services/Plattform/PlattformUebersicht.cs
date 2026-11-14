using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Kennzahlen des Tableau de bord (alles per COUNT, nichts in den Speicher geladen).</summary>
public sealed record PlattformZahlen(
    SchulZaehler Schulen, IReadOnlyList<(string Rolle, int Anzahl)> KontenJeRolle, int KontenGesperrt,
    int NeueSchulen7Tage, int NeueKonten7Tage, int SchuelerAValider, int MailsFehlgeschlagen, int MailsWartend,
    int Fehler24h, int NachrichtenUngelesen, IReadOnlyList<SchulZeile> WartendeSchulen);

/// <summary>Eine Schule des Responsable (« Mes écoles »): Status, Schüler, Direktion (Name, E-Mail) und Telefon der Schule.</summary>
public sealed record BetreuteSchule(
    Guid Id, string Name, string Ort, SchulStatus Status, string? StatusGrund, int Schueler, int Personal, int SchuelerAValider,
    string? SchulTelefon, string? SchulEmail, IReadOnlyList<DirektionKontakt> Direktion);

/// <summary>Kontakt der Direktion (keine private Telefonnummer).</summary>
public sealed record DirektionKontakt(string Name, string Rolle, string? Email);

/// <summary>Tableau de bord (SuperAdmin) und « Mes écoles » (Responsable).</summary>
public sealed class PlattformUebersicht(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    public async Task<PlattformZahlen> ZahlenAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var woche = jetzt.AddDays(-7);
        var tag = jetzt.AddHours(-24);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schulen = await SchulVerwaltungService.ZaehleAsync(db, ct);
        var rollen = await db.Users.Where(k => k.Status != KontoStatus.Archive).GroupBy(k => k.Rolle)
            .Select(g => new { g.Key, Anzahl = g.Count() }).ToListAsync(ct);
        var jeRolle = Rollen.Alle.Select(r => (r, rollen.FirstOrDefault(x => x.Key == r)?.Anzahl ?? 0)).ToList();
        var wartend = await PlattformListen.SeiteAsync(
            SchulVerwaltungService.Projektion(db, db.Schulen.AsNoTracking().Where(s => s.Status == SchulStatus.EnAttente).OrderBy(s => s.ErstelltAmUtc)), 0, 5, ct);
        return new PlattformZahlen(
            schulen, jeRolle,
            await db.Users.CountAsync(k => k.Status == KontoStatus.Bloque, ct),
            await db.Schulen.CountAsync(s => s.ErstelltAmUtc >= woche, ct),
            await db.Users.CountAsync(k => k.ErstelltAmUtc >= woche, ct),
            await db.Users.CountAsync(k => k.Rolle == Rollen.Schueler && k.Status == KontoStatus.EnAttente, ct),
            await db.MailAusgaenge.CountAsync(m => m.Status == MailStatus.Fehlgeschlagen && m.ErstelltAmUtc >= woche, ct),
            await db.MailAusgaenge.CountAsync(m => m.Status == MailStatus.Offen && m.Versuche > 0, ct),
            await db.FehlerProtokolle.CountAsync(f => f.ZeitUtc >= tag, ct),
            await db.Nachrichten.CountAsync(n => n.Richtung == NachrichtRichtung.Eingang && n.GelesenAmUtc == null, ct),
            wartend.Eintraege);
    }

    /// <summary>« Mes écoles » des Responsable (SuperAdmin: alle Schulen mit Responsable = er selbst, also leer).</summary>
    public async Task<IReadOnlyList<BetreuteSchule>> MeineSchulenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schulen = await db.Schulen.AsNoTracking().Where(s => s.ResponsableId == akteur.Id).OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id, s.Name, s.Ort, s.Status, s.StatusGrund, s.Telefon, s.Email,
                Schueler = db.Users.Count(k => k.SchuleId == s.Id && k.Rolle == Rollen.Schueler && k.Status == KontoStatus.Actif),
                Personal = db.Users.Count(k => k.SchuleId == s.Id && (k.Rolle == Rollen.Lehrer || k.Rolle == Rollen.Direktor || k.Rolle == Rollen.Koordinator)
                                              && k.Status == KontoStatus.Actif),
                AValider = db.Users.Count(k => k.SchuleId == s.Id && k.Rolle == Rollen.Schueler && k.Status == KontoStatus.EnAttente)
            }).ToListAsync(ct);
        var ids = schulen.Select(s => s.Id).ToList();
        var direktion = (await db.Users.AsNoTracking()
                .Where(k => k.SchuleId != null && ids.Contains(k.SchuleId.Value) && (k.Rolle == Rollen.Direktor || k.Rolle == Rollen.Koordinator)
                            && k.Status != KontoStatus.Archive)
                .OrderBy(k => k.Rolle).ThenBy(k => k.Nachname)
                .Select(k => new { k.SchuleId, Name = k.Vorname + " " + k.Nachname, k.Rolle, k.Email }).ToListAsync(ct))
            .ToLookup(k => k.SchuleId!.Value);
        return schulen.Select(s => new BetreuteSchule(s.Id, s.Name, s.Ort, s.Status, s.StatusGrund, s.Schueler, s.Personal, s.AValider, s.Telefon, s.Email,
            direktion[s.Id].Select(d => new DirektionKontakt(d.Name, Rollen.Bezeichnung(d.Rolle), d.Email)).ToList())).ToList();
    }
}
