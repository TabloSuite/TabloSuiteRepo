using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Noten;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Simulation;

internal sealed partial class Simulation
{
    private static string NotenText(decimal wert) => wert.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Note eines Schülers: Niveau + Streuung je Fach, auf Viertelpunkte; selten abs/aj/disp oder (ganz selten) fehlend.</summary>
    private static string? NoteFuer(Zufall z, SimSchueler? s, string fach)
    {
        var x = z.Anteil();
        if (x < 0.0015) return null;
        if (x < 0.0095) return "abs";
        if (x < 0.0135) return "aj";
        if (fach.StartsWith("Éducation physique", StringComparison.Ordinal) && x < 0.0235) return "disp";
        var wert = Math.Clamp(z.Normal(s?.Niveau ?? 11, 2.6), 0, 20);
        return NotenText(Math.Round((decimal)wert * 4) / 4);
    }

    /// <summary>Ende einer Periode: Bewertungen aus der Vorlage, Notenraster füllen, Conseil, ggf. Jahresentscheidungen, Veröffentlichung.</summary>
    private async Task PeriodeAbschliessenAsync(SimSchule s, Zufall z, SimPeriode p)
    {
        var jahr = s.Jahr;
        var letzte = p == jahr.Perioden[^1];
        var klassen = jahr.Klassen.Where(k => k.Session == 0 || k.Session == p.Nr).ToList();
        foreach (var k in klassen)
        {
            await NotenErfassenAsync(s, z, k, p, 1.0);
            var u = await SollAsync("Conseil laden", () => api.Conseil.UebersichtAsync(s.Direktor, k.Id, p.Id));
            if (u is null || u.Zeilen.Count == 0) continue;

            // Conseil: Klassenlehrer bereitet zwei Einträge vor, die Direktion ergänzt eine Sanktion.
            var lehrer = k.Faecher[0].Lehrer;
            foreach (var zeile in z.Mische(u.Zeilen).Take(2))
                await SollAsync("Conseil (Klassenlehrer)", () => api.Conseil.ConseilSpeichernAsync(lehrer, p.Id, zeile.EinschreibungId,
                    new ConseilEingabe(zeile.Moyenne.Wert >= 14 ? Distinktion.Felicitations : null, Sanktionen.Keine, "Élève sérieux, continuez ainsi.")));
            var schwach = u.Zeilen.Where(x => x.Moyenne.Wert is < 8).Take(1).ToList();
            foreach (var zeile in schwach)
                await SollAsync("Conseil (Direktion)", () => api.Conseil.ConseilSpeichernAsync(s.Direktor, p.Id, zeile.EinschreibungId,
                    new ConseilEingabe(null, Sanktionen.AvertissementTravail, "Travail insuffisant : un effort est attendu.")));

            if (letzte) await EntscheidungenAsync(s, z, k, p, u);

            var vor = await SollAsync("Conseil laden (Hash)", () => api.Conseil.UebersichtAsync(s.Direktor, k.Id, p.Id));
            if (vor is null) continue;
            var pub = await SollAsync($"Veröffentlichen {k.Name} {p.Titel} ({s.P.Name})", () => api.Bulletins.VeroeffentlichenAsync(
                s.Nr % 2 == 0 ? s.KoordMit : s.Direktor, k.Id, p.Id, vor.KlassenHash, trotzNichtKlassierter: true));
            if (pub is not null)
            {
                Zaehle("Veröffentlichungen");
                Zaehle("Bulletins", pub.Bulletins);
                Zaehle("Nicht klassiert", vor.NonClasses);
            }
        }

        // Eine Berichtigung je Schule und Jahr (abwechselnd), danach Periode schließen.
        if (p.Nr == 1 && (s.Nr + jahr.Beginn.Year) % 4 == 0) await BerichtigenAsync(s, z, klassen[0], p);
        await SollAsync("Periode schließen", () => api.Jahre.PeriodeStatusAsync(s.Direktor, p.Id, true));
        p.Abgeschlossen = true;
    }

    /// <summary>Notenraster einer Klasse für eine Periode: Bewertungen aus der Vorlage anlegen und (Anteil) Noten erfassen.</summary>
    private async Task NotenErfassenAsync(SimSchule s, Zufall z, SimKlasse k, SimPeriode p, double anteil)
    {
        foreach (var kf in k.Faecher)
        {
            var fach = s.Faecher.First(f => f.Id == kf.FachId).Name;
            await SollAsync("Bewertungen aus Vorlage", () => api.Bewertungen.AusKomponentenAsync(kf.Lehrer, kf.Id, p.Id));
            var raster = await SollAsync("Notenraster", () => api.Noten.RasterAsync(kf.Lehrer, kf.Id, p.Id));
            if (raster is null) continue;
            var noten = api.Noten;
            foreach (var zeile in raster.Zeilen)
            {
                if (anteil < 1 && !z.Ja(anteil)) continue;
                s.Schueler.TryGetValue(zeile.SchuelerId, out var schueler);
                for (var c = 0; c < raster.Spalten.Count; c++)
                {
                    if (!zeile.Zellen[c].Erwartet || zeile.Zellen[c].HatNote) continue;
                    var text = NoteFuer(z, schueler, fach);
                    if (text is null) { Zaehle("Noten fehlend"); continue; }
                    var stand = await SollAsync("Note", () => noten.NoteSpeichernAsync(kf.Lehrer, raster.Spalten[c].BewertungId, zeile.EinschreibungId, text));
                    if (stand is not null) Zaehle("Noten");
                }
            }
        }
    }

    /// <summary>Jahresentscheidungen der letzten Periode: Redoublement bei schwachem Mittel, seltene Exclusion/Départ, sonst « Admis » für alle offenen.</summary>
    private async Task EntscheidungenAsync(SimSchule s, Zufall z, SimKlasse k, SimPeriode p, KlassenratUebersicht u)
    {
        var sprache = s.P.Typ == SchulTyp.Langues;
        foreach (var zeile in u.Zeilen)
        {
            JahresEntscheidungsArt? e = null;
            string? bemerkung = null;
            if (!sprache && zeile.Moyenne.Wert is < 8.5m && z.Ja(0.7)) (e, bemerkung) = (JahresEntscheidungsArt.Redouble, "Moyenne annuelle insuffisante.");
            else if (!sprache && z.Ja(0.004)) (e, bemerkung) = (JahresEntscheidungsArt.Exclu, "Exclusion pour indiscipline grave.");
            else if (z.Ja(sprache ? 0.2 : 0.006)) (e, bemerkung) = (JahresEntscheidungsArt.Partie, "Départ annoncé par la famille.");
            if (e is { } entscheidung)
            {
                await SollAsync("Entscheidung", () => api.Conseil.EntscheidungSpeichernAsync(s.Direktor, p.Id, zeile.EinschreibungId,
                    new EntscheidungEingabe(entscheidung, null, bemerkung)));
                Zaehle($"Entscheidung {entscheidung}");
            }
        }
        var admis = await SollAsync("Offene: Admis", () => api.Wechsel.OffeneEntscheidenAsync(s.KoordMit, k.Id, JahresEntscheidungsArt.Admis));
        Zaehle("Entscheidung Admis", admis);
    }

    /// <summary>Berichtigung: Rectification öffnen, eine Note korrigieren, Vorschau prüfen, berichtigte Bulletins veröffentlichen.</summary>
    private async Task BerichtigenAsync(SimSchule s, Zufall z, SimKlasse k, SimPeriode p)
    {
        await SollAsync("Rectification öffnen", () => api.Bulletins.RectificationOeffnenAsync(s.Direktor, k.Id, p.Id, "Erreur de report d'une note de composition."));
        var kf = k.Faecher[1];
        var raster = await SollAsync("Raster (Rectification)", () => api.Noten.RasterAsync(kf.Lehrer, kf.Id, p.Id));
        var zeile = raster?.Zeilen.FirstOrDefault(r => r.Zellen[0].Punkte is < 18);
        if (raster is null || zeile is null) return;
        await SollAsync("Note berichtigen", () => api.Noten.NoteSpeichernAsync(kf.Lehrer, raster.Spalten[0].BewertungId, zeile.EinschreibungId,
            NotenText(zeile.Zellen[0].Punkte!.Value + 2)));
        var vorschau = await SollAsync("Rectification Vorschau", () => api.Bulletins.RectificationVorschauAsync(s.Direktor, k.Id, p.Id));
        if (vorschau is null) return;
        var n = await SollAsync("Rectification veröffentlichen", () => api.Bulletins.RectificationVeroeffentlichenAsync(s.KoordMit, k.Id, p.Id, vorschau.Hash));
        Zaehle("Berichtigungen");
        Zaehle("Bulletins berichtigt", n);
        if (vorschau.Aenderungen.Count == 0) Fehler.Enqueue($"Rectification ohne Änderung ({s.P.Name}, {k.Name}, {p.Titel}).");
    }

    /// <summary>Monatlicher Klassenappell durch den Klassenlehrer (Standard « présent », einige Absences/Retards).</summary>
    private async Task AppellAsync(SimSchule s, Zufall z, DateOnly heute)
    {
        var tag = heute.DayOfWeek switch { DayOfWeek.Saturday => heute.AddDays(-1), DayOfWeek.Sunday => heute.AddDays(-2), _ => heute };
        var jahr = s.Jahr;
        foreach (var k in jahr.Klassen)
        {
            if (k.Session > 0 && !(jahr.Perioden[k.Session - 1].Beginn <= tag && tag <= jahr.Perioden[k.Session - 1].Ende)) continue;
            var lehrer = k.Faecher[0].Lehrer;
            var appell = await SollAsync("Appell laden", () => api.Anwesenheit.AppellAsync(lehrer, k.Id, tag));
            if (appell is null || appell.Sperre is not null || appell.Zeilen.Count == 0) continue;
            var eingaben = new List<AppellEingabe>();
            foreach (var zeile in appell.Zeilen)
            {
                var x = z.Anteil();
                if (x < 0.05) eingaben.Add(new AppellEingabe(zeile.EinschreibungId, AnwesenheitsStatus.Absent, null, z.Ja(0.3), z.Ja(0.3) ? "Malade" : null));
                else if (x < 0.08) eingaben.Add(new AppellEingabe(zeile.EinschreibungId, AnwesenheitsStatus.Retard, z.Zahl(5, 31), false, null));
            }
            await SollAsync("Appell speichern", () => api.Anwesenheit.AppellSpeichernAsync(lehrer, k.Id, tag, eingaben));
            Zaehle("Appelle");
        }
    }

    // ---------------------------------------------------------------- Bewegungen im Jahr

    /// <summary>Austritte, Klassenwechsel, späte und rückwirkende Einschreibungen.</summary>
    private async Task BewegungenAsync(SimSchule s, Zufall z, DateOnly heute)
    {
        if (s.P.Typ == SchulTyp.Langues) return;
        var jahr = s.Jahr;
        var akteur = z.Ja(0.5) ? s.Direktor : s.KoordMit;
        foreach (var schueler in s.Aktive.ToList())
        {
            var x = z.Anteil();
            if (x < 0.0011)
            {
                var grund = z.Eins(new[] { "Déménagement de la famille.", "Raisons financières.", "Transfert vers un autre établissement." });
                var archiv = z.Ja(0.5);
                await SollAsync("Austritt", () => api.Einschreibung.AustrittAsync(akteur, schueler.Id, heute, grund, archiv));
                schueler.Aktiv = false;
                Zaehle("Austritte im Jahr");
            }
            else if (x < 0.0021 && s.P.KlassenJeStufe > 1 && heute > jahr.Beginn)
            {
                var ziel = jahr.Klassen.FirstOrDefault(k => k.Stufe == schueler.Klasse!.Stufe && k != schueler.Klasse);
                if (ziel is null) continue;
                var neu = await SollAsync("Klassenwechsel", () => api.Einschreibung.KlasseWechselnAsync(akteur, schueler.Id, ziel.Id, heute));
                if (neu == Guid.Empty) continue;
                schueler.Klasse = ziel;
                Zaehle("Klassenwechsel");
            }
        }
        if (heute.Month is >= 9 or <= 3 && z.Ja(0.35))
        {
            var klasse = z.Eins(jahr.Klassen);
            var spaet = await SchuelerAnlegenAsync(s, z, klasse, akteur);
            Zaehle("Späte Einschreibungen");
            if (spaet is not null && z.Ja(0.12))
            {
                // Irrtum: Einschreibung annullieren (zählt weder für Gebühren noch für die Abrechnung).
                var id = await d.MitDbAsync(db => db.Einschreibungen.Where(e => e.SchuelerId == spaet.Id && e.Status == EinschreibungsStatus.Active).Select(e => e.Id).SingleAsync());
                var ok = await SollAsync("Einschreibung annullieren", async () =>
                {
                    await api.Einschreibung.StornierenAsync(s.Direktor, id, "Erreur d'inscription : élève inscrit dans la mauvaise classe.");
                    return true;
                });
                if (ok)
                {
                    spaet.Konten.Remove(jahr.Id);
                    spaet.Aktiv = false;
                    spaet.Klasse = null;
                    Zaehle("Einschreibungen annulliert");
                }
            }
        }
        // Rückwirkend: am 15/01 in einigen Schulen ein Schüler ab Oktober (Periode 1 ist bereits abgerechnet → Nachtragsrechnung).
        if (heute.Month == 1 && heute.Day == 15 && s.Nr % 5 == 2)
        {
            var klasse = jahr.Klassen[0];
            var neu = await SchuelerAnlegenAsync(s, z, null, s.Direktor);
            if (neu is null) return;
            var von = jahr.Perioden[0].Beginn.AddDays(40);
            var e = await SollAsync("Rückwirkende Einschreibung", () => api.Einschreibung.EinschreibenAsync(s.Direktor, neu.Id, klasse.Id, von, false));
            if (e == Guid.Empty) return;
            neu.Klasse = klasse;
            neu.Aktiv = true;
            KontoFuer(s, neu, klasse);
            Zaehle("Rückwirkende Einschreibungen");
        }
    }

    /// <summary>
    /// Sprachschule nach einer Session: weiter in die Klasse der nächsten Session (teils Stufe höher), Austritt, vergessen
    /// (weder Wechsel noch Austritt – die Einschreibung endet mit der Session) oder neue Schüler. Vergessene früherer
    /// Sessions kommen teils zurück (« Inscrire » bzw. « Changer de classe ») oder werden aus « Sans classe » archiviert.
    /// </summary>
    private async Task SessionWechselAsync(SimSchule s, Zufall z, SimPeriode p)
    {
        var jahr = s.Jahr;
        if (p.Nr >= jahr.Perioden.Count) return;
        var naechste = jahr.Perioden[p.Nr];
        foreach (var v in s.Vergessene.Where(v => v.JahrId == jahr.Id && v.ZurueckAb is null && !v.Archiviert && v.Session < p.Nr).ToList())
        {
            var x = z.Anteil();
            if (x < 0.35) await ZurueckAsync(s, z, v, naechste);
            else if (x < 0.45) await VergessenArchivierenAsync(s, v);
        }
        foreach (var schueler in s.Aktive.Where(x => x.Klasse!.Session == p.Nr && !x.SessionVergessen).ToList())
        {
            if (z.Ja(0.06))
            {
                // E7b: die Direktion vergisst den Teilnehmer – die Einschreibung endet trotzdem mit der Session
                // (keine Abrechnung/Gebühr der Folgesessions, nicht mehr in den Listen).
                schueler.SessionVergessen = true;
                lock (s.Vergessene) s.Vergessene.Add(new SimVergessen(schueler, jahr.Id, p.Nr));
                Zaehle("Session ohne Wechsel/Austritt");
                continue;
            }
            if (z.Ja(0.25))
            {
                await SollAsync("Austritt (Session)", () => api.Einschreibung.AustrittAsync(s.Direktor, schueler.Id, p.Ende, "Fin de la session.", false));
                schueler.Aktiv = false;
                Zaehle("Austritte Session");
                continue;
            }
            var stufe = schueler.Klasse!.Stufe;
            if (z.Ja(0.3) && stufe.Nr < s.Stufen.Count) stufe = s.Stufen[stufe.Nr];
            var ziel = jahr.Klassen.First(k => k.Stufe == stufe && k.Session == naechste.Nr);
            var e = await SollAsync("Sessionwechsel", () => api.Einschreibung.KlasseWechselnAsync(s.KoordMit, schueler.Id, ziel.Id, naechste.Beginn));
            if (e != Guid.Empty) { schueler.Klasse = ziel; KontoFuer(s, schueler, ziel); Zaehle("Sessionwechsel"); }
        }
        foreach (var k in jahr.Klassen.Where(k => k.Session == naechste.Nr))
            for (var i = 0; i < Groesse(s, z) / 3; i++)
                await NeuEinschreibenAsync(s, z, k, naechste.Beginn);
        await NachSessionEndePruefenAsync(s);
    }

    /// <summary>Ein vergessener Teilnehmer kommt in der nächsten Session wieder (je zur Hälfte « Inscrire » bzw. « Changer de classe »).</summary>
    private async Task ZurueckAsync(SimSchule s, Zufall z, SimVergessen v, SimPeriode naechste)
    {
        var schueler = v.Schueler;
        var ziel = s.Jahr.Klassen.First(k => k.Stufe == schueler.Klasse!.Stufe && k.Session == naechste.Nr);
        var inscrire = z.Ja(0.5);
        // « Inscrire » schließt die abgelaufene Session-Einschreibung selbst (Terminée zum Session-Ende).
        var e = inscrire
            ? await SollAsync("Rückkehr (Inscrire)", () => api.Einschreibung.EinschreibenAsync(s.KoordMit, schueler.Id, ziel.Id, naechste.Beginn, false))
            : await SollAsync("Rückkehr (Changer de classe)", () => api.Einschreibung.KlasseWechselnAsync(s.Direktor, schueler.Id, ziel.Id, naechste.Beginn));
        if (e == Guid.Empty) return;
        schueler.Klasse = ziel;
        schueler.SessionVergessen = false;
        v.ZurueckAb = naechste.Nr;
        KontoFuer(s, schueler, ziel);
        Zaehle(inscrire ? "Vergessene zurück (Inscrire)" : "Vergessene zurück (Changer de classe)");
    }

    /// <summary>Die Direktion archiviert einen vergessenen Teilnehmer aus « Sans classe » (seine Session ist vorbei).</summary>
    private async Task VergessenArchivierenAsync(SimSchule s, SimVergessen v)
    {
        var ok = await SollAsync("Vergessenen archivieren", async () => { await api.Konten.ArchivierenAsync(s.Direktor, v.Schueler.Id); return true; });
        if (!ok) return;
        v.Archiviert = true;
        v.Schueler.Aktiv = false;
        v.Schueler.SessionVergessen = false;
        Zaehle("Vergessene archiviert");
    }

    /// <summary>
    /// Nach dem Ende einer Session (Invariante « kein Schüler nach Session-Ende in Listen »): beendete Session-Klassen haben
    /// in « Classes », in den Auswahllisten (Inscrire, Appel) kein Effectif mehr, die Schülerliste zeigt vergessene Teilnehmer
    /// « sans classe », die Eltern sehen keine beendete Klasse, der Appell der alten Klasse ist leer.
    /// </summary>
    private async Task NachSessionEndePruefenAsync(SimSchule s)
    {
        var jahr = s.Jahr;
        var heute = d.HeuteDatum;
        var beendet = jahr.Klassen.Where(k => k.Session > 0 && jahr.Perioden[k.Session - 1].Ende < heute).ToDictionary(k => k.Id);
        if (beendet.Count == 0) return;
        var wo = $"{s.P.Name} {heute:dd/MM/yyyy}";
        foreach (var k in await api.Klassen.KlassenAsync(s.Direktor, jahr.Id))
            if (beendet.ContainsKey(k.Id)) Pruefe(k.Effectif == 0, $"{wo}: « Classes » zeigt {k.Name} (Session beendet) mit Effectif {k.Effectif}.");
        // Zusatz der Auswahl: « 0 élève » bzw. mit Kapazität « 0/60 élèves ».
        static bool OhneEffectif(string? zusatz) => zusatz is not null && (zusatz.StartsWith("0 ", StringComparison.Ordinal) || zusatz.StartsWith("0/", StringComparison.Ordinal));
        foreach (var k in await api.Klassen.KlassenAuswahlAsync(s.Direktor, jahr.Id))
            if (beendet.ContainsKey(k.Id)) Pruefe(OhneEffectif(k.Zusatz), $"{wo}: Auswahl « Inscrire » zeigt {k.Name} (Session beendet) mit « {k.Zusatz} ».");
        foreach (var k in await api.Anwesenheit.KlassenAsync(s.Direktor, jahr.Id))
            if (beendet.ContainsKey(k.Id)) Pruefe(OhneEffectif(k.Zusatz), $"{wo}: Auswahl « Appel » zeigt {k.Name} (Session beendet) mit « {k.Zusatz} ».");
        foreach (var (kid, klasse) in beendet)
        {
            var liste = await api.Schueler.SucheAsync(s.Direktor, null, kid, null, 0, 50);
            Pruefe(liste.Eintraege.Count == 0, $"{wo}: « Élèves » der Klasse {klasse.Name} (Session beendet) zeigt {liste.Eintraege.Count} Schüler.");
        }
        List<SimVergessen> vergessene;
        lock (s.Vergessene) vergessene = s.Vergessene.Where(v => v.JahrId == jahr.Id && v.ZurueckAb is null && !v.Archiviert).ToList();
        foreach (var v in vergessene)
        {
            var x = v.Schueler;
            var zeile = (await api.Schueler.SucheAsync(s.Direktor, SchuelerService.FilterSansClasse, null, $"{x.Vorname} {x.Nachname}", 0, 200))
                .Eintraege.FirstOrDefault(r => r.KontoId == x.Id);
            Pruefe(zeile is not null && zeile.KlasseId is null, $"{wo}: {x.Nachname} {x.Vorname} (Session {v.Session} beendet) nicht « sans classe » ({zeile?.Klasse ?? "fehlt"}).");
            if (x.Eltern is { } eltern)
            {
                var kind = (await api.ElternKinder.KinderAsync(eltern)).FirstOrDefault(k => k.SchuelerId == x.Id);
                Pruefe(kind?.Klasse is null || !beendet.Values.Any(k => k.Name == kind.Klasse), $"{wo}: Eltern sehen {x.Vorname} {x.Nachname} noch in {kind?.Klasse} (Session beendet).");
            }
            Zaehle("Listenprüfung: vergessene Teilnehmer");
        }
        var tag = heute.DayOfWeek switch { DayOfWeek.Saturday => heute.AddDays(-1), DayOfWeek.Sunday => heute.AddDays(-2), _ => heute };
        foreach (var v in vergessene.Where(v => jahr.Perioden[v.Session - 1].Ende < tag).Take(2))
        {
            var klasse = v.Schueler.Klasse!;
            var appell = await SollAsync("Appell (alte Session)", () => api.Anwesenheit.AppellAsync(s.Direktor, klasse.Id, tag));
            Pruefe(appell is null || appell.Zeilen.All(r => r.SchuelerId != v.Schueler.Id), $"{wo}: Appell der Klasse {klasse.Name} (Session beendet) enthält {v.Schueler.Nachname}.");
        }
        Zaehle("Listenprüfungen nach Session-Ende");
    }

    /// <summary>Konto ohne Klasse anlegen und ab einem Datum einschreiben (Session- oder rückwirkende Einschreibung).</summary>
    private async Task NeuEinschreibenAsync(SimSchule s, Zufall z, SimKlasse klasse, DateOnly von)
    {
        var neu = await SchuelerAnlegenAsync(s, z, null, s.KoordMit);
        if (neu is null) return;
        var e = await SollAsync("Einschreiben ab Datum", () => api.Einschreibung.EinschreibenAsync(s.KoordMit, neu.Id, klasse.Id, von, false));
        if (e == Guid.Empty) return;
        neu.Klasse = klasse;
        neu.Aktiv = true;
        KontoFuer(s, neu, klasse);
    }

    // ---------------------------------------------------------------- Jahreswechsel

    /// <summary>Assistent « Passage »: Plan, Vorschau, Ausführung; neues Jahr übernehmen, Gebühren kopieren, starten, neue Schüler.</summary>
    private async Task PassageAsync(SimSchule s, Zufall z)
    {
        var alt = s.Jahr;
        // Vergessene Session-Teilnehmer: die Hälfte trägt die Direktion zum Jahresende aus, die übrigen bleiben unberührt –
        // sie dürfen weder in der Liste des Passage-Assistenten noch im neuen Jahr erscheinen (ihre Session ist vorbei).
        var unberuehrt = new List<SimSchueler>();
        foreach (var schueler in s.Aktive.Where(x => x.SessionVergessen).ToList())
        {
            if (z.Ja(0.5)) unberuehrt.Add(schueler);
            else await SollAsync("Austritt (Session vergessen)", () => api.Einschreibung.AustrittAsync(s.Direktor, schueler.Id, alt.Ende, "Fin d'année : session terminée.", false));
            schueler.Aktiv = false;
            schueler.SessionVergessen = false;
        }
        var wechsel = api.Wechsel;
        var plan = await wechsel.PlanAsync(s.Direktor, alt.Id);
        if (s.P.Typ == SchulTyp.Langues)
        {
            // Admis der letzten Session beginnen das neue Jahr in der ersten Session der nächsten Stufe.
            foreach (var k in alt.Klassen.Where(k => k.Session == alt.Perioden.Count))
                plan.ZielAdmis[k.Id] = k.Stufe.Nr < s.Stufen.Count ? alt.Klassen.First(x => x.Stufe == s.Stufen[k.Stufe.Nr] && x.Session == 1).Id : null;
        }
        var vorschau = await wechsel.VorschauAsync(s.Direktor, alt.Id, plan);
        if (!vorschau.Bereit)
            Fehler.Enqueue($"Passage {s.P.Name} nicht bereit: offen {vorschau.Offen}; {string.Join(" | ", vorschau.Warnungen)}");
        if (unberuehrt.Count > 0)
        {
            var inListe = vorschau.Klassen.SelectMany(k => k.Schueler).Select(x => x.EinschreibungId).ToHashSet();
            var ids = unberuehrt.Select(x => x.Id).ToList();
            var gelistet = await d.MitDbAsync(db => db.Einschreibungen.Where(e => e.SchuljahrId == alt.Id && ids.Contains(e.SchuelerId))
                .Select(e => e.Id).ToListAsync());
            Pruefe(!gelistet.Any(inListe.Contains), $"Passage {s.P.Name} {alt.Bezeichnung}: {gelistet.Count(inListe.Contains)} vergessene Teilnehmer in der Liste des Assistenten.");
        }
        var ergebnis = await SollAsync($"Passage {s.P.Name}", () => wechsel.AusfuehrenAsync(s.KoordMit, alt.Id, plan));
        if (ergebnis is null) return;
        Zaehle("Passagen");
        Zaehle("Passage: Einschreibungen", ergebnis.Einschreibungen);
        Zaehle("Passage: Sortants", ergebnis.Sortants);

        await PassagePruefenAsync(s, alt, ergebnis.NeuesJahrId);
        if (unberuehrt.Count > 0)
        {
            var ids = unberuehrt.Select(x => x.Id).ToList();
            var danach = await d.MitDbAsync(db => db.Einschreibungen.Where(e => ids.Contains(e.SchuelerId) && e.StorniertAmUtc == null
                    && (e.SchuljahrId == ergebnis.NeuesJahrId || (e.SchuljahrId == alt.Id && e.Status == EinschreibungsStatus.Active)))
                .Select(e => new { e.SchuelerId, e.SchuljahrId }).ToListAsync());
            foreach (var e in danach)
                Pruefe(false, $"Passage {s.P.Name} {alt.Bezeichnung}: vergessener Teilnehmer {e.SchuelerId} {(e.SchuljahrId == alt.Id ? "bleibt im alten Jahr « active »" : "ist im neuen Jahr eingeschrieben")}.");
            Zaehle("Passage: vergessene Teilnehmer unberührt", unberuehrt.Count);
        }
        var jahr = await JahrLadenAsync(s, ergebnis.NeuesJahrId);
        s.Jahre.Add(jahr);
        foreach (var k in await api.Klassen.KlassenAsync(s.Direktor, jahr.Id))
        {
            var stufe = s.Stufen.First(x => x.Id == k.StufeId);
            var session = 0;
            if (s.P.Typ == SchulTyp.Langues)
            {
                session = jahr.Perioden.FindIndex(p => p.Id == k.PeriodeId) + 1;
                if (session == 0)
                {
                    Fehler.Enqueue($"Passage {s.P.Name}: Klasse {k.Name} hat im neuen Jahr keine Session.");
                    session = int.Parse(k.Name[^1..], CultureInfo.InvariantCulture);
                }
            }
            var klasse = new SimKlasse(k.Id, k.Name, stufe, session);
            foreach (var kf in await api.Unterricht.KlassenfaecherAsync(s.Direktor, k.Id))
                klasse.Faecher.Add(new SimKlassenfach(kf.Id, kf.FachId, kf.Lehrer.FirstOrDefault()?.LehrerId ?? s.Direktor));
            jahr.Klassen.Add(klasse);
        }

        // Wer ist im neuen Jahr eingeschrieben (Lesen für die Buchführung der Simulation)?
        var neu = await d.MitDbAsync(db => db.Einschreibungen.Where(e => e.SchuljahrId == jahr.Id && e.StorniertAmUtc == null)
            .Select(e => new { e.SchuelerId, e.KlasseId }).ToListAsync());
        var klasseVon = neu.ToDictionary(e => e.SchuelerId, e => e.KlasseId);
        foreach (var schueler in s.Schueler.Values)
        {
            if (klasseVon.TryGetValue(schueler.Id, out var kid))
            {
                schueler.Klasse = jahr.Klassen.First(k => k.Id == kid);
                schueler.Aktiv = true;
            }
            else schueler.Aktiv = false;
        }

        // Gebühren: Vorjahr kopieren, in einem Teil der Schulen die Scolarité um 5 % erhöhen.
        if (s.P.Gebuehr == SessionGebuehr.GleicheBezeichnung)
        {
            // Die Direktion legt « Frais de session » der ersten Session schon selbst an und kopiert dann das Vorjahr:
            // die gleichnamigen Posten der übrigen Sessions müssen trotzdem übernommen werden.
            var erste = jahr.Perioden[0];
            var betrag = alt.Posten.First(p => p.Name == "Frais de session").Betrag;
            await SollAsync("Session-Posten vorab", () => api.Gebuehren.PostenSpeichernAsync(s.Direktor, jahr.Id, null,
                new PostenEingabe("Frais de session", betrag, null, null, true, Raten(betrag, erste.Beginn, erste.Beginn.AddDays(21)), erste.Id)));
        }
        await SollAsync("Gebühren kopieren", () => api.Gebuehren.VorjahrKopierenAsync(s.KoordMit, jahr.Id));
        await PostenLadenAsync(s, jahr);
        if (jahr.Posten.Count != alt.Posten.Count)
            Fehler.Enqueue($"{d.HeuteDatum:dd/MM/yyyy} Gebühren kopieren ({s.P.Name}, {jahr.Bezeichnung}): {jahr.Posten.Count} Posten statt {alt.Posten.Count} wie im Vorjahr.");
        Zaehle("Gebühren kopiert", jahr.Posten.Count);
        if (s.Nr % 3 == 0)
            foreach (var p in jahr.Posten.Where(p => p.Name.StartsWith("Scolarité", StringComparison.Ordinal) || (p.PeriodeId is not null && p.KlasseId is null)).ToList())
            {
                // Sprachschule: ein Posten einer Session behält beim Ändern seine Session.
                var neuBetrag = (long)Math.Round(p.Betrag * 1.05m / 1_000m) * 1_000;
                await SollAsync("Posten erhöhen", () => api.Gebuehren.PostenSpeichernAsync(s.Direktor, jahr.Id, p.Id,
                    new PostenEingabe(p.Name, neuBetrag, p.StufeId, p.KlasseId, true, Raten(neuBetrag, p.Raten.Select(r => r.Faellig).ToArray()), p.PeriodeId)));
            }
        await PostenLadenAsync(s, jahr);
        foreach (var schueler in s.Aktive) KontoFuer(s, schueler, schueler.Klasse!);

        await SollAsync("Jahr starten", () => api.Jahre.JahrStartenAsync(s.Direktor, jahr.Id));
        jahr.Preis = await PreisAsync(jahr.Id);

        // Neue Schüler: erste Stufe auffüllen (Sprachschule: Klassen der ersten Session), einzelne Zugänge in höheren Stufen.
        foreach (var k in jahr.Klassen.Where(k => s.P.Typ == SchulTyp.Langues ? k.Session == 1 : true))
        {
            var vorhanden = s.Aktive.Count(x => x.Klasse == k);
            var ziel = k.Stufe.Nr == 1 || s.P.Typ == SchulTyp.Langues ? Groesse(s, z) : vorhanden + z.Zahl(0, 3);
            for (var i = vorhanden; i < ziel; i++) await SchuelerAnlegenAsync(s, z, k, z.Ja(0.5) ? s.Direktor : s.KoordMit);
        }
        if (s.P.Typ != SchulTyp.Langues)
            foreach (var schueler in s.Aktive.Where(_ => z.Ja(0.03)).ToList())
                await ErmaessigungAsync(s, schueler, 50, "Bourse d'excellence");
        await SelbstregistrierungenAsync(s, z);
    }

    /// <summary>Prüft die Ausführung: Admis → Folgestufe, Redouble → gleiche Stufe als Wiederholer, Exclu/Partie → keine Einschreibung.</summary>
    private async Task PassagePruefenAsync(SimSchule s, SimJahr alt, Guid neuesJahr)
    {
        var daten = await d.MitDbAsync(async db => new
        {
            Alt = await (from j in db.Jahresentscheidungen
                         join e in db.Einschreibungen on j.EinschreibungId equals e.Id
                         join k in db.Klassen on e.KlasseId equals k.Id
                         where e.SchuljahrId == alt.Id
                         select new { e.SchuelerId, j.Entscheidung, k.StufeId }).ToListAsync(),
            Neu = await (from e in db.Einschreibungen
                         join k in db.Klassen on e.KlasseId equals k.Id
                         where e.SchuljahrId == neuesJahr
                         select new { e.SchuelerId, k.StufeId, e.IstWiederholer }).ToDictionaryAsync(x => x.SchuelerId)
        });
        foreach (var a in daten.Alt)
        {
            var stufe = s.Stufen.First(x => x.Id == a.StufeId);
            var folge = stufe.Nr < s.Stufen.Count ? s.Stufen[stufe.Nr].Id : (Guid?)null;
            daten.Neu.TryGetValue(a.SchuelerId, out var n);
            var ok = a.Entscheidung switch
            {
                JahresEntscheidungsArt.Admis => folge is null ? n is null : n is not null && n.StufeId == folge && !n.IstWiederholer,
                JahresEntscheidungsArt.Redouble => n is not null && n.StufeId == a.StufeId && n.IstWiederholer,
                _ => n is null
            };
            if (!ok) Fehler.Enqueue($"Passage {s.P.Name} {alt.Bezeichnung}: Schüler {a.SchuelerId} mit « {a.Entscheidung} » aus {stufe.Name} → {(n is null ? "keine Einschreibung" : s.Stufen.First(x => x.Id == n.StufeId).Name)}.");
        }
        Zaehle("Passage: Entscheidungen geprüft", daten.Alt.Count);
    }

    /// <summary>Laufendes Jahr bis heute: erste Noten der laufenden Periode in einem Teil der Klassen (noch nicht veröffentlicht).</summary>
    private async Task AktuellePeriodeAsync(SimSchule s, Zufall z)
    {
        var p = s.Jahr.Perioden[0];
        foreach (var k in s.Jahr.Klassen.Where(k => k.Session <= 1).Where((_, i) => i % 3 == 0))
            await NotenErfassenAsync(s, z, k, p, 0.8);
    }
}
