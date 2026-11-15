using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record StufeZeile(Guid Id, string Name, int Reihenfolge, Zyklus? Zyklus, Guid? NachfolgerId, string? NachfolgerName, bool Aktiv, int AnzahlKlassen);

public sealed record StufeEingabe(string? Name, Zyklus? Zyklus, Guid? NachfolgerId);

public sealed record KlasseZeile(Guid Id, string Name, Guid? StufeId, string? StufeName, Guid? PeriodeId, string? PeriodeTitel, int? Kapazitaet,
    Guid? KlassenlehrerId, string? KlassenlehrerName, string? Raum, bool Aktiv, int Effectif, int AnzahlFaecher, int FaecherOhneLehrer);

public sealed record KlasseEingabe(string? Name, Guid? StufeId, Guid? PeriodeId, int? Kapazitaet, Guid? KlassenlehrerId, string? Raum);

/// <summary>Eintrag einer Auswahlliste (Id, Anzeige, Zusatz).</summary>
public sealed record AuswahlEintrag(Guid Id, string Name, string? Zusatz = null);

/// <summary>Ergebnis von « Supprimer »: gelöscht (unbenutzt) oder nur deaktiviert (§11.11).</summary>
public enum LoeschErgebnis
{
    Geloescht,
    Deaktiviert
}

/// <summary>Stufen (« Niveaux ») und Klassen eines Schuljahres (Bauplan §4.2, §11.11).</summary>
public sealed class KlassenService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    // ---------------- Stufen ----------------

    public async Task<IReadOnlyList<StufeZeile>> StufenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Stufen.AsNoTracking().Where(s => s.SchuleId == a.SchuleId)
            .OrderBy(s => s.Reihenfolge).ThenBy(s => s.Name)
            .Select(s => new StufeZeile(s.Id, s.Name, s.Reihenfolge, s.Zyklus, s.NachfolgerId,
                db.Stufen.Where(n => n.Id == s.NachfolgerId).Select(n => n.Name).FirstOrDefault(), s.Aktiv,
                db.Klassen.Count(k => k.StufeId == s.Id && k.Aktiv)))
            .ToListAsync(ct);
    }

    public async Task<Guid> StufeSpeichernAsync(Guid akteurId, Guid? stufeId, StufeEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var name = Eingabe.Pflicht(e.Name, 60, "le nom du niveau (ex. 6e, CP1)", "name");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alle = await db.Stufen.Where(s => s.SchuleId == a.SchuleId).ToListAsync(ct);
        if (alle.Any(s => s.Id != stufeId && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new FachFehler($"Le niveau « {name} » existe déjà.", "name");
        if (e.NachfolgerId is { } n && (n == stufeId || alle.All(s => s.Id != n)))
            throw new FachFehler("Choisissez un autre niveau comme niveau suivant.", "nachfolger");

        Stufe stufe;
        if (stufeId is { } id)
        {
            stufe = alle.FirstOrDefault(s => s.Id == id) ?? throw new FachFehler("Ce niveau est introuvable.");
        }
        else
        {
            stufe = new Stufe { SchuleId = a.SchuleId, Reihenfolge = alle.Count == 0 ? 1 : alle.Max(s => s.Reihenfolge) + 1 };
            db.Stufen.Add(stufe);
        }
        stufe.Name = name;
        stufe.Zyklus = e.Zyklus;
        stufe.NachfolgerId = e.NachfolgerId;
        await db.SaveChangesAsync(ct);
        return stufe.Id;
    }

    /// <summary>Löschen, solange unbenutzt; sonst deaktivieren.</summary>
    public async Task<LoeschErgebnis> StufeLoeschenAsync(Guid akteurId, Guid stufeId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var stufe = await db.Stufen.FirstOrDefaultAsync(s => s.Id == stufeId, ct) ?? throw new FachFehler("Ce niveau est introuvable.");
            a.PruefeSchule(stufe.SchuleId);
            var benutzt = await db.Klassen.AnyAsync(k => k.StufeId == stufe.Id, ct)
                          || await db.Lecons.AnyAsync(l => l.StufeId == stufe.Id, ct)
                          || await db.Pruefungen.AnyAsync(p => p.StufeId == stufe.Id, ct)
                          || await db.GebuehrenPosten.AnyAsync(g => g.StufeId == stufe.Id, ct)
                          || await db.BulletinVorlagen.AnyAsync(v => v.StufeId == stufe.Id, ct)
                          || await db.Jahresentscheidungen.AnyAsync(j => j.ZielStufeId == stufe.Id, ct);
            if (benutzt)
            {
                stufe.Aktiv = false;
                await db.SaveChangesAsync(ct);
                return LoeschErgebnis.Deaktiviert;
            }
            foreach (var vorgaenger in await db.Stufen.Where(s => s.NachfolgerId == stufe.Id).ToListAsync(ct)) vorgaenger.NachfolgerId = null;
            await db.SaveChangesAsync(ct);
            db.Stufen.Remove(stufe);
            await db.SaveChangesAsync(ct);
            return LoeschErgebnis.Geloescht;
        }, ct);
    }

    public async Task StufeAktivierenAsync(Guid akteurId, Guid stufeId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stufe = await db.Stufen.FirstOrDefaultAsync(s => s.Id == stufeId, ct) ?? throw new FachFehler("Ce niveau est introuvable.");
        a.PruefeSchule(stufe.SchuleId);
        stufe.Aktiv = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Reihenfolge der Stufen (aufsteigend, z. B. 6e, 5e, 4e …).</summary>
    public async Task StufenReihenfolgeAsync(Guid akteurId, IReadOnlyList<Guid> reihenfolge, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stufen = await db.Stufen.Where(s => s.SchuleId == a.SchuleId).ToListAsync(ct);
        var nr = 0;
        foreach (var id in reihenfolge.Distinct())
            if (stufen.FirstOrDefault(s => s.Id == id) is { } s) s.Reihenfolge = ++nr;
        foreach (var s in stufen.Where(s => !reihenfolge.Contains(s.Id)).OrderBy(s => s.Reihenfolge)) s.Reihenfolge = ++nr;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Klassen ----------------

    public async Task<IReadOnlyList<KlasseZeile>> KlassenAsync(Guid akteurId, Guid jahrId, bool mitInaktiven = true, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        // Effectif = laufende Einschreibungen: eine Session-Klasse hat nach ihrer Session keine Teilnehmer mehr.
        var aktive = SchulAbfragen.LaufendeEinschreibungen(db, new SchulZeit(zeit).Heute);
        return await db.Klassen.AsNoTracking().Where(k => k.SchuljahrId == jahrId && (mitInaktiven || k.Aktiv))
            .OrderBy(k => db.Stufen.Where(s => s.Id == k.StufeId).Select(s => (int?)s.Reihenfolge).FirstOrDefault() ?? 999)
            .ThenBy(k => k.Name)
            .Select(k => new KlasseZeile(k.Id, k.Name, k.StufeId,
                db.Stufen.Where(s => s.Id == k.StufeId).Select(s => s.Name).FirstOrDefault(),
                k.PeriodeId, db.Perioden.Where(p => p.Id == k.PeriodeId).Select(p => p.Titel).FirstOrDefault(),
                k.Kapazitaet, k.KlassenlehrerId,
                db.Users.Where(u => u.Id == k.KlassenlehrerId).Select(u => u.Vorname + " " + u.Nachname).FirstOrDefault(),
                k.Raum, k.Aktiv,
                aktive.Count(e => e.KlasseId == k.Id),
                db.Klassenfaecher.Count(kf => kf.KlasseId == k.Id),
                db.Klassenfaecher.Count(kf => kf.KlasseId == k.Id && !db.Lehrauftraege.Any(l => l.KlassenfachId == kf.Id))))
            .ToListAsync(ct);
    }

    /// <summary>Aktive Klassen eines Jahres für Auswahllisten (Zusatz: Effectif/Kapazität).</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> KlassenAuswahlAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        // Effectif = laufende Einschreibungen: eine Session-Klasse hat nach ihrer Session keine Teilnehmer mehr.
        var aktive = SchulAbfragen.LaufendeEinschreibungen(db, new SchulZeit(zeit).Heute);
        var liste = await db.Klassen.AsNoTracking().Where(k => k.SchuljahrId == jahrId && k.Aktiv)
            .OrderBy(k => db.Stufen.Where(s => s.Id == k.StufeId).Select(s => (int?)s.Reihenfolge).FirstOrDefault() ?? 999).ThenBy(k => k.Name)
            .Select(k => new { k.Id, k.Name, k.Kapazitaet, Effectif = aktive.Count(e => e.KlasseId == k.Id) })
            .ToListAsync(ct);
        return liste.Select(k => new AuswahlEintrag(k.Id, k.Name,
            k.Kapazitaet is { } kap ? $"{k.Effectif}/{kap} élèves" : $"{k.Effectif} élève{(k.Effectif > 1 ? "s" : "")}")).ToList();
    }

    /// <summary>Lehrkräfte der Schule (Enseignants, Direction, Coordination) – für Professeur principal und Lehraufträge.</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> LehrkraefteAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var liste = await db.Users.AsNoTracking()
            .Where(k => k.SchuleId == a.SchuleId && OrgRollen.Lehrkraefte.Contains(k.Rolle) && k.Status != KontoStatus.Archive)
            .OrderBy(k => k.Nachname).ThenBy(k => k.Vorname)
            .Select(k => new { k.Id, k.Vorname, k.Nachname, k.Rolle, k.Status }).ToListAsync(ct);
        return liste.Select(k => new AuswahlEintrag(k.Id, $"{k.Vorname} {k.Nachname}".Trim(),
            k.Rolle == Rollen.Lehrer ? (k.Status == KontoStatus.Bloque ? "bloqué" : null) : Rollen.Bezeichnung(k.Rolle))).ToList();
    }

    public async Task<Guid> KlasseSpeichernAsync(Guid akteurId, Guid jahrId, Guid? klasseId, KlasseEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var name = Eingabe.Pflicht(e.Name, 60, "le nom de la classe (ex. 6e A)", "name");
        var raum = Eingabe.Text(e.Raum, 80, "la salle");
        if (e.Kapazitaet is { } kap && (kap < 1 || kap > 10_000)) throw new FachFehler("La capacité doit être comprise entre 1 et 10 000 élèves.", "kapazitaet");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        OrgAbfragen.PruefeOffen(jahr);
        if (await db.Klassen.AnyAsync(k => k.SchuljahrId == jahr.Id && k.Id != klasseId && k.Name == name, ct))
            throw new FachFehler($"La classe « {name} » existe déjà en {jahr.Bezeichnung}.", "name");
        if (e.StufeId is { } stufeId && !await db.Stufen.AnyAsync(s => s.Id == stufeId && s.SchuleId == a.SchuleId, ct))
            throw new FachFehler("Ce niveau est introuvable.", "stufe");
        if (e.PeriodeId is { } periodeId && !await db.Perioden.AnyAsync(p => p.Id == periodeId && p.SchuljahrId == jahr.Id, ct))
            throw new FachFehler("Choisissez une période de cette année scolaire.", "periode");
        if (e.KlassenlehrerId is { } lehrerId && !await db.Users.AnyAsync(k => k.Id == lehrerId && k.SchuleId == a.SchuleId
                && OrgRollen.Lehrkraefte.Contains(k.Rolle) && k.Status != KontoStatus.Archive, ct))
            throw new FachFehler("Le professeur principal doit être un enseignant actif de l'établissement.", "klassenlehrer");

        Klasse klasse;
        if (klasseId is { } id)
        {
            klasse = await OrgAbfragen.KlasseAsync(db, a, id, ct);
            if (klasse.SchuljahrId != jahr.Id) throw new FachFehler("Cette classe appartient à une autre année scolaire.");
        }
        else
        {
            klasse = new Klasse { SchuleId = a.SchuleId, SchuljahrId = jahr.Id };
            db.Klassen.Add(klasse);
        }
        klasse.Name = name;
        klasse.StufeId = e.StufeId;
        klasse.PeriodeId = e.PeriodeId;
        klasse.Kapazitaet = e.Kapazitaet;
        klasse.KlassenlehrerId = e.KlassenlehrerId;
        klasse.Raum = raum;
        await db.SaveChangesAsync(ct);
        return klasse.Id;
    }

    /// <summary>Löschen, solange unbenutzt (nur Fächer/Lehraufträge ohne Daten); sonst deaktivieren.</summary>
    public async Task<LoeschErgebnis> KlasseLoeschenAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
            var kfIds = await db.Klassenfaecher.Where(kf => kf.KlasseId == klasse.Id).Select(kf => kf.Id).ToListAsync(ct);
            var benutzt = await db.Einschreibungen.AnyAsync(e => e.KlasseId == klasse.Id, ct)
                          || await db.Stundenplaene.AnyAsync(s => s.KlasseId == klasse.Id, ct)
                          || await db.Zuweisungen.AnyAsync(z => z.KlasseId == klasse.Id, ct)
                          || await db.GebuehrenPosten.AnyAsync(g => g.KlasseId == klasse.Id, ct)
                          || await db.KlassenVeroeffentlichungen.AnyAsync(v => v.KlasseId == klasse.Id, ct)
                          || await db.Bewertungen.AnyAsync(b => kfIds.Contains(b.KlassenfachId), ct)
                          || await db.FachBemerkungen.AnyAsync(b => kfIds.Contains(b.KlassenfachId), ct);
            if (benutzt)
            {
                klasse.Aktiv = false;
                await db.SaveChangesAsync(ct);
                return LoeschErgebnis.Deaktiviert;
            }
            db.Lehrauftraege.RemoveRange(await db.Lehrauftraege.Where(l => kfIds.Contains(l.KlassenfachId)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.Klassenfaecher.RemoveRange(await db.Klassenfaecher.Where(kf => kf.KlasseId == klasse.Id).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.Klassen.Remove(klasse);
            await db.SaveChangesAsync(ct);
            return LoeschErgebnis.Geloescht;
        }, ct);
    }

    public async Task KlasseAktivierenAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        if (await db.Klassen.AnyAsync(k => k.SchuljahrId == klasse.SchuljahrId && k.Id != klasse.Id && k.Aktiv && k.Name == klasse.Name, ct))
            throw new FachFehler($"Une classe active « {klasse.Name} » existe déjà.");
        klasse.Aktiv = true;
        await db.SaveChangesAsync(ct);
    }
}
