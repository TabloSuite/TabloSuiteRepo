using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>
/// Demo-Daten des Bereichs E3 Organisation (nach der Grund-Demo-Schule; nur erfundene Daten):
/// veröffentlichte Stundenpläne für 6e A und 6e B, ein Entwurf für 5e A, Appelle der letzten Schultage mit
/// einigen Abwesenheiten/Verspätungen, eine offene Selbstregistrierung und ein archivierter Schüler nach Austritt.
/// </summary>
public static class DemoSeedOrganisation
{
    private static readonly (TimeOnly Beginn, TimeOnly Ende)[] Stunden =
    [
        (new(7, 30), new(8, 30)), (new(8, 30), new(9, 30)), (new(9, 45), new(10, 45)), (new(10, 45), new(11, 45)),
        (new(15, 0), new(16, 0)), (new(16, 0), new(17, 0)),
    ];

    /// <param name="dienste">Dienste der App (DbContextFactory, UserManager, Bereichsdienste …).</param>
    /// <param name="schuleId">Demo-Schule « Collège Démo Les Palmiers ».</param>
    /// <param name="neueKonten">Neu angelegte Demo-Konten (Passwörter landen nur in demo-konten.json).</param>
    public static async Task ErgaenzenAsync(IServiceProvider dienste, Guid schuleId, List<DemoKonto> neueKonten, CancellationToken ct)
    {
        var dbFactory = dienste.GetRequiredService<IDbContextFactory<SkoulouDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await db.Schuljahre.FirstOrDefaultAsync(j => j.SchuleId == schuleId && j.Status == SchuljahrStatus.Laufend, ct);
        if (jahr is null || await db.Stundenplaene.AnyAsync(p => p.SchuleId == schuleId, ct)) return;
        var klassen = await db.Klassen.Where(k => k.SchuljahrId == jahr.Id).ToDictionaryAsync(k => k.Name, ct);

        StundenplaeneAnlegen(db, schuleId, jahr, klassen, await AuftraegeAsync(db, schuleId, ct));
        await db.SaveChangesAsync(ct);

        await AppelleAnlegenAsync(db, schuleId, jahr, klassen, dienste.GetRequiredService<TimeProvider>(), ct);
        await db.SaveChangesAsync(ct);

        var users = dienste.GetRequiredService<UserManager<Konto>>();
        await OffeneAnmeldungAsync(db, users, schuleId, neueKonten, ct);
        await AusgetretenAsync(db, users, schuleId, jahr, klassen["6e B"], neueKonten, ct);
        await db.SaveChangesAsync(ct);
    }

    private sealed record Auftrag(Guid Id, Guid KlasseId, string Fach, Guid LehrerId, decimal Stunden);

    private static Task<List<Auftrag>> AuftraegeAsync(SkoulouDbContext db, Guid schuleId, CancellationToken ct)
        => (from l in db.Lehrauftraege
            join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
            join f in db.Faecher on kf.FachId equals f.Id
            where l.SchuleId == schuleId
            orderby f.Reihenfolge
            select new Auftrag(l.Id, kf.KlasseId, f.Name, l.LehrerId, l.Wochenstunden)).ToListAsync(ct);

    /// <summary>
    /// Verteilt die Wochenstunden gierig auf die Zeitfenster (Mo–Fr): je Fenster höchstens eine Stunde je Klasse und
    /// je Lehrkraft, ein Fach höchstens zweimal am Tag. Was nicht passt, bleibt offen (Soll/Ist zeigt es).
    /// </summary>
    private static void StundenplaeneAnlegen(SkoulouDbContext db, Guid schuleId, Schuljahr jahr, Dictionary<string, Klasse> klassen, List<Auftrag> auftraege)
    {
        var belegt = new HashSet<(DayOfWeek, int, Guid)>();
        foreach (var (name, veroeffentlicht) in new[] { ("6e A", true), ("6e B", true), ("5e A", false) })
        {
            if (!klassen.TryGetValue(name, out var klasse)) continue;
            var plan = new Stundenplan { SchuleId = schuleId, KlasseId = klasse.Id, GueltigAb = jahr.Beginn, Veroeffentlicht = veroeffentlicht };
            db.Stundenplaene.Add(plan);
            var offen = auftraege.Where(a => a.KlasseId == klasse.Id).ToDictionary(a => a, a => (int)Math.Floor(a.Stunden));
            if (!veroeffentlicht) foreach (var a in offen.Keys.ToList()) offen[a] = Math.Min(offen[a], 2); // Entwurf: erst ein Teil eingetragen
            // Fenster für Fenster über alle Tage, damit sich die Stunden gleichmäßig auf die Woche verteilen.
            var proTag = new Dictionary<(int, Auftrag), int>();
            for (var s = 0; s < Stunden.Length; s++)
            {
                for (var t = 0; t < 5; t++)
                {
                    var tag = (DayOfWeek)(t + 1);
                    var wahl = offen.Where(x => x.Value > 0 && !belegt.Contains((tag, s, x.Key.LehrerId)) && proTag.GetValueOrDefault((t, x.Key)) < 2)
                        .OrderByDescending(x => x.Value).ThenBy(x => (t + s) % 3).Select(x => x.Key).FirstOrDefault();
                    if (wahl is null) continue;
                    offen[wahl]--;
                    proTag[(t, wahl)] = proTag.GetValueOrDefault((t, wahl)) + 1;
                    if (veroeffentlicht) belegt.Add((tag, s, wahl.LehrerId));
                    db.StundenplanEintraege.Add(new StundenplanEintrag
                    {
                        StundenplanId = plan.Id, LehrauftragId = wahl.Id, Wochentag = tag, Beginn = Stunden[s].Beginn, Ende = Stunden[s].Ende,
                        Raum = wahl.Fach == "EPS" ? "Terrain" : klasse.Raum
                    });
                }
            }
        }
    }

    /// <summary>Appelle der letzten fünf Schultage (6e A, 6e B) mit einigen Abwesenheiten und Verspätungen.</summary>
    private static async Task AppelleAnlegenAsync(SkoulouDbContext db, Guid schuleId, Schuljahr jahr, Dictionary<string, Klasse> klassen, TimeProvider zeit, CancellationToken ct)
    {
        var heute = new SchulZeit(zeit).Heute;
        var erfasser = await db.Users.Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Lehrer).OrderBy(k => k.Nachname).Select(k => k.Id).FirstAsync(ct);
        var tage = new List<DateOnly>();
        for (var d = heute.AddDays(-1); tage.Count < 5 && d >= jahr.Beginn; d = d.AddDays(-1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) tage.Add(d);

        var n = 0;
        foreach (var name in new[] { "6e A", "6e B" })
        {
            var einschreibungen = await db.Einschreibungen.Where(e => e.KlasseId == klassen[name].Id && e.Status == EinschreibungsStatus.Active)
                .OrderBy(e => e.Id).Select(e => e.Id).ToListAsync(ct);
            foreach (var tag in tage)
                foreach (var e in einschreibungen)
                {
                    n++;
                    var (status, minuten, entschuldigt, bemerkung) = (n % 17) switch
                    {
                        3 => (AnwesenheitsStatus.Absent, (int?)null, true, "Malade, mot des parents."),
                        8 => (AnwesenheitsStatus.Absent, null, false, (string?)null),
                        12 => (AnwesenheitsStatus.Retard, 15, false, "Arrivé après la première heure."),
                        _ => (AnwesenheitsStatus.Present, null, false, null)
                    };
                    db.Anwesenheiten.Add(new Anwesenheit
                    {
                        SchuleId = schuleId, EinschreibungId = e, Datum = tag, Status = status, Minuten = minuten, Entschuldigt = entschuldigt,
                        Bemerkung = bemerkung, ErfasstVonId = erfasser
                    });
                }
        }
    }

    /// <summary>Selbst registrierter Schüler, der auf die Validierung wartet.</summary>
    private static async Task OffeneAnmeldungAsync(SkoulouDbContext db, UserManager<Konto> users, Guid schuleId, List<DemoKonto> neu, CancellationToken ct)
    {
        var konto = await KontoAsync(users, neu, "Yawo", "Mensah", Geschlecht.Maennlich, schuleId, KontoStatus.EnAttente, new DateOnly(2014, 5, 21));
        if (konto is null) return;
        ct.ThrowIfCancellationRequested();
        db.SchuelerProfile.Add(new SchuelerProfil { KontoId = konto.Id, SchuleId = schuleId, Geburtsort = "Tsévié", Nationalitaet = "Togolaise", ElternTelefon = "+228 00 00 01 01" });
    }

    /// <summary>Schüler, der im Oktober die Schule verlassen hat und archiviert wurde.</summary>
    private static async Task AusgetretenAsync(SkoulouDbContext db, UserManager<Konto> users, Guid schuleId, Schuljahr jahr, Klasse klasse, List<DemoKonto> neu, CancellationToken ct)
    {
        var konto = await KontoAsync(users, neu, "Afiwa", "Kpodo", Geschlecht.Weiblich, schuleId, KontoStatus.Archive, new DateOnly(2014, 2, 9));
        if (konto is null) return;
        var nr = await db.Nummernkreise.FirstAsync(n => n.SchuleId == schuleId && n.Art == NummernArt.Matrikel && n.Jahr == 2026, ct);
        nr.Letzte++;
        db.SchuelerProfile.Add(new SchuelerProfil
        {
            KontoId = konto.Id, SchuleId = schuleId, Matrikel = NummernFormat.Formatiere(NummernArt.Matrikel, 2026, nr.Letzte, "CDP"),
            Geburtsort = "Lomé", Nationalitaet = "Togolaise", AufnahmeBestaetigtAmUtc = Skoulou.Core.Werkzeuge.AppUhr.UtcNow
        });
        db.Einschreibungen.Add(new Einschreibung
        {
            SchuleId = schuleId, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = klasse.Id, Von = jahr.Beginn, Bis = new DateOnly(2026, 10, 16),
            Status = EinschreibungsStatus.Partie, Austrittsgrund = "Déménagement de la famille à Kara."
        });
    }

    private static async Task<Konto?> KontoAsync(UserManager<Konto> users, List<DemoKonto> neu, string vorname, string nachname, Geschlecht g,
        Guid schuleId, KontoStatus status, DateOnly geburt)
    {
        var identifiant = IdentifiantRegel.Grundform(vorname, nachname);
        if (await users.FindByNameAsync(identifiant) is not null) return null;
        var passwort = StartPasswort.Erzeuge();
        var konto = new Konto
        {
            UserName = identifiant, Vorname = vorname, Nachname = nachname, Geschlecht = g, SchuleId = schuleId, Geburtsdatum = geburt,
            Status = status, SelbstRegistriert = status == KontoStatus.EnAttente, MussPasswortAendern = false
        };
        var ergebnis = await KontoAnlage.AnlegenAsync(users, konto, Rollen.Schueler, passwort);
        if (!ergebnis.Succeeded) throw new InvalidOperationException($"Compte de démonstration non créé : {KontoAnlage.Fehlertext(ergebnis)}");
        neu.Add(new DemoKonto(Rollen.Schueler, konto.VollerName, identifiant, passwort));
        return konto;
    }
}
