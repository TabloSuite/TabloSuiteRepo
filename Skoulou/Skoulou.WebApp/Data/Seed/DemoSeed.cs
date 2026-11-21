using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>Neu angelegtes Demo-Konto (Passwort nur für die lokale Ausgabedatei).</summary>
public sealed record DemoKonto(string Rolle, string Name, string Identifiant, string Passwort);

/// <summary>
/// Fiktive Demo-Schule « Collège Démo Les Palmiers » (Lomé) für Entwicklung und Rundgänge – idempotent:
/// existiert die Schule, passiert nichts; vorhandene Konten werden wiederverwendet.
/// Alle Namen sind erfunden, E-Mails nutzen die Fantasie-Domain « exemple.tg ».
/// </summary>
public sealed class DemoSeed(UserManager<Konto> users, IDbContextFactory<SkoulouDbContext> dbFactory, ILogger<DemoSeed> logger, IServiceProvider dienste)
{
    public const string SchulName = "Collège Démo Les Palmiers";
    public const string Domain = "exemple.tg";

    /// <summary>Anmeldung der Demo-Schulen bei SKOULOU: vor dem Demo-Vorjahr 2025-2026 (Abrechnung ab Anmeldung, 21.11.2026).</summary>
    public static readonly DateTime Angemeldet = new(2025, 8, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly (string Vorname, string Nachname, Geschlecht G, string Klasse, int Jahr)[] Schueler =
    [
        ("Kossi", "Adjo", Geschlecht.Maennlich, "6e A", 2014), ("Ama", "Dossou", Geschlecht.Weiblich, "6e A", 2014),
        ("Komlan", "Ekué", Geschlecht.Maennlich, "6e A", 2014), ("Essi", "Amegah", Geschlecht.Weiblich, "6e A", 2015),
        ("Edem", "Kodjo", Geschlecht.Maennlich, "6e A", 2014), ("Yawa", "Tchalla", Geschlecht.Weiblich, "6e B", 2014),
        ("Sena", "Agbodjan", Geschlecht.Maennlich, "6e B", 2014), ("Kafui", "Atsu", Geschlecht.Weiblich, "6e B", 2015),
        ("Elom", "Gbeassor", Geschlecht.Maennlich, "6e B", 2014), ("Abla", "Sossou", Geschlecht.Weiblich, "5e A", 2013),
        ("Mawuli", "Ahadji", Geschlecht.Maennlich, "5e A", 2013), ("Dela", "Kponton", Geschlecht.Weiblich, "5e A", 2013),
    ];

    private static readonly (string Name, string Kuerzel, FachGruppe Gruppe, decimal Koeff, decimal Stunden, bool VonYao)[] Faecher =
    [
        ("Français", "FR", FachGruppe.Lettres, 3, 5, false), ("Mathématiques", "MATH", FachGruppe.Sciences, 3, 5, true),
        ("Anglais", "ANG", FachGruppe.Lettres, 2, 3, false), ("SVT", "SVT", FachGruppe.Sciences, 2, 2, true),
        ("Histoire-Géographie", "HG", FachGruppe.Lettres, 2, 3, false), ("EPS", "EPS", FachGruppe.Autres, 1, 2, true),
    ];

    public async Task<IReadOnlyList<DemoKonto>> AusfuehrenAsync(CancellationToken ct = default)
    {
        var neu = new List<DemoKonto>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schluessel = TextNormalisierung.Schluessel(SchulName);
        if (await db.Schulen.AnyAsync(s => s.NameNormalisiert == schluessel, ct))
        {
            logger.LogInformation("Demo-Schule existiert bereits – nichts zu tun.");
            return neu;
        }

        var responsable = await KontoAsync(neu, Rollen.Responsable, "Sélom", "Amouzou", Geschlecht.Maennlich, null, mitMail: true);
        var schule = new Schule
        {
            Name = SchulName, NameNormalisiert = schluessel, Ort = "Lomé", Land = "Togo", Status = SchulStatus.Active,
            ResponsableId = responsable.Id, MatrikelPraefix = "CDP", OffizielleBezeichnung = SchulName,
            Slogan = "Apprendre, grandir, réussir", Adresse = "Quartier Démo, Lomé", Telefon = "+228 00 00 00 00",
            Email = "contact@" + Domain, RepublikZeile = "RÉPUBLIQUE TOGOLAISE", StaatsMotto = "Travail – Liberté – Patrie",
            Ministerium = "Ministère des Enseignements Primaire et Secondaire", DirectionRegionale = "Direction régionale de l'éducation Golfe-Lomé",
            Statut = "Privé laïc",
            EssaiPerioden = 0,   // Demo-Schule hat schon Rechnungen (2025-2026) – keine Probezeit
            // Seit August 2025 bei SKOULOU: sonst wären alle Perioden vor dem Tag des Seedens « vor der Anmeldung » (21.11.2026).
            ErstelltAmUtc = Angemeldet
        };
        db.Schulen.Add(schule);
        await db.SaveChangesAsync(ct);

        var direktor = await KontoAsync(neu, Rollen.Direktor, "Koffi", "Agbéko", Geschlecht.Maennlich, schule.Id, mitMail: true);
        var koordinator = await KontoAsync(neu, Rollen.Koordinator, "Akossiwa", "Mensah", Geschlecht.Weiblich, schule.Id, mitMail: true,
            rechte: KoordinatorRechte.FinanzenEinsehen | KoordinatorRechte.ZahlungenErfassen);
        var yao = await KontoAsync(neu, Rollen.Lehrer, "Yao", "Kpodar", Geschlecht.Maennlich, schule.Id, mitMail: true);
        var afi = await KontoAsync(neu, Rollen.Lehrer, "Afi", "Lawson", Geschlecht.Weiblich, schule.Id, mitMail: true);

        var jahr = new Schuljahr
        {
            SchuleId = schule.Id, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 14), Ende = new(2027, 6, 30),
            Status = SchuljahrStatus.Laufend, PeriodenArt = PeriodenArt.Trimestre, PlattformMonatspreis = 500
        };
        db.Schuljahre.Add(jahr);
        db.Perioden.AddRange(
            Periode(schule.Id, jahr.Id, 1, "1er trimestre", new(2026, 9, 14), new(2026, 12, 18)),
            Periode(schule.Id, jahr.Id, 2, "2e trimestre", new(2027, 1, 4), new(2027, 3, 26)),
            Periode(schule.Id, jahr.Id, 3, "3e trimestre", new(2027, 4, 6), new(2027, 6, 30)));

        var fuenfte = new Stufe { SchuleId = schule.Id, Name = "5e", Reihenfolge = 2, Zyklus = Zyklus.College };
        db.Stufen.Add(fuenfte);
        var sechste = new Stufe { SchuleId = schule.Id, Name = "6e", Reihenfolge = 1, Zyklus = Zyklus.College, NachfolgerId = fuenfte.Id };
        db.Stufen.Add(sechste);

        var klassen = new Dictionary<string, Klasse>
        {
            ["6e A"] = Klasse(schule.Id, jahr.Id, sechste.Id, "6e A", afi.Id, "Salle 1"),
            ["6e B"] = Klasse(schule.Id, jahr.Id, sechste.Id, "6e B", yao.Id, "Salle 2"),
            ["5e A"] = Klasse(schule.Id, jahr.Id, fuenfte.Id, "5e A", afi.Id, "Salle 3"),
        };
        db.Klassen.AddRange(klassen.Values);

        var reihenfolge = 0;
        foreach (var f in Faecher)
        {
            var fach = new Fach { SchuleId = schule.Id, Name = f.Name, Kuerzel = f.Kuerzel, Gruppe = f.Gruppe, Reihenfolge = ++reihenfolge };
            db.Faecher.Add(fach);
            foreach (var klasse in klassen.Values)
            {
                var kf = new Klassenfach { SchuleId = schule.Id, KlasseId = klasse.Id, FachId = fach.Id, Koeffizient = f.Koeff, Reihenfolge = reihenfolge };
                db.Klassenfaecher.Add(kf);
                db.Lehrauftraege.Add(new Lehrauftrag { SchuleId = schule.Id, KlassenfachId = kf.Id, LehrerId = f.VonYao ? yao.Id : afi.Id, Wochenstunden = f.Stunden });
            }
        }
        await db.SaveChangesAsync(ct);

        var nr = 0;
        var kinder = new Dictionary<string, Konto>();
        foreach (var s in Schueler)
        {
            var konto = await KontoAsync(neu, Rollen.Schueler, s.Vorname, s.Nachname, s.G, schule.Id, mitMail: false,
                geburtsdatum: new DateOnly(s.Jahr, 1 + nr % 12, 3 + nr));
            kinder[s.Nachname] = konto;
            db.SchuelerProfile.Add(new SchuelerProfil
            {
                KontoId = konto.Id, SchuleId = schule.Id, Matrikel = NummernFormat.Formatiere(NummernArt.Matrikel, 2026, ++nr, "CDP"),
                Geburtsort = "Lomé", Nationalitaet = "Togolaise", ElternTelefon = "+228 00 00 00 " + nr.ToString("00"),
                AufnahmeBestaetigtAmUtc = DateTime.UtcNow
            });
            db.Einschreibungen.Add(new Einschreibung
            {
                SchuleId = schule.Id, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = klassen[s.Klasse].Id,
                Von = jahr.Beginn, Status = EinschreibungsStatus.Active
            });
        }
        db.Nummernkreise.Add(new Nummernkreis { SchuleId = schule.Id, Art = NummernArt.Matrikel, Jahr = 2026, Letzte = nr });

        var vater = await KontoAsync(neu, Rollen.Eltern, "Kodzo", "Adjo", Geschlecht.Maennlich, schule.Id, mitMail: true);
        var mutter = await KontoAsync(neu, Rollen.Eltern, "Mawuéna", "Dossou", Geschlecht.Weiblich, schule.Id, mitMail: true);
        db.ElternKinder.AddRange(
            new ElternKind { SchuleId = schule.Id, ElternId = vater.Id, SchuelerId = kinder["Adjo"].Id, Beziehung = "Père" },
            new ElternKind { SchuleId = schule.Id, ElternId = mutter.Id, SchuelerId = kinder["Dossou"].Id, Beziehung = "Mère" });
        await db.SaveChangesAsync(ct);

        // Demo-Daten der Bereiche (je Bereich eine eigene Datei, siehe Bauplan §12).
        foreach (var bereich in new Func<IServiceProvider, Guid, List<DemoKonto>, CancellationToken, Task>[]
                 { DemoSeedPlattform.ErgaenzenAsync, DemoSeedOrganisation.ErgaenzenAsync, DemoSeedPaedagogik.ErgaenzenAsync,
                   DemoSeedNoten.ErgaenzenAsync, DemoSeedFinanzen.ErgaenzenAsync })
            await bereich(dienste, schule.Id, neu, ct);

        logger.LogInformation("Demo-Schule angelegt: {Konten} Konten, {Schueler} Schüler.", neu.Count, Schueler.Length);
        return neu;
    }

    private async Task<Konto> KontoAsync(List<DemoKonto> neu, string rolle, string vorname, string nachname, Geschlecht g,
        Guid? schuleId, bool mitMail, KoordinatorRechte rechte = KoordinatorRechte.Keine, DateOnly? geburtsdatum = null)
    {
        var identifiant = IdentifiantRegel.Grundform(vorname, nachname);
        var vorhanden = await users.FindByNameAsync(identifiant);
        if (vorhanden is not null) return vorhanden;

        var passwort = StartPasswort.Erzeuge();
        var konto = new Konto
        {
            UserName = identifiant, Vorname = vorname, Nachname = nachname, Geschlecht = g, SchuleId = schuleId,
            Email = mitMail ? $"{identifiant}@{Domain}" : null, KoordinatorRechte = rechte, Geburtsdatum = geburtsdatum,
            Status = KontoStatus.Actif, MussPasswortAendern = false
        };
        var ergebnis = await KontoAnlage.AnlegenAsync(users, konto, rolle, passwort);
        if (!ergebnis.Succeeded) throw new InvalidOperationException($"Compte de démonstration non créé : {KontoAnlage.Fehlertext(ergebnis)}");
        neu.Add(new DemoKonto(rolle, konto.VollerName, identifiant, passwort));
        return konto;
    }

    private static Periode Periode(Guid schuleId, Guid jahrId, int nr, string titel, DateOnly von, DateOnly bis)
        => new() { SchuleId = schuleId, SchuljahrId = jahrId, Nr = nr, Titel = titel, Beginn = von, Ende = bis };

    private static Klasse Klasse(Guid schuleId, Guid jahrId, Guid stufeId, string name, Guid lehrerId, string raum)
        => new() { SchuleId = schuleId, SchuljahrId = jahrId, StufeId = stufeId, Name = name, KlassenlehrerId = lehrerId, Raum = raum, Kapazitaet = 45 };
}
