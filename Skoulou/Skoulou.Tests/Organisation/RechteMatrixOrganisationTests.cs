using ClosedXML.Excel;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Organisation.Import;

namespace Skoulou.Tests.Organisation;

/// <summary>
/// Rechte-Matrix je Service-Methode des Bereichs Organisation: für jede Rolle (D Direktor, K Koordinator, L Lehrer,
/// S Schüler, E Eltern, R Responsable, A SuperAdmin) erlaubt oder <see cref="KeinZugriffFehler"/>; « erlaubt » heißt,
/// die Rechteprüfung wurde bestanden (eine fachliche Meldung danach zählt als erlaubt). Ressourcen der Schule A sind
/// für den Direktor der Schule B tabu.
/// </summary>
public class RechteMatrixOrganisationTests
{
    private const string Rollenfolge = "DKLSERA";

    /// <summary>Operation, erlaubte Rollen, Prüfung der fremden Schule.</summary>
    private sealed record Fall(Func<OrgWelt, Guid, Task> Aktion, string Erlaubt, bool Fremd);

    private static Fall F(Func<OrgWelt, Guid, Task> aktion, string erlaubt = "DK", bool fremd = false) => new(aktion, erlaubt, fremd);

    private static Stream Png() => new MemoryStream(OrgWelt.Png);

    private static Stream LehrerXlsx()
    {
        using var mappe = new XLWorkbook();
        var blatt = mappe.Worksheets.Add("Enseignants");
        blatt.Cell(1, 1).Value = "Prénom";
        blatt.Cell(1, 2).Value = "Nom";
        blatt.Cell(2, 1).Value = "Kofi";
        blatt.Cell(2, 2).Value = "Mensah";
        var ziel = new MemoryStream();
        mappe.SaveAs(ziel);
        ziel.Position = 0;
        return ziel;
    }

    private static readonly Dictionary<string, Fall> Faelle = new()
    {
        // SchulService – nur der Direktor (Koordinator ändert keine Stammdaten)
        ["Schule.Lade"] = F((w, a) => w.D.Schule.LadeAsync(a), "D"),
        ["Schule.Stammdaten"] = F((w, a) => w.D.Schule.StammdatenSpeichernAsync(a, new SchulStammEingabe(w.SchulNameA, "Lomé", "Togo")), "D"),
        ["Schule.Dokumente"] = F((w, a) => w.D.Schule.DokumenteSpeichernAsync(a, new DokumentEingabe("Collège Alpha", "Devise", "Lomé", "+228 90 00 00 00",
            "contact@exemple.tg", "N° 12", "Ministère", "RÉPUBLIQUE TOGOLAISE", "Travail – Liberté – Patrie", "DRE Golfe", "Privé")), "D"),
        ["Schule.Einstellungen"] = F((w, a) => w.D.Schule.EinstellungenSpeichernAsync(a, new SchulEinstellungEingabe("CA", true, false)), "D"),
        ["Schule.Logo"] = F((w, a) => w.D.Schule.LogoHochladenAsync(a, Png(), "logo.png"), "D"),
        ["Schule.LogoEntfernen"] = F((w, a) => w.D.Schule.LogoEntfernenAsync(a), "D"),
        ["Schule.Koordinatoren"] = F((w, a) => w.D.Schule.KoordinatorenAsync(a), "D"),
        ["Schule.KoordinatorRechte"] = F((w, a) => w.D.Schule.KoordinatorRechteSetzenAsync(a, w.KoordZiel, KoordinatorRechte.FinanzenEinsehen), "D", true),

        // SchuljahrService
        ["Jahr.Liste"] = F((w, a) => w.D.Jahre.JahreAsync(a)),
        ["Jahr.Vorschlag"] = F((w, a) => w.D.Jahre.VorschlagAsync(a)),
        ["Jahr.Anlegen"] = F((w, a) => w.D.Jahre.JahrAnlegenAsync(a, new SchuljahrEingabe("2028-2029", new(2028, 9, 1), new(2029, 6, 30), PeriodenArt.Semestre, 2))),
        ["Jahr.Aendern"] = F((w, a) => w.D.Jahre.JahrAendernAsync(a, w.JahrVorb, "2027-2028", new(2027, 9, 1), new(2028, 6, 30)), fremd: true),
        ["Jahr.Loeschen"] = F((w, a) => w.D.Jahre.JahrLoeschenAsync(a, w.JahrVorb), fremd: true),
        ["Jahr.Struktur"] = F((w, a) => w.D.Jahre.StrukturNeuAsync(a, w.JahrVorb, PeriodenArt.Semestre, 2), fremd: true),
        ["Jahr.PeriodeNeu"] = F((w, a) => w.D.Jahre.PeriodeHinzufuegenAsync(a, w.JahrVorb, new PeriodeEingabe("Examen blanc", new(2028, 5, 1), new(2028, 5, 10), 1, false)), fremd: true),
        ["Jahr.PeriodeAendern"] = F((w, a) => w.D.Jahre.PeriodeAendernAsync(a, w.P1, new PeriodeEingabe("1er trimestre", new(2026, 9, 1), new(2026, 12, 18), 1, true)), fremd: true),
        ["Jahr.PeriodeLoeschen"] = F((w, a) => w.D.Jahre.PeriodeLoeschenAsync(a, w.PV3), fremd: true),
        ["Jahr.PeriodeStatus"] = F((w, a) => w.D.Jahre.PeriodeStatusAsync(a, w.P3, true), fremd: true),
        ["Jahr.Starten"] = F((w, a) => w.D.Jahre.JahrStartenAsync(a, w.JahrVorb), fremd: true),
        ["Jahr.Abschliessen"] = F((w, a) => w.D.Jahre.JahrAbschliessenAsync(a, w.Jahr), fremd: true),
        ["Jahr.Wiedereroeffnen"] = F((w, a) => w.D.Jahre.JahrWiedereroeffnenAsync(a, w.Jahr, "Correction d'une erreur"), "D", true),

        // KlassenService
        ["Stufen"] = F((w, a) => w.D.Klassen.StufenAsync(a)),
        ["Stufe.Speichern"] = F((w, a) => w.D.Klassen.StufeSpeichernAsync(a, null, new StufeEingabe("4e", Zyklus.College, null))),
        ["Stufe.Loeschen"] = F((w, a) => w.D.Klassen.StufeLoeschenAsync(a, w.Stufe5), fremd: true),
        ["Stufe.Aktivieren"] = F((w, a) => w.D.Klassen.StufeAktivierenAsync(a, w.Stufe5), fremd: true),
        ["Stufen.Reihenfolge"] = F((w, a) => w.D.Klassen.StufenReihenfolgeAsync(a, [w.Stufe5, w.Stufe6])),
        ["Klassen"] = F((w, a) => w.D.Klassen.KlassenAsync(a, w.Jahr), fremd: true),
        ["Klassen.Auswahl"] = F((w, a) => w.D.Klassen.KlassenAuswahlAsync(a, w.Jahr), fremd: true),
        ["Lehrkraefte"] = F((w, a) => w.D.Klassen.LehrkraefteAsync(a)),
        ["Klasse.Speichern"] = F((w, a) => w.D.Klassen.KlasseSpeichernAsync(a, w.Jahr, null, new KlasseEingabe("6e C", w.Stufe6, null, 40, w.Lehrer, "Salle 9")), fremd: true),
        ["Klasse.Loeschen"] = F((w, a) => w.D.Klassen.KlasseLoeschenAsync(a, w.Klasse5A), fremd: true),
        ["Klasse.Aktivieren"] = F((w, a) => w.D.Klassen.KlasseAktivierenAsync(a, w.Klasse5A), fremd: true),

        // FaecherService
        ["Faecher"] = F((w, a) => w.D.Faecher.FaecherAsync(a)),
        ["Fach.Speichern"] = F((w, a) => w.D.Faecher.FachSpeichernAsync(a, null, new FachEingabe("Anglais", "ANG", FachGruppe.Lettres))),
        ["Fach.Loeschen"] = F((w, a) => w.D.Faecher.FachLoeschenAsync(a, w.FachFr), fremd: true),
        ["Fach.Aktivieren"] = F((w, a) => w.D.Faecher.FachAktivierenAsync(a, w.FachFr), fremd: true),
        ["Fach.Verschieben"] = F((w, a) => w.D.Faecher.VerschiebenAsync(a, w.FachFr, -1)),

        // UnterrichtService
        ["Klassenfaecher"] = F((w, a) => w.D.Unterricht.KlassenfaecherAsync(a, w.Klasse6A), fremd: true),
        ["Faecher.Hinzufuegen"] = F((w, a) => w.D.Unterricht.FaecherHinzufuegenAsync(a, w.Klasse6B, [w.FachFr], 2), fremd: true),
        ["Koeffizient"] = F((w, a) => w.D.Unterricht.KoeffizientSetzenAsync(a, w.Kf6AFr, 2.5m), fremd: true),
        ["Klassenfach.Entfernen"] = F((w, a) => w.D.Unterricht.KlassenfachEntfernenAsync(a, w.Kf6AFr), fremd: true),
        ["Koeffizienten.Kopieren"] = F((w, a) => w.D.Unterricht.KoeffizientenKopierenAsync(a, w.Klasse6A, w.Klasse6B), fremd: true),
        ["Koeffizienten.Stufe"] = F((w, a) => w.D.Unterricht.KoeffizientenAufStufeAsync(a, w.Klasse6A), fremd: true),
        ["Lehrauftrag.Setzen"] = F((w, a) => w.D.Unterricht.LehrauftragSetzenAsync(a, w.Kf6AFr, w.Lehrer2, 2), fremd: true),
        ["Lehrauftrag.Entfernen"] = F((w, a) => w.D.Unterricht.LehrauftragEntfernenAsync(a, w.Auftrag6B), fremd: true),
        ["Auftraege.Lehrer"] = F((w, a) => w.D.Unterricht.AuftraegeDesLehrersAsync(a, w.Lehrer, w.Jahr), fremd: true),
        ["Klassenfaecher.Jahr"] = F((w, a) => w.D.Unterricht.KlassenfaecherDesJahresAsync(a, w.Jahr), fremd: true),
        ["Wahlfach.Setzen"] = F((w, a) => w.D.Unterricht.WahlfachSetzenAsync(a, w.Kf6AFr, true, WahlfachRegel.PunkteUeber10), fremd: true),
        ["Fachwahlen"] = F((w, a) => w.D.Unterricht.FachwahlenAsync(a, w.Klasse6A), fremd: true),
        ["Fachwahlen.Speichern"] = F((w, a) => w.D.Unterricht.FachwahlenSpeichernAsync(a, w.Klasse6A, []), fremd: true),

        // KontoVerwaltungService (Koordinatoren nur der Direktor)
        ["Konto.Vorschlag"] = F((w, a) => w.D.Konten.IdentifiantVorschlagAsync(a, "Ama", "Mensah")),
        ["Konto.Sperren.Lehrer"] = F((w, a) => w.D.Konten.SperrenAsync(a, w.LehrerZiel), fremd: true),
        ["Konto.Sperren.Koordinator"] = F((w, a) => w.D.Konten.SperrenAsync(a, w.KoordZiel), "D", true),
        ["Konto.Entsperren"] = F((w, a) => w.D.Konten.EntsperrenAsync(a, w.LehrerZiel), fremd: true),
        ["Konto.Archivieren"] = F((w, a) => w.D.Konten.ArchivierenAsync(a, w.ElternZiel), fremd: true),
        ["Konto.Reintegrieren"] = F((w, a) => w.D.Konten.ReintegrierenAsync(a, w.ElternZiel), fremd: true),
        ["Konto.Passwort"] = F((w, a) => w.D.Konten.PasswortZuruecksetzenAsync(a, [w.LehrerZiel]), fremd: true),
        ["Konto.Passwort.Koordinator"] = F((w, a) => w.D.Konten.PasswortZuruecksetzenAsync(a, [w.KoordZiel]), "D", true),

        // PersonalService
        ["Personal.Suche"] = F((w, a) => w.D.Personal.SucheAsync(a, w.Jahr, null, "prof", 0, 50)),
        ["Personal.Zaehler"] = F((w, a) => w.D.Personal.ZaehlerAsync(a, w.Jahr)),
        ["Personal.Detail"] = F((w, a) => w.D.Personal.DetailAsync(a, w.LehrerZiel), fremd: true),
        ["Personal.Anlegen.Lehrer"] = F((w, a) => w.D.Personal.AnlegenAsync(a, new PersonalEingabe(Rollen.Lehrer, "Kofi", "Annan", null, null, null, Geschlecht.Maennlich))),
        ["Personal.Anlegen.Koordinator"] = F((w, a) => w.D.Personal.AnlegenAsync(a, new PersonalEingabe(Rollen.Koordinator, "Afi", "Lawson", null, null, null, Geschlecht.Weiblich)), "D"),
        ["Personal.Aendern"] = F((w, a) => w.D.Personal.AendernAsync(a, w.LehrerZiel, new PersonalEingabe(Rollen.Lehrer, "Test", "Ziel", null, null, null, Geschlecht.Divers)), fremd: true),
        ["Personal.Aendern.Koordinator"] = F((w, a) => w.D.Personal.AendernAsync(a, w.KoordZiel, new PersonalEingabe(Rollen.Koordinator, "Test", "Coord", null, null, null, Geschlecht.Divers)), "D", true),

        // SchuelerService
        ["Schueler.Suche"] = F((w, a) => w.D.Schueler.SucheAsync(a, null, null, "eleve", 0, 50)),
        ["Schueler.Zaehler"] = F((w, a) => w.D.Schueler.ZaehlerAsync(a)),
        ["Schueler.Auswahl"] = F((w, a) => w.D.Schueler.AuswahlAsync(a, "eleve")),
        ["Schueler.Detail"] = F((w, a) => w.D.Schueler.DetailAsync(a, w.Schueler), fremd: true),
        ["Schueler.Anlegen"] = F((w, a) => w.D.Schueler.AnlegenAsync(a, OrgWelt.SchuelerDaten("Afi", "Koffi"), w.Klasse6A), fremd: true),
        ["Schueler.Aendern"] = F((w, a) => w.D.Schueler.AendernAsync(a, w.SchuelerZiel, OrgWelt.SchuelerDaten("Test", "Ziel")), fremd: true),
        ["Schueler.Validieren"] = F((w, a) => w.D.Schueler.ValidierenAsync(a, w.SchuelerWartend, w.Klasse6A, false), fremd: true),
        ["Schueler.Ablehnen"] = F((w, a) => w.D.Schueler.AblehnenAsync(a, w.SchuelerWartend, "Doublon"), fremd: true),
        ["Schueler.Foto"] = F((w, a) => w.D.Schueler.FotoHochladenAsync(a, w.SchuelerZiel, Png(), "photo.png"), fremd: true),
        ["Schueler.FotoEntfernen"] = F((w, a) => w.D.Schueler.FotoEntfernenAsync(a, w.SchuelerZiel), fremd: true),
        ["Schueler.KartenCode"] = F((w, a) => w.D.Schueler.KartenCodeErneuernAsync(a, w.SchuelerZiel), fremd: true),

        // EinschreibungService
        ["Einschreiben"] = F((w, a) => w.D.Einschreibung.EinschreibenAsync(a, w.SchuelerOhne, w.Klasse6B, new(2026, 11, 2), false), fremd: true),
        ["Einschreiben.Mehrere"] = F((w, a) => w.D.Einschreibung.MehrereEinschreibenAsync(a, w.Klasse6B, [w.SchuelerOhne], new(2026, 11, 2), false), fremd: true),
        ["Klasse.Wechseln"] = F((w, a) => w.D.Einschreibung.KlasseWechselnAsync(a, w.SchuelerZiel, w.Klasse6B, new(2026, 11, 2)), fremd: true),
        ["Austritt"] = F((w, a) => w.D.Einschreibung.AustrittAsync(a, w.SchuelerZiel, new(2026, 11, 10), "Déménagement", false), fremd: true),
        ["Stornieren"] = F((w, a) => w.D.Einschreibung.StornierenAsync(a, w.EinschreibungZiel, "Erreur de saisie"), fremd: true),
        ["Wiederholer"] = F((w, a) => w.D.Einschreibung.WiederholerSetzenAsync(a, w.EinschreibungZiel, true), fremd: true),

        // ElternService
        ["Eltern.Suche"] = F((w, a) => w.D.Eltern.SucheAsync(a, null, "parent", 0, 50)),
        ["Eltern.Zaehler"] = F((w, a) => w.D.Eltern.ZaehlerAsync(a)),
        ["Eltern.Detail"] = F((w, a) => w.D.Eltern.DetailAsync(a, w.Eltern), fremd: true),
        ["Eltern.Auswahl"] = F((w, a) => w.D.Eltern.AuswahlAsync(a, null)),
        ["Eltern.Vorbelegung"] = F((w, a) => w.D.Eltern.VorbelegungAsync(a, w.Schueler), fremd: true),
        ["Eltern.Anlegen"] = F((w, a) => w.D.Eltern.AnlegenAsync(a, new ElternEingabe("Kodzo", "Adjo", null, null, null, Geschlecht.Maennlich), w.Schueler, "Père"), fremd: true),
        ["Eltern.Aendern"] = F((w, a) => w.D.Eltern.AendernAsync(a, w.ElternZiel, new ElternEingabe("Test", "Ziel", null, null, null, Geschlecht.Divers)), fremd: true),
        ["Eltern.Verknuepfen"] = F((w, a) => w.D.Eltern.VerknuepfenAsync(a, w.ElternZiel, w.Schueler, "Tuteur"), fremd: true),
        ["Eltern.Trennen"] = F((w, a) => w.D.Eltern.TrennenAsync(a, w.Eltern, w.Schueler), fremd: true),

        // StundenplanService
        ["Plaene"] = F((w, a) => w.D.Stundenplan.PlaeneAsync(a, w.Klasse6A), fremd: true),
        ["Plan.Anlegen"] = F((w, a) => w.D.Stundenplan.PlanAnlegenAsync(a, w.Klasse5A, null, null, null), fremd: true),
        ["Plan.Zeitraum"] = F((w, a) => w.D.Stundenplan.PlanZeitraumAsync(a, w.PlanEntwurf, new(2026, 9, 1), null), fremd: true),
        ["Plan.Loeschen"] = F((w, a) => w.D.Stundenplan.PlanLoeschenAsync(a, w.PlanEntwurf), fremd: true),
        ["Plan.Detail"] = F((w, a) => w.D.Stundenplan.PlanAsync(a, w.PlanPub), fremd: true),
        ["Stunde.Speichern"] = F((w, a) => w.D.Stundenplan.StundeSpeichernAsync(a, w.PlanEntwurf, null,
            new StundeEingabe(w.Auftrag6B, DayOfWeek.Tuesday, new(8, 0), new(9, 0), "Salle 2")), fremd: true),
        ["Stunde.Loeschen"] = F((w, a) => w.D.Stundenplan.StundeLoeschenAsync(a, w.Stunde), fremd: true),
        ["Plan.Veroeffentlichen"] = F((w, a) => w.D.Stundenplan.VeroeffentlichenAsync(a, w.PlanEntwurf), fremd: true),
        ["Plan.Zurueckziehen"] = F((w, a) => w.D.Stundenplan.ZurueckziehenAsync(a, w.PlanPub), fremd: true),
        ["Plan.Kopieren"] = F((w, a) => w.D.Stundenplan.KopierenAsync(a, w.PlanEntwurf, w.PlanPub), fremd: true),
        ["Woche.Lehrer"] = F((w, a) => w.D.Stundenplan.LehrerWocheAsync(a, OrgWelt.Heute), "DKL"),
        ["Woche.Schueler"] = F((w, a) => w.D.Stundenplan.SchuelerWocheAsync(a, w.Schueler, OrgWelt.Heute), "DKSE", true),
        ["Plan.Pdf"] = F((w, a) => w.D.Stundenplan.PlanPdfAsync(a, w.PlanPub), "DKLSE", true),
        ["Lehrer.Pdf"] = F((w, a) => w.D.Stundenplan.LehrerPdfAsync(a, OrgWelt.Heute), "DKL"),

        // AnwesenheitService (Lehrkraft nur mit Lehrauftrag in der Klasse)
        ["Appell.Klassen"] = F((w, a) => w.D.Anwesenheit.KlassenAsync(a, w.Jahr), "DKL", true),
        ["Appell"] = F((w, a) => w.D.Anwesenheit.AppellAsync(a, w.Klasse6A, OrgWelt.Heute), "DKL", true),
        ["Appell.Speichern"] = F((w, a) => w.D.Anwesenheit.AppellSpeichernAsync(a, w.Klasse6A, OrgWelt.Heute, []), "DKL", true),
        ["Appell.Uebersicht"] = F((w, a) => w.D.Anwesenheit.KlassenUebersichtAsync(a, w.Klasse6A, new(2026, 9, 1), OrgWelt.Heute), "DKL", true),
        ["Anwesenheit.Schueler"] = F((w, a) => w.D.Anwesenheit.SchuelerAsync(a, w.Schueler), "DKLSE", true),

        // CockpitService, ImportService
        ["Cockpit"] = F((w, a) => w.D.Cockpit.LadeAsync(a)),
        ["Import.Vorlage"] = F((w, a) => w.D.Import.VorlageAsync(a, ImportArt.Eleves, w.Jahr), fremd: true),
        ["Import.Vorschau"] = F((w, a) => w.D.Import.VorschauAsync(a, ImportArt.Enseignants, null, LehrerXlsx(), "equipe.xlsx")),
        ["Import.Fehlerdatei"] = F((w, a) => w.D.Import.FehlerDateiAsync(a, Guid.NewGuid())),
        ["Import.Importieren"] = F((w, a) => w.D.Import.ImportierenAsync(a, Guid.NewGuid())),
    };

    public static IEnumerable<object[]> Namen => Faelle.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Namen))]
    public async Task Rechte_je_Rolle(string name)
    {
        var fall = Faelle[name];
        var w = await OrgWelt.ErstelleAsync();

        if (fall.Fremd)
            await Assert.ThrowsAsync<KeinZugriffFehler>(() => fall.Aktion(w, w.DirektorB));

        var erlaubt = new List<char>();
        foreach (var rolle in Rollenfolge)
        {
            try
            {
                await fall.Aktion(w, w.Akteur(rolle));
                erlaubt.Add(rolle);
            }
            catch (KeinZugriffFehler)
            {
                // verweigert
            }
            catch (FachFehler)
            {
                // Rechteprüfung bestanden, fachliche Regel greift danach
                erlaubt.Add(rolle);
            }
        }
        Assert.Equal(fall.Erlaubt, new string([.. erlaubt]));
    }

    [Fact]
    public async Task Niemand_verwaltet_das_Konto_des_Direktors()
    {
        var w = await OrgWelt.ErstelleAsync();
        var fehler = await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Konten.SperrenAsync(w.Koordinator, w.Direktor));
        Assert.Equal(KontoVerwaltungService.MeldungDirektor, fehler.Message);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Konten.PasswortZuruecksetzenAsync(w.Koordinator, [w.Direktor]));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Konten.ArchivierenAsync(w.Koordinator, w.Direktor));
        // Der Direktor selbst: eigenes Konto über « Mon profil ».
        Assert.Equal(KontoVerwaltungService.MeldungSelbst,
            (await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.SperrenAsync(w.Direktor, w.Direktor))).Message);
        Assert.Equal(KontoVerwaltungService.MeldungSelbst,
            (await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.PasswortZuruecksetzenAsync(w.Direktor, [w.Direktor]))).Message);
        var status = await w.MitDbAsync(db => Task.FromResult(db.Users.Single(k => k.Id == w.Direktor).Status));
        Assert.Equal(KontoStatus.Actif, status);
    }

    [Fact]
    public async Task Koordinator_setzt_keine_Rechte_und_legt_keine_Koordinatoren_an()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Schule.KoordinatorRechteSetzenAsync(w.Koordinator, w.KoordZiel, KoordinatorRechte.Alle));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Schule.KoordinatorRechteSetzenAsync(w.Koordinator, w.Koordinator, KoordinatorRechte.Alle));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Personal.AnlegenAsync(w.Koordinator,
            new PersonalEingabe(Rollen.Koordinator, "Afi", "Lawson", null, null, null, Geschlecht.Weiblich)));
        // Direktor darf; Rolle « Direktor » oder « Élève » ist über « Équipe » nie anlegbar.
        await w.D.Schule.KoordinatorRechteSetzenAsync(w.Direktor, w.KoordZiel, KoordinatorRechte.FinanzenEinsehen | KoordinatorRechte.ZahlungenErfassen);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Personal.AnlegenAsync(w.Direktor,
            new PersonalEingabe(Rollen.Direktor, "Afi", "Lawson", null, null, null, Geschlecht.Weiblich)));
        var rechte = await w.MitDbAsync(db => Task.FromResult(db.Users.Single(k => k.Id == w.KoordZiel).KoordinatorRechte));
        Assert.Equal(KoordinatorRechte.FinanzenEinsehen | KoordinatorRechte.ZahlungenErfassen, rechte);
        Assert.Equal(1, await w.MitDbAsync(db => Task.FromResult(db.AuditEintraege.Count(x => x.Aktion == Skoulou.WebApp.Services.Infrastruktur.AuditAktionen.RechteGeaendert))));
    }

    [Fact]
    public async Task Lehrer_ohne_Lehrauftrag_macht_keinen_Appell_und_sieht_keine_fremden_Plaene()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Anwesenheit.AppellAsync(w.Lehrer2, w.Klasse6A, OrgWelt.Heute));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Anwesenheit.AppellAsync(w.Lehrer, w.Klasse6B, OrgWelt.Heute));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Anwesenheit.SchuelerAsync(w.Lehrer2, w.Schueler));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Stundenplan.PlanPdfAsync(w.Lehrer2, w.PlanPub));
        Assert.Empty(await w.D.Anwesenheit.KlassenAsync(w.Lehrer2, w.Jahr));
        Assert.Equal(new[] { "6e A" }, (await w.D.Anwesenheit.KlassenAsync(w.Lehrer, w.Jahr)).Select(k => k.Name));
        // Eltern sehen nur eigene Kinder, Schüler nur sich selbst.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Stundenplan.SchuelerWocheAsync(w.Eltern, w.SchuelerZiel, OrgWelt.Heute));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Anwesenheit.SchuelerAsync(w.Eltern, w.SchuelerZiel));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Anwesenheit.SchuelerAsync(w.Schueler, w.SchuelerZiel));
        // Entwürfe sind nur für die Direktion.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Stundenplan.PlanPdfAsync(w.LehrerZiel, w.PlanEntwurf));
    }

    [Fact]
    public async Task Gesperrtes_Direktionskonto_hat_keinen_Zugriff()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Konten.SperrenAsync(w.Direktor, w.LehrerZiel);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Anwesenheit.KlassenAsync(w.LehrerZiel, w.Jahr));
        await w.MitDbAsync(async db =>
        {
            db.Users.Single(k => k.Id == w.Koordinator).Status = KontoStatus.Bloque;
            return await db.SaveChangesAsync();
        });
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Klassen.KlassenAsync(w.Koordinator, w.Jahr));
    }
}
