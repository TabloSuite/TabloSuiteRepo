using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Simulation;

internal sealed partial class Simulation
{
    private static readonly string[] Klassenbuchstaben = ["A", "B", "C", "D"];

    private static (string Name, Zyklus Zyklus)[] StufenVon(SchulTyp typ) => typ switch
    {
        SchulTyp.Primaire => [("CP1", Zyklus.Primaire), ("CP2", Zyklus.Primaire), ("CE1", Zyklus.Primaire), ("CE2", Zyklus.Primaire), ("CM1", Zyklus.Primaire), ("CM2", Zyklus.Primaire)],
        SchulTyp.College => [("6e", Zyklus.College), ("5e", Zyklus.College), ("4e", Zyklus.College), ("3e", Zyklus.College)],
        SchulTyp.Lycee => [("2nde", Zyklus.Lycee), ("1re", Zyklus.Lycee), ("Tle", Zyklus.Lycee)],
        _ => [("A1", Zyklus.Formation), ("A2", Zyklus.Formation), ("B1", Zyklus.Formation), ("B2", Zyklus.Formation)]
    };

    private static (string Name, string Kuerzel, FachGruppe Gruppe, decimal Koeff)[] FaecherVon(SchulTyp typ) => typ switch
    {
        SchulTyp.Primaire => [("Français", "FR", FachGruppe.Lettres, 2), ("Mathématiques", "MATH", FachGruppe.Sciences, 2),
            ("Sciences d'observation", "SO", FachGruppe.Sciences, 1), ("Histoire-Géographie", "HG", FachGruppe.Lettres, 1),
            ("Éducation civique et morale", "ECM", FachGruppe.Autres, 1)],
        SchulTyp.College => [("Français", "FR", FachGruppe.Lettres, 3), ("Anglais", "ANG", FachGruppe.Lettres, 2),
            ("Mathématiques", "MATH", FachGruppe.Sciences, 3), ("Sciences de la vie et de la Terre", "SVT", FachGruppe.Sciences, 2),
            ("Physique-Chimie", "PC", FachGruppe.Sciences, 2), ("Histoire-Géographie", "HG", FachGruppe.Lettres, 2),
            ("Éducation physique et sportive", "EPS", FachGruppe.Autres, 1)],
        SchulTyp.Lycee => [("Français", "FR", FachGruppe.Lettres, 3), ("Anglais", "ANG", FachGruppe.Lettres, 2),
            ("Philosophie", "PHILO", FachGruppe.Lettres, 2), ("Mathématiques", "MATH", FachGruppe.Sciences, 4),
            ("Physique-Chimie", "PC", FachGruppe.Sciences, 3), ("Sciences de la vie et de la Terre", "SVT", FachGruppe.Sciences, 2),
            ("Histoire-Géographie", "HG", FachGruppe.Lettres, 2), ("Éducation physique et sportive", "EPS", FachGruppe.Autres, 1)],
        _ => [("Expression orale", "ORAL", FachGruppe.Lettres, 2), ("Expression écrite", "ECRIT", FachGruppe.Lettres, 2),
            ("Compréhension", "COMP", FachGruppe.Lettres, 1), ("Grammaire", "GRAM", FachGruppe.Lettres, 1)]
    };

    private int Groesse(SimSchule s, Zufall z) => Math.Max(5, (int)Math.Round(z.Zahl(s.P.MinGroesse, s.P.MaxGroesse + 1) * faktor));

    /// <summary>Erstes Jahr einer Schule: Stammdaten, Personal, Fächer, Stufen, Jahr 2023-2024, Klassen, Lehraufträge, Gebühren, Schüler.</summary>
    private async Task EinrichtenAsync(SimSchule s, Zufall z)
    {
        var dir = s.Direktor;
        await api.Schule.DokumenteSpeichernAsync(dir, new DokumentEingabe(s.P.Name, "Travail – Discipline – Réussite", $"BP {z.Zahl(100, 9999)} {s.P.Ort}",
            SimNamen.Telefon(z), null, $"N° {z.Zahl(100, 999)}/MEPSTA", "Ministère des Enseignements primaire, secondaire, technique et de l'artisanat",
            "RÉPUBLIQUE TOGOLAISE", "Travail – Liberté – Patrie", $"Direction régionale de l'éducation – {s.P.Ort}", z.Ja(0.5) ? "Privé laïc" : "Public"));

        // Personal: Koordination mit/ohne Finanzrechte, Lehrkräfte.
        s.KoordMit = await PersonAsync(s, z, Rollen.Koordinator, true);
        s.KoordOhne = await PersonAsync(s, z, Rollen.Koordinator, false);
        await api.Schule.KoordinatorRechteSetzenAsync(dir, s.KoordMit, KoordinatorRechte.Alle);
        await AbgelehntAsync("Koordinator setzt Rechte", () => api.Schule.KoordinatorRechteSetzenAsync(s.KoordMit, s.KoordOhne, KoordinatorRechte.Alle));

        var faecher = FaecherVon(s.P.Typ);
        foreach (var f in faecher)
            s.Faecher.Add(new SimFach(await api.Faecher.FachSpeichernAsync(dir, null, new FachEingabe(f.Name, f.Kuerzel, f.Gruppe)), f.Name, f.Koeff));

        var stufen = StufenVon(s.P.Typ);
        for (var i = 0; i < stufen.Length; i++)
            s.Stufen.Add(new SimStufe(await api.Klassen.StufeSpeichernAsync(s.KoordMit, null, new StufeEingabe(stufen[i].Name, stufen[i].Zyklus, null)), stufen[i].Name, i + 1));
        for (var i = 0; i < stufen.Length - 1; i++)
            await api.Klassen.StufeSpeichernAsync(dir, s.Stufen[i].Id, new StufeEingabe(stufen[i].Name, stufen[i].Zyklus, s.Stufen[i + 1].Id));

        var jahr = await JahrAnlegenAsync(s, 2023);

        // Klassen (Sprachschule: je Stufe und Session eine Klasse, nur für diese Session).
        var klassen = new List<(string Name, SimStufe Stufe, int Session)>();
        foreach (var st in s.Stufen)
        {
            if (s.P.Typ == SchulTyp.Langues)
                foreach (var p in jahr.Perioden) klassen.Add(($"Anglais {st.Name} – S{p.Nr}", st, p.Nr));
            else
                for (var k = 0; k < s.P.KlassenJeStufe; k++) klassen.Add(($"{st.Name} {Klassenbuchstaben[k]}", st, 0));
        }

        // Lehrkräfte: Primaire eine je Klasse; Collège/Lycée je Fach eine je vier Klassen; Sprachschule drei.
        var anzahlLehrer = s.P.Typ switch
        {
            SchulTyp.Primaire => klassen.Count,
            SchulTyp.Langues => 3,
            _ => faecher.Length * (int)Math.Ceiling(klassen.Count / 4.0)
        };
        for (var i = 0; i < anzahlLehrer; i++) s.Lehrer.Add(await PersonAsync(s, z, Rollen.Lehrer, z.Ja(0.4)));

        Guid LehrerFuer(int klasse, int fach) => s.P.Typ switch
        {
            SchulTyp.Primaire => s.Lehrer[klasse],
            SchulTyp.Langues => s.Lehrer[klasse % 3],
            _ => s.Lehrer[fach * (int)Math.Ceiling(klassen.Count / 4.0) + klasse / 4]
        };

        for (var i = 0; i < klassen.Count; i++)
        {
            var (name, stufe, session) = klassen[i];
            var periode = session == 0 ? (Guid?)null : jahr.Perioden[session - 1].Id;
            var id = await api.Klassen.KlasseSpeichernAsync(s.KoordMit, jahr.Id, null,
                new KlasseEingabe(name, stufe.Id, periode, 60, LehrerFuer(i, 0), $"Salle {i + 1}"));
            jahr.Klassen.Add(new SimKlasse(id, name, stufe, session));
        }

        // Fächer und Koeffizienten: erste Klasse je Stufe einstellen, dann auf die Stufe übertragen.
        foreach (var gruppe in jahr.Klassen.GroupBy(k => k.Stufe))
        {
            var erste = gruppe.First();
            foreach (var k in gruppe) await api.Unterricht.FaecherHinzufuegenAsync(dir, k.Id, s.Faecher.Select(f => f.Id).ToList());
            foreach (var kf in await api.Unterricht.KlassenfaecherAsync(dir, erste.Id))
            {
                var koeff = s.Faecher.First(f => f.Id == kf.FachId).Koeffizient;
                if (koeff != 1) await api.Unterricht.KoeffizientSetzenAsync(dir, kf.Id, koeff);
            }
            if (gruppe.Count() > 1) await api.Unterricht.KoeffizientenAufStufeAsync(s.KoordMit, erste.Id);
        }
        for (var i = 0; i < jahr.Klassen.Count; i++)
        {
            var k = jahr.Klassen[i];
            foreach (var kf in await api.Unterricht.KlassenfaecherAsync(dir, k.Id))
            {
                var fachNr = s.Faecher.FindIndex(f => f.Id == kf.FachId);
                var fach = s.Faecher[fachNr];
                Guid? lehrer = LehrerFuer(i, fachNr);
                // Die Direktion unterrichtet Mathématiques in der letzten Klasse; EPS der ersten Klasse bleibt ohne Lehrkraft.
                if (s.P.Typ is SchulTyp.College or SchulTyp.Lycee && i == jahr.Klassen.Count - 1 && fach.Name == "Mathématiques") lehrer = dir;
                if (s.P.Typ is SchulTyp.College or SchulTyp.Lycee && i == 0 && fach.Name.StartsWith("Éducation physique", StringComparison.Ordinal)) lehrer = null;
                if (lehrer is { } l) await api.Unterricht.LehrauftragSetzenAsync(s.KoordMit, kf.Id, l, fach.Koeffizient + 1);
                k.Faecher.Add(new SimKlassenfach(kf.Id, kf.FachId, lehrer ?? dir));
            }
        }

        // Bewertungsvorlage: « Composition » je Periode; in je einer Schule pro Typ zusätzlich « Devoir ».
        var p1 = jahr.Perioden[0].Id;
        await api.Bewertungen.KomponenteAnlegenAsync(dir, p1, new KomponenteEingabe("Composition", BewertungsArt.Composition, 2, 20));
        if (s.Nr % 7 == 1) await api.Bewertungen.KomponenteAnlegenAsync(dir, p1, new KomponenteEingabe("Devoir surveillé", BewertungsArt.Devoir, 1, 20));
        await api.Bewertungen.KomponentenVerteilenAsync(s.KoordMit, jahr.Id, p1);

        await api.Jahre.JahrStartenAsync(dir, jahr.Id);
        await PostenAnlegenAsync(s, jahr);
        jahr.Preis = await PreisAsync(jahr.Id);

        // Schüler: Sprachschule nur für die Klassen der ersten Session.
        foreach (var k in jahr.Klassen.Where(k => k.Session <= 1))
        {
            var anzahl = Groesse(s, z);
            for (var i = 0; i < anzahl; i++) await SchuelerAnlegenAsync(s, z, k, z.Ja(0.5) ? dir : s.KoordMit);
        }
        await SelbstregistrierungenAsync(s, z);
        Zaehle("Klassen", jahr.Klassen.Count);
    }

    private Task<long> PreisAsync(Guid jahrId)
        => d.MitDbAsync(db => db.Schuljahre.Where(j => j.Id == jahrId).Select(j => j.PlattformJahrespreis ?? 0).SingleAsync());

    private async Task<SimJahr> JahrAnlegenAsync(SimSchule s, int startJahr)
    {
        var art = s.P.Perioden;
        var anzahl = art switch { PeriodenArt.Trimestre => 3, PeriodenArt.Semestre => 2, _ => 4 };
        var id = await api.Jahre.JahrAnlegenAsync(s.Direktor, new SchuljahrEingabe($"{startJahr}-{startJahr + 1}",
            new DateOnly(startJahr, 9, 1), new DateOnly(startJahr + 1, 6, 30), art, anzahl));
        var jahr = await JahrLadenAsync(s, id);
        s.Jahre.Add(jahr);
        return jahr;
    }

    private async Task<SimJahr> JahrLadenAsync(SimSchule s, Guid id)
    {
        var zeile = (await api.Jahre.JahreAsync(s.Direktor)).Single(j => j.Id == id);
        var jahr = new SimJahr(id, zeile.Bezeichnung, zeile.Beginn, zeile.Ende);
        foreach (var p in zeile.Perioden.Where(p => p.ZaehltFuerJahr).OrderBy(p => p.Nr))
            jahr.Perioden.Add(new SimPeriode(p.Id, p.Nr, p.Titel, p.Beginn, p.Ende));
        return jahr;
    }

    private static IReadOnlyList<RateEingabe> Raten(long betrag, params DateOnly[] faellig)
    {
        var raten = new List<RateEingabe>();
        var rest = betrag;
        for (var i = 0; i < faellig.Length; i++)
        {
            var teil = i == faellig.Length - 1 ? rest : (betrag / faellig.Length / 1_000) * 1_000;
            raten.Add(new RateEingabe(teil, faellig[i]));
            rest -= teil;
        }
        return raten;
    }

    /// <summary>Gebührenposten des ersten Jahres (Inscription, APE, Scolarité je Stufe bzw. Formation der Sprachschule).</summary>
    private async Task PostenAnlegenAsync(SimSchule s, SimJahr jahr)
    {
        var y = jahr.Beginn.Year;
        var (inscription, ape, basis, schritt) = s.P.Typ switch
        {
            SchulTyp.Primaire => (5_000L, 2_000L, 24_000L, 3_000L),
            SchulTyp.College => (10_000L, 3_000L, 60_000L, 5_000L),
            SchulTyp.Lycee => (15_000L, 5_000L, 90_000L, 10_000L),
            _ => (10_000L, 0L, 100_000L, 0L)
        };
        await PostenAsync(s, jahr, new PostenEingabe("Inscription", inscription, null, null, true, Raten(inscription, jahr.Beginn)));
        if (ape > 0) await PostenAsync(s, jahr, new PostenEingabe("APE", ape, null, null, true, Raten(ape, jahr.Beginn.AddDays(30))));
        if (s.P.Typ == SchulTyp.Langues)
        {
            await PostenAsync(s, jahr, new PostenEingabe("Formation (4 sessions)", basis, null, null, true,
                Raten(basis, jahr.Perioden.Select(p => p.Beginn).ToArray())));
            return;
        }
        foreach (var st in s.Stufen)
        {
            var betrag = basis + schritt * (st.Nr - 1);
            await PostenAsync(s, jahr, new PostenEingabe($"Scolarité {st.Name}", betrag, st.Id, null, true,
                Raten(betrag, new DateOnly(y, 10, 1), new DateOnly(y + 1, 1, 10), new DateOnly(y + 1, 4, 10))));
        }
    }

    private async Task PostenAsync(SimSchule s, SimJahr jahr, PostenEingabe e)
    {
        var akteur = s.Nr % 2 == 0 ? s.KoordMit : s.Direktor;
        var id = await api.Gebuehren.PostenSpeichernAsync(akteur, jahr.Id, null, e);
        jahr.Posten.Add(new SimPosten(id, e.Bezeichnung!, e.Betrag!.Value, e.StufeId, e.Raten.Select(r => (r.Betrag!.Value, r.Faellig!.Value)).ToList()));
    }

    /// <summary>Gebührenposten eines neuen Jahres aus dem Dienst lesen (nach « Copier l'année précédente »).</summary>
    private async Task PostenLadenAsync(SimSchule s, SimJahr jahr)
    {
        jahr.Posten.Clear();
        foreach (var p in await api.Gebuehren.PostenAsync(s.Direktor, jahr.Id))
            jahr.Posten.Add(new SimPosten(p.Id, p.Bezeichnung, p.Betrag, p.StufeId, p.Raten.Select(r => (r.Betrag, r.Faellig)).ToList()));
    }

    private async Task<Guid> PersonAsync(SimSchule s, Zufall z, string rolle, bool mitMail)
    {
        var (vor, g) = SimNamen.Vorname(z);
        var nach = SimNamen.Nachname(z);
        var eingabe = new PersonalEingabe(rolle, vor, nach, null, mitMail ? SimNamen.Mail(vor, nach, z.Zahl(1, 99)) : null, SimNamen.Telefon(z), g);
        var stapel = await SollAsync("Personal anlegen", () => api.Personal.AnlegenAsync(s.Direktor, eingabe))
                     ?? throw new InvalidOperationException($"Personal in {s.P.Name} nicht angelegt.");
        Zaehle(rolle == Rollen.Lehrer ? "Lehrkräfte" : "Koordinatoren");
        var id = stapel.Zugaenge.Single().KontoId;
        await ErsteAnmeldungAsync(id);
        return id;
    }

    /// <summary>Neuer Schüler über « Nouvel élève » (mit Klasse), Eltern (neu oder Geschwister), Ermäßigungen.</summary>
    private async Task<SimSchueler?> SchuelerAnlegenAsync(SimSchule s, Zufall z, SimKlasse? klasse, Guid akteur, bool wiederholer = false)
    {
        var (vor, g) = SimNamen.Vorname(z);
        SimSchueler? geschwister = null;
        if (z.Ja(0.12))
        {
            List<SimSchueler> kandidaten;
            lock (s.Schueler) kandidaten = s.Aktive.Where(x => x.Eltern is { } e && s.KinderJeEltern.GetValueOrDefault(e) < 3).ToList();
            if (kandidaten.Count > 0) geschwister = z.Eins(kandidaten);
        }
        var nach = geschwister?.Nachname ?? SimNamen.Nachname(z);
        var alter = s.P.Typ switch { SchulTyp.Primaire => 6, SchulTyp.College => 11, SchulTyp.Lycee => 15, _ => 18 } + (klasse?.Stufe.Nr ?? 1);
        var gross = s.P.Typ is SchulTyp.Lycee or SchulTyp.Langues;
        var eingabe = new SchuelerEingabe(vor, nach, g, new DateOnly(d.HeuteDatum.Year - alter, z.Zahl(1, 13), z.Zahl(1, 29)), z.Eins(SimNamen.Geburtsorte),
            "Togolaise", z.Ja(0.1), z.Ja(0.3), SimNamen.Telefon(z), z.Ja(0.2) ? SimNamen.Mail("parent." + vor, nach, z.Zahl(1, 999)) : null,
            gross && z.Ja(0.3) ? SimNamen.Mail(vor, nach, z.Zahl(100, 999)) : null, gross ? SimNamen.Telefon(z) : null, null, null);
        var stapel = await SollAsync($"Schüler anlegen ({s.P.Name})", () => api.Schueler.AnlegenAsync(akteur, eingabe, klasse?.Id, wiederholer));
        if (stapel is null) return null;
        var schueler = new SimSchueler(stapel.Zugaenge.Single().KontoId, vor, nach, Math.Clamp(z.Normal(11.5, 3.2), 3, 19),
            z.Anteil() switch { < 0.6 => 0, < 0.85 => 1, < 0.95 => 2, _ => 3 })
        { Klasse = klasse, Aktiv = klasse is not null };
        lock (s.Schueler) s.Schueler[schueler.Id] = schueler;
        Zaehle("Schüler");
        if (klasse is not null) KontoFuer(s, schueler, klasse);

        if (geschwister is not null)
        {
            var ok = await SollAsync("Eltern verknüpfen", async () => { await api.Eltern.VerknuepfenAsync(s.Direktor, geschwister.Eltern!.Value, schueler.Id, "Père"); return true; });
            if (!ok) return schueler;
            schueler.Eltern = geschwister.Eltern;
            lock (s.Schueler) s.KinderJeEltern[schueler.Eltern!.Value] = s.KinderJeEltern.GetValueOrDefault(schueler.Eltern.Value) + 1;
            if (klasse is not null && s.P.Typ != SchulTyp.Langues) await ErmaessigungAsync(s, schueler, 10, "Fratrie");
        }
        else if (z.Ja(s.P.Typ == SchulTyp.Langues ? 0.1 : 0.55))
        {
            var (evor, eg) = SimNamen.Vorname(z);
            var e = await SollAsync("Eltern anlegen", () => api.Eltern.AnlegenAsync(akteur, new ElternEingabe(evor, nach, null,
                z.Ja(0.3) ? SimNamen.Mail(evor, nach, z.Zahl(1000, 9999)) : null, SimNamen.Telefon(z), eg), schueler.Id,
                eg == Geschlecht.Weiblich ? "Mère" : z.Ja(0.85) ? "Père" : "Tuteur"));
            if (e is not null)
            {
                schueler.Eltern = e.Zugaenge.Single().KontoId;
                lock (s.Schueler)
                {
                    s.Elternkonten.Add(schueler.Eltern.Value);
                    s.KinderJeEltern[schueler.Eltern.Value] = 1;
                }
                Zaehle("Eltern");
            }
        }
        if (klasse is not null && s.P.Typ != SchulTyp.Langues && z.Ja(0.04)) await ErmaessigungAsync(s, schueler, 50, "Bourse d'excellence");
        return schueler;
    }

    private static void KontoFuer(SimSchule s, SimSchueler schueler, SimKlasse klasse)
    {
        if (!schueler.Konten.TryGetValue(s.Jahr.Id, out var k)) schueler.Konten[s.Jahr.Id] = k = new JahresKonto();
        k.StufeId = s.P.Typ == SchulTyp.Langues ? null : klasse.Stufe.Id;
    }

    /// <summary>Prozentuale Ermäßigung auf die Scolarité der Stufe (ganze FCFA, Beträge sind Vielfache von 1.000).</summary>
    private async Task ErmaessigungAsync(SimSchule s, SimSchueler schueler, int prozent, string grund)
    {
        var stufe = schueler.Klasse!.Stufe.Id;
        var posten = s.Jahr.Posten.FirstOrDefault(p => p.StufeId == stufe && p.Name.StartsWith("Scolarité", StringComparison.Ordinal));
        if (posten is null) return;
        var ok = await SollAsync("Ermäßigung", () => api.Gebuehren.ErmaessigungAnlegenAsync(s.Direktor, s.Jahr.Id,
            new ErmaessigungEingabe(schueler.Id, posten.Id, ErmaessigungsArt.Pourcentage, prozent, grund)));
        if (ok == Guid.Empty) return;
        schueler.Konten[s.Jahr.Id].Ermaessigung += (long)Math.Round(posten.Betrag * prozent / 100m, MidpointRounding.AwayFromZero);
        Zaehle("Ermäßigungen");
    }

    /// <summary>Selbstregistrierungen von Schülern: die Direktion validiert (mit Klasse) oder lehnt ab.</summary>
    private async Task SelbstregistrierungenAsync(SimSchule s, Zufall z)
    {
        if (s.P.Typ is not (SchulTyp.Lycee or SchulTyp.College)) return;
        for (var i = 0; i < 3; i++)
        {
            var (vor, _) = SimNamen.Vorname(z);
            var nach = SimNamen.Nachname(z);
            var pw = "eleve" + z.Zahl(1000, 9999) + "x";
            var e = await SollAsync("Selbstregistrierung", () => api.Registrierung.RegistrierenAsync(new RegistrierungsEingabe(RegistrierungsArt.Eleve,
                vor, nach, null, null, null, pw, pw, true, SchuleId: s.Id,
                Geburtsdatum: new DateOnly(2009, z.Zahl(1, 13), z.Zahl(1, 29)), ElternTelefon: SimNamen.Telefon(z)), $"10.{s.Nr}.{i}.{d.HeuteDatum.DayNumber % 250}"));
            if (e is null) continue;
            var id = await d.MitDbAsync(db => db.Users.Where(k => k.UserName == e.Identifiant).Select(k => k.Id).SingleAsync());
            if (i == 2)
            {
                await SollAsync("Selbstregistrierung ablehnen", () => api.Schueler.AblehnenAsync(s.Direktor, id, "Élève inconnu de l'établissement."));
                Zaehle("Selbstregistrierungen abgelehnt");
                continue;
            }
            var klasse = s.Jahr.Klassen[z.Zahl(0, s.Jahr.Klassen.Count)];
            await SollAsync("Selbstregistrierung validieren", () => api.Schueler.ValidierenAsync(s.KoordMit, id, klasse.Id, false));
            var schueler = new SimSchueler(id, vor, nach, Math.Clamp(z.Normal(11, 3), 3, 19), 1) { Klasse = klasse, Aktiv = true };
            lock (s.Schueler) s.Schueler[id] = schueler;
            KontoFuer(s, schueler, klasse);
            Zaehle("Selbstregistrierungen validiert");
        }
    }
}
