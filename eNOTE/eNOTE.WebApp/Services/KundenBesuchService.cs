using ENote.Core.DataModels.Kasse;
using ENote.Core.Services.Kunden;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Anwesende Kunden (« Client présent pour …, salle ») und Kundenverlauf (Besuche + Notizen, neueste zuerst).
/// Lesen und Schreiben: Besitzer und Mitarbeitende des Ladens. Notizen löschen: Verfasser oder Besitzer.
/// Ein offener Besuch endet von Hand oder beim Encaissement mit diesem Kunden (<see cref="BeendeOffeneAsync"/>).
/// </summary>
public class KundenBesuchService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit, AnwesenheitsSignal signal)
{
    public sealed record Anwesend(Guid BesuchId, Guid KundeId, string Name, string Grund, string? Raum, DateTime AnkunftUtc,
        string ErfasstVon, string? LetzteNotiz, int Notizen, bool Vergessen);

    public sealed record NotizEintrag(Guid Id, string Text, DateTime ErstelltUtc, string Von, bool DarfLoeschen);

    public sealed record BesuchEintrag(Guid Id, string Grund, string? Raum, DateTime AnkunftUtc, DateTime? EndeUtc, string ErfasstVon,
        bool MitVerkauf, IReadOnlyList<NotizEintrag> Notizen);

    /// <summary>Ein Verlaufseintrag: entweder ein Besuch (mit seinen Notizen) oder eine allgemeine Notiz.</summary>
    public sealed record VerlaufEintrag(DateTime ZeitUtc, BesuchEintrag? Besuch, NotizEintrag? Notiz);

    public sealed record Fiche(Guid KundeId, string Name, string? Telefon, BesuchEintrag? Offen, int AnzahlBesuche,
        IReadOnlyList<VerlaufEintrag> Verlauf, bool IstBesitzer);

    /// <summary>Gruende/Raeume = Knöpfe; Katalog = alle Prestations für die Autovervollständigung im Feld « Présent pour ».</summary>
    public sealed record Vorschlaege(IReadOnlyList<string> Gruende, IReadOnlyList<string> Raeume, IReadOnlyList<string>? Katalog = null);

    public sealed record Raum(Guid Id, string Name, int Reihenfolge);

    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ Zugriff

    private sealed record Rolle(bool Besitzer, string Name);

    private static async Task<Rolle> PruefeZugangAsync(ApplicationDbContext db, Guid user, Guid salonId, CancellationToken ct)
    {
        if (!await LadenZugriff.HatZugangAsync(db, user, salonId, ct)) throw new UnauthorizedAccessException("Accès refusé.");
        var besitzer = await LadenZugriff.IstBesitzerAsync(db, user, salonId, ct);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == user)
            .Select(u => u.FullName != "" ? u.FullName : (u.UserName ?? "")).FirstOrDefaultAsync(ct) ?? "";
        return new Rolle(besitzer, name);
    }

    private static async Task<Kunde> KundeAsync(ApplicationDbContext db, Guid kundeId, CancellationToken ct)
        => await db.Kunden.AsNoTracking().FirstOrDefaultAsync(k => k.Id == kundeId, ct) ?? throw new ArgumentException("Client introuvable.");

    // ------------------------------------------------------------------ Anwesenheit

    /// <summary>Alle Kunden, die gerade da sind (älteste Ankunft zuerst = wartet am längsten).</summary>
    public async Task<List<Anwesend>> GetAnwesendeAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, ct);
        var jetzt = Jetzt;
        var offen = await db.KundenBesuche.AsNoTracking()
            .Where(b => b.SalonId == salonId && b.EndeUtc == null)
            .OrderBy(b => b.AnkunftUtc)
            .Select(b => new { b.Id, b.KundeId, b.Kunde.Name, b.Grund, b.Raum, b.AnkunftUtc, b.ErfasstVonName })
            .Take(200).ToListAsync(ct);
        var ids = offen.Select(o => o.Id).ToList();
        var notizen = await db.KundenNotizen.AsNoTracking()
            .Where(n => n.BesuchId != null && ids.Contains(n.BesuchId.Value))
            .Select(n => new { BesuchId = n.BesuchId!.Value, n.Text, n.ErstelltUtc })
            .ToListAsync(ct);
        return offen.Select(o =>
        {
            var eigene = notizen.Where(n => n.BesuchId == o.Id).OrderByDescending(n => n.ErstelltUtc).ToList();
            return new Anwesend(o.Id, o.KundeId, o.Name, o.Grund, o.Raum, o.AnkunftUtc, o.ErfasstVonName,
                eigene.FirstOrDefault()?.Text, eigene.Count, jetzt - o.AnkunftUtc > KundenBesuchRegeln.VergessenNach);
        }).ToList();
    }

    /// <summary>Anzahl offener Besuche (für Hinweise in Kasse und Navigation).</summary>
    public async Task<int> ZaehleAnwesendeAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await LadenZugriff.HatZugangAsync(db, user, salonId, ct)) return 0;
        return await db.KundenBesuche.CountAsync(b => b.SalonId == salonId && b.EndeUtc == null, ct);
    }

    /// <summary>
    /// Schnellauswahl für « Nouvelle arrivée »:
    /// Motive = Prestations des Katalogs (aktiv, ohne Produkte und « Divers »), Favoriten zuerst, dann die meistverkauften
    /// der letzten 90 Tage, dann nach Name (höchstens <see cref="KundenBesuchRegeln.MotiveMax"/>);
    /// Räume = vom Besitzer festgelegte Räume in ihrer Reihenfolge.
    /// Ohne Prestations bzw. ohne festgelegte Räume: die häufigsten Einträge der letzten 90 Tage (wie bisher).
    /// </summary>
    public async Task<Vorschlaege> GetVorschlaegeAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, ct);
        var seit = Jetzt.AddDays(-90);
        var letzte = await db.KundenBesuche.AsNoTracking()
            .Where(b => b.SalonId == salonId && b.AnkunftUtc >= seit)
            .OrderByDescending(b => b.AnkunftUtc).Take(500)
            .Select(b => new { b.Grund, b.Raum }).ToListAsync(ct);
        static List<string> Haeufigste(IEnumerable<string?> werte, int n) => werte
            .Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w!)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(n).Select(g => g.First()).ToList();

        var prestations = await db.Leistungen.AsNoTracking()
            .Where(l => l.SalonId == salonId && l.IstAktiv && !l.IstStandardArtikel && l.Typ == ENote.Core.DataModels.Katalog.LeistungsTyp.Prestation)
            .Select(l => new { l.Id, l.Name }).ToListAsync(ct);
        List<string> motive;
        if (prestations.Count == 0)
        {
            motive = Haeufigste(letzte.Select(l => l.Grund), 6);
        }
        else
        {
            var ids = prestations.Select(p => p.Id).ToList();
            var favoriten = (await db.LeistungFavoriten.AsNoTracking().Where(f => f.SalonId == salonId).Select(f => f.LeistungId).ToListAsync(ct)).ToHashSet();
            var verkauft = await db.VerkaufsPositionen.AsNoTracking()
                .Where(p => ids.Contains(p.LeistungId) && p.Verkauf.SalonId == salonId && p.Verkauf.ZeitUtc >= seit)
                .GroupBy(p => p.LeistungId).Select(g => new { g.Key, Menge = g.Sum(p => p.Menge) }).ToDictionaryAsync(x => x.Key, x => x.Menge, ct);
            motive = prestations
                .OrderByDescending(p => favoriten.Contains(p.Id))
                .ThenByDescending(p => verkauft.GetValueOrDefault(p.Id))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(KundenBesuchRegeln.MotiveMax).Select(p => p.Name).ToList();
        }

        var festgelegt = await db.SalonRaeume.AsNoTracking().Where(r => r.SalonId == salonId)
            .OrderBy(r => r.Reihenfolge).ThenBy(r => r.Name).Select(r => r.Name).ToListAsync(ct);
        var raeume = festgelegt.Count > 0 ? festgelegt : Haeufigste(letzte.Select(l => l.Raum), 10);
        var katalog = prestations.Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        return new Vorschlaege(motive, raeume, katalog);
    }

    // ------------------------------------------------------------------ Räume des Ladens (Paramètres › Salles et postes)

    /// <summary>Festgelegte Räume in ihrer Reihenfolge (lesen: alle Mitarbeitenden).</summary>
    public async Task<List<Raum>> GetRaeumeAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, ct);
        return await db.SalonRaeume.AsNoTracking().Where(r => r.SalonId == salonId)
            .OrderBy(r => r.Reihenfolge).ThenBy(r => r.Name)
            .Select(r => new Raum(r.Id, r.Name, r.Reihenfolge)).ToListAsync(ct);
    }

    private static async Task PruefeBesitzerAsync(ApplicationDbContext db, Guid user, Guid salonId, CancellationToken ct)
    {
        if (!(await PruefeZugangAsync(db, user, salonId, ct)).Besitzer)
            throw new UnauthorizedAccessException("Seul le propriétaire peut modifier les salles.");
    }

    private static async Task PruefeEindeutigAsync(ApplicationDbContext db, Guid salonId, string name, Guid? ausser, CancellationToken ct)
    {
        var namen = await db.SalonRaeume.Where(r => r.SalonId == salonId && r.Id != ausser).Select(r => r.Name).ToListAsync(ct);
        if (namen.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"« {name} » existe déjà.");
    }

    public async Task<Raum> RaumAnlegenAsync(Guid user, Guid salonId, string name, CancellationToken ct = default)
    {
        var n = KundenBesuchRegeln.PruefeRaumName(name);
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var vorhandene = await db.SalonRaeume.Where(r => r.SalonId == salonId).Select(r => r.Reihenfolge).ToListAsync(ct);
        if (vorhandene.Count >= KundenBesuchRegeln.RaeumeMax) throw new ArgumentException($"{KundenBesuchRegeln.RaeumeMax} salles au maximum.");
        await PruefeEindeutigAsync(db, salonId, n, null, ct);
        var raum = new SalonRaum { SalonId = salonId, Name = n, Reihenfolge = vorhandene.Count == 0 ? 0 : vorhandene.Max() + 1, ErstelltUtc = Jetzt };
        db.SalonRaeume.Add(raum);
        await db.SaveChangesAsync(ct);
        return new Raum(raum.Id, raum.Name, raum.Reihenfolge);
    }

    public async Task RaumUmbenennenAsync(Guid user, Guid raumId, string name, CancellationToken ct = default)
    {
        var n = KundenBesuchRegeln.PruefeRaumName(name);
        await using var db = await factory.CreateDbContextAsync(ct);
        var raum = await db.SalonRaeume.FirstOrDefaultAsync(r => r.Id == raumId, ct) ?? throw new ArgumentException("Salle introuvable.");
        await PruefeBesitzerAsync(db, user, raum.SalonId, ct);
        if (raum.Name == n) return;
        await PruefeEindeutigAsync(db, raum.SalonId, n, raum.Id, ct);
        raum.Name = n;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Raum eine Stelle nach oben (richtung &lt; 0) oder unten (&gt; 0) schieben.</summary>
    public async Task RaumVerschiebenAsync(Guid user, Guid raumId, int richtung, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var raum = await db.SalonRaeume.FirstOrDefaultAsync(r => r.Id == raumId, ct) ?? throw new ArgumentException("Salle introuvable.");
        await PruefeBesitzerAsync(db, user, raum.SalonId, ct);
        var liste = await db.SalonRaeume.Where(r => r.SalonId == raum.SalonId).OrderBy(r => r.Reihenfolge).ThenBy(r => r.Name).ToListAsync(ct);
        var i = liste.FindIndex(r => r.Id == raumId);
        var j = i + Math.Sign(richtung);
        if (richtung == 0 || j < 0 || j >= liste.Count) return;
        (liste[i], liste[j]) = (liste[j], liste[i]);
        for (var k = 0; k < liste.Count; k++) liste[k].Reihenfolge = k;
        await db.SaveChangesAsync(ct);
    }

    public async Task RaumLoeschenAsync(Guid user, Guid raumId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var raum = await db.SalonRaeume.FirstOrDefaultAsync(r => r.Id == raumId, ct);
        if (raum is null) return;
        await PruefeBesitzerAsync(db, user, raum.SalonId, ct);
        db.SalonRaeume.Remove(raum);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>« Client présent pour … » – optional mit Raum und erster Notiz.</summary>
    public async Task<Guid> AnkunftAsync(Guid user, Guid salonId, Guid kundeId, string grund, string? raum, string? notiz = null, CancellationToken ct = default)
    {
        var (g, r) = KundenBesuchRegeln.PruefeBesuch(grund, raum);
        var text = string.IsNullOrWhiteSpace(notiz) ? null : KundenBesuchRegeln.PruefeNotiz(notiz);
        await using var db = await factory.CreateDbContextAsync(ct);
        var rolle = await PruefeZugangAsync(db, user, salonId, ct);
        var kunde = await KundeAsync(db, kundeId, ct);
        if (kunde.SalonId != salonId) throw new ArgumentException("Client introuvable.");
        if (await db.KundenBesuche.AnyAsync(b => b.KundeId == kundeId && b.EndeUtc == null, ct))
            throw new ArgumentException($"« {kunde.Name} » est déjà enregistré comme présent.");

        var jetzt = Jetzt;
        var besuch = new KundenBesuch
        {
            SalonId = salonId, KundeId = kundeId, Grund = g, Raum = r, AnkunftUtc = jetzt,
            ErfasstVonId = user, ErfasstVonName = rolle.Name
        };
        db.KundenBesuche.Add(besuch);
        if (text is not null)
        {
            db.KundenNotizen.Add(new KundenNotiz
            {
                SalonId = salonId, KundeId = kundeId, BesuchId = besuch.Id, Text = text,
                ErstelltUtc = jetzt, ErstelltVonId = user, ErstelltVonName = rolle.Name
            });
        }
        await db.SaveChangesAsync(ct);
        signal.Melde(salonId);
        return besuch.Id;
    }

    /// <summary>Anlass oder Raum ändern (z. B. Wechsel in einen anderen Raum).</summary>
    public async Task AendereBesuchAsync(Guid user, Guid besuchId, string grund, string? raum, CancellationToken ct = default)
    {
        var (g, r) = KundenBesuchRegeln.PruefeBesuch(grund, raum);
        await using var db = await factory.CreateDbContextAsync(ct);
        var besuch = await db.KundenBesuche.FirstOrDefaultAsync(b => b.Id == besuchId, ct) ?? throw new ArgumentException("Passage introuvable.");
        await PruefeZugangAsync(db, user, besuch.SalonId, ct);
        if (besuch.EndeUtc is not null) throw new ArgumentException("Ce passage est déjà terminé.");
        besuch.Grund = g;
        besuch.Raum = r;
        await db.SaveChangesAsync(ct);
        signal.Melde(besuch.SalonId);
    }

    /// <summary>Kunde ist gegangen.</summary>
    public async Task BeendeAsync(Guid user, Guid besuchId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var besuch = await db.KundenBesuche.FirstOrDefaultAsync(b => b.Id == besuchId, ct) ?? throw new ArgumentException("Passage introuvable.");
        await PruefeZugangAsync(db, user, besuch.SalonId, ct);
        if (besuch.EndeUtc is not null) return;
        besuch.EndeUtc = Jetzt;
        await db.SaveChangesAsync(ct);
        signal.Melde(besuch.SalonId);
    }

    /// <summary>Versehentliche Ankunft zurücknehmen (nur offen; Erfasser oder Besitzer). Notizen des Besuchs bleiben als allgemeine Notizen.</summary>
    public async Task AnnuliereAsync(Guid user, Guid besuchId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var besuch = await db.KundenBesuche.FirstOrDefaultAsync(b => b.Id == besuchId, ct) ?? throw new ArgumentException("Passage introuvable.");
        var rolle = await PruefeZugangAsync(db, user, besuch.SalonId, ct);
        if (besuch.EndeUtc is not null) throw new ArgumentException("Ce passage est déjà terminé.");
        if (!rolle.Besitzer && besuch.ErfasstVonId != user)
            throw new UnauthorizedAccessException("Seule la personne qui a enregistré l'arrivée (ou le propriétaire) peut l'annuler.");
        foreach (var n in await db.KundenNotizen.Where(n => n.BesuchId == besuch.Id).ToListAsync(ct)) n.BesuchId = null;
        db.KundenBesuche.Remove(besuch);
        await db.SaveChangesAsync(ct);
        signal.Melde(besuch.SalonId);
    }

    /// <summary>
    /// Beim Encaissement: offene Besuche dieses Kunden enden mit dem Verkauf. Speichert nicht selbst
    /// (läuft im selben SaveChanges wie der Verkauf). Gibt die Anzahl beendeter Besuche zurück.
    /// </summary>
    public static async Task<int> BeendeOffeneAsync(ApplicationDbContext db, Guid salonId, Guid kundeId, Guid verkaufId, DateTime jetztUtc, CancellationToken ct)
    {
        var offen = await db.KundenBesuche.Where(b => b.SalonId == salonId && b.KundeId == kundeId && b.EndeUtc == null).ToListAsync(ct);
        foreach (var b in offen)
        {
            b.EndeUtc = jetztUtc;
            b.VerkaufId = verkaufId;
        }
        return offen.Count;
    }

    // ------------------------------------------------------------------ Notizen und Verlauf

    public async Task NotizAsync(Guid user, Guid kundeId, string text, Guid? besuchId = null, CancellationToken ct = default)
    {
        var t = KundenBesuchRegeln.PruefeNotiz(text);
        await using var db = await factory.CreateDbContextAsync(ct);
        var kunde = await KundeAsync(db, kundeId, ct);
        var rolle = await PruefeZugangAsync(db, user, kunde.SalonId, ct);
        if (besuchId is Guid bid && !await db.KundenBesuche.AnyAsync(b => b.Id == bid && b.KundeId == kundeId, ct))
            throw new ArgumentException("Passage introuvable.");
        db.KundenNotizen.Add(new KundenNotiz
        {
            SalonId = kunde.SalonId, KundeId = kundeId, BesuchId = besuchId, Text = t,
            ErstelltUtc = Jetzt, ErstelltVonId = user, ErstelltVonName = rolle.Name
        });
        await db.SaveChangesAsync(ct);
        if (besuchId is not null) signal.Melde(kunde.SalonId);
    }

    public async Task LoescheNotizAsync(Guid user, Guid notizId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var notiz = await db.KundenNotizen.FirstOrDefaultAsync(n => n.Id == notizId, ct) ?? throw new ArgumentException("Note introuvable.");
        var rolle = await PruefeZugangAsync(db, user, notiz.SalonId, ct);
        if (!rolle.Besitzer && notiz.ErstelltVonId != user)
            throw new UnauthorizedAccessException("Seul l'auteur de la note (ou le propriétaire) peut la supprimer.");
        db.KundenNotizen.Remove(notiz);
        await db.SaveChangesAsync(ct);
        if (notiz.BesuchId is not null) signal.Melde(notiz.SalonId);
    }

    /// <summary>Kundenfiche für Kasse, Empfang und Besitzer: offener Besuch, Verlauf (neueste zuerst).</summary>
    public async Task<Fiche> GetFicheAsync(Guid user, Guid kundeId, int maxEintraege = 40, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var kunde = await KundeAsync(db, kundeId, ct);
        var rolle = await PruefeZugangAsync(db, user, kunde.SalonId, ct);

        var besuche = await db.KundenBesuche.AsNoTracking().Where(b => b.KundeId == kundeId)
            .OrderByDescending(b => b.AnkunftUtc).Take(maxEintraege).ToListAsync(ct);
        var anzahl = await db.KundenBesuche.CountAsync(b => b.KundeId == kundeId, ct);
        var besuchIds = besuche.Select(b => b.Id).ToList();
        var notizen = await db.KundenNotizen.AsNoTracking()
            .Where(n => n.KundeId == kundeId && (n.BesuchId == null || besuchIds.Contains(n.BesuchId.Value)))
            .OrderByDescending(n => n.ErstelltUtc).Take(maxEintraege * 4).ToListAsync(ct);

        NotizEintrag Eintrag(KundenNotiz n) => new(n.Id, n.Text, n.ErstelltUtc, n.ErstelltVonName,
            rolle.Besitzer || n.ErstelltVonId == user);
        BesuchEintrag Besuch(KundenBesuch b) => new(b.Id, b.Grund, b.Raum, b.AnkunftUtc, b.EndeUtc, b.ErfasstVonName, b.VerkaufId is not null,
            notizen.Where(n => n.BesuchId == b.Id).Select(Eintrag).ToList());

        var verlauf = besuche.Select(b => new VerlaufEintrag(b.AnkunftUtc, Besuch(b), null))
            .Concat(notizen.Where(n => n.BesuchId == null).Select(n => new VerlaufEintrag(n.ErstelltUtc, null, Eintrag(n))))
            .OrderByDescending(e => e.ZeitUtc).Take(maxEintraege).ToList();
        var offen = besuche.FirstOrDefault(b => b.EndeUtc is null);

        var telefon = rolle.Besitzer ? kunde.Telefon : KundenService.Gekuerzt(kunde.TelefonZiffern);
        return new Fiche(kunde.Id, kunde.Name, telefon, offen is null ? null : Besuch(offen), anzahl, verlauf, rolle.Besitzer);
    }
}
