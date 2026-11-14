using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>Reine Rechner (Bauplan §5 Regeln 1–6): Fachmittel, Moyenne, Jahresmittel, Rundung, Rang, Schwellen, Statistik, Eingabe.</summary>
public class NotenRechnerTests
{
    private static Mittel Fach(bool absentNull, params Wertung[] w) => NotenRechner.Fachmittel(w, absentNull);

    [Fact]
    public void Fachmittel_normalisiert_Bareme_und_gewichtet()
    {
        // 5/10 (Gewicht 1) + 35/40 (Gewicht 2) = (10 + 2·17,5) / 3 = 15
        var m = Fach(false, Wertung.Note(5, 10), Wertung.Note(35, 40, 2));
        Assert.Equal(MittelStatus.Wert, m.Status);
        Assert.Equal(15m, m.Wert);
    }

    [Fact]
    public void Fehlende_Note_macht_unvollstaendig_echte_Null_zaehlt()
    {
        Assert.Equal(MittelStatus.Incomplet, Fach(false, Wertung.Note(12), Wertung.Fehlt()).Status);
        Assert.Equal(6m, Fach(false, Wertung.Note(12), Wertung.Note(0)).Wert);
        Assert.Equal(MittelStatus.Incomplet, Fach(false, Wertung.Note(12), Wertung.Mit(NotenStatus.Leer)).Status);
    }

    [Fact]
    public void Nicht_erwartete_Bewertung_fehlt_nicht()
    {
        var m = Fach(false, Wertung.Note(14), new Wertung(20, 1, null, null, Erwartet: false));
        Assert.Equal(14m, m.Wert);
        Assert.Equal(MittelStatus.NonEvalue, Fach(false, new Wertung(20, 1, null, null, false)).Status);
    }

    [Fact]
    public void Ohne_Bewertung_nicht_bewertet()
        => Assert.Equal(MittelStatus.NonEvalue, Fach(false).Status);

    [Fact]
    public void Absent_justifie_und_dispense_zaehlen_nicht()
    {
        Assert.Equal(16m, Fach(true, Wertung.Note(16), Wertung.Mit(NotenStatus.AbsentJustifie), Wertung.Mit(NotenStatus.Dispense)).Wert);
        Assert.Equal(MittelStatus.Dispense, Fach(true, Wertung.Mit(NotenStatus.Dispense), Wertung.Mit(NotenStatus.AbsentJustifie)).Status);
    }

    [Fact]
    public void Absent_zaehlt_null_nur_wenn_die_Schule_es_will()
    {
        Assert.Equal(8m, Fach(true, Wertung.Note(16), Wertung.Mit(NotenStatus.Absent)).Wert);
        Assert.Equal(16m, Fach(false, Wertung.Note(16), Wertung.Mit(NotenStatus.Absent)).Wert);
        Assert.Equal(0m, Fach(true, Wertung.Mit(NotenStatus.Absent)).Wert);
    }

    [Fact]
    public void Ungueltige_Werte_werfen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fach(false, Wertung.Note(21)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fach(false, Wertung.Note(5, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fach(false, Wertung.Note(5, 20, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotenRechner.MoyenneGenerale([(Mittel.Von(10), 0m)]));
    }

    [Fact]
    public void Moyenne_generale_gewichtet_und_unvollstaendig()
    {
        var m = NotenRechner.MoyenneGenerale([(Mittel.Von(12), 3m), (Mittel.Von(15), 2m), (Mittel.NonEvalue, 4m), (Mittel.Dispense, 1m)]);
        Assert.Equal(13.2m, m.Wert);
        Assert.Equal(MittelStatus.Incomplet, NotenRechner.MoyenneGenerale([(Mittel.Von(12), 3m), (Mittel.Incomplet, 2m)]).Status);
        Assert.Equal(MittelStatus.NonEvalue, NotenRechner.MoyenneGenerale([(Mittel.NonEvalue, 3m)]).Status);
    }

    [Fact]
    public void Jahresmittel_ignoriert_Examen_blanc_und_verlangt_alle_Perioden()
    {
        var m = NotenRechner.Jahresmittel([(Mittel.Von(10), 1m, true), (Mittel.Von(12), 1m, true), (Mittel.Von(14), 2m, true), (Mittel.Von(0), 100m, false)]);
        Assert.Equal(12.5m, m.Wert);
        Assert.Equal(MittelStatus.Incomplet, NotenRechner.Jahresmittel([(Mittel.Von(10), 1m, true), (Mittel.Incomplet, 1m, true)]).Status);
        Assert.Equal(MittelStatus.Incomplet, NotenRechner.Jahresmittel([(Mittel.Von(10), 1m, true), (Mittel.NonEvalue, 1m, true)]).Status);
        Assert.Equal(MittelStatus.NonEvalue, NotenRechner.Jahresmittel([(Mittel.Von(10), 1m, false)]).Status);
    }

    [Fact]
    public void Fach_Jahresmittel_ueberspringt_nicht_bewertete_Perioden()
    {
        Assert.Equal(13m, NotenRechner.FachJahresmittel([(Mittel.Von(12), 1m, true), (Mittel.NonEvalue, 1m, true), (Mittel.Von(14), 1m, true)]).Wert);
        Assert.Equal(MittelStatus.Incomplet, NotenRechner.FachJahresmittel([(Mittel.Von(12), 1m, true), (Mittel.Incomplet, 1m, true)]).Status);
    }

    [Theory]
    [InlineData("11.995", "12.00")]
    [InlineData("11.994", "11.99")]
    [InlineData("12.345", "12.35")]
    [InlineData("9.995", "10.00")]
    public void Rundung_kaufmaennisch_auf_zwei_Stellen(string wert, string erwartet)
        => Assert.Equal(decimal.Parse(erwartet, System.Globalization.CultureInfo.InvariantCulture),
            NotenRechner.Runde(decimal.Parse(wert, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Rang_mit_ex_aequo_und_Non_classe()
    {
        var r = RangRechner.Berechne<string>([("a", 15m), ("b", 12m), ("c", 15.001m), ("d", null), ("e", 11m)]);
        Assert.Equal(new RangInfo(1, true), r["a"]);
        Assert.Equal(new RangInfo(1, true), r["c"]); // 15,001 → 15,00 gerundet = Gleichstand
        Assert.Equal(new RangInfo(3, false), r["b"]);
        Assert.Equal(new RangInfo(4, false), r["e"]);
        Assert.False(r["d"].Klassiert);
        Assert.Equal("1er ex", r["a"].Text);
        Assert.Equal("3e", r["b"].Text);
        Assert.Equal("Non classé", r["d"].Text);
    }

    [Fact]
    public void Statistik_ueber_klassierte_Schueler()
    {
        var s = KlassenStatistik.Berechne([Mittel.Von(8), Mittel.Von(12), Mittel.Von(9.996m), Mittel.Incomplet]);
        Assert.Equal(4, s.Effectif);
        Assert.Equal(3, s.Klassiert);
        Assert.Equal(8m, s.Min);
        Assert.Equal(12m, s.Max);
        Assert.Equal(10m, s.Mittel);
        Assert.Equal(66.67m, s.ProzentUeber10); // 9,996 → 10,00 zählt als ≥ 10
    }

    [Theory]
    [InlineData("18", "Excellent")]
    [InlineData("16", "Très bien")]
    [InlineData("15.999", "Très bien")]
    [InlineData("14", "Bien")]
    [InlineData("12", "Assez bien")]
    [InlineData("10", "Passable")]
    [InlineData("8", "Insuffisant")]
    [InlineData("5", "Faible")]
    [InlineData("4.99", "Très faible")]
    public void Appreciation_Schwellen_auf_gerundetem_Wert(string wert, string text)
        => Assert.Equal(text, NotenTexte.Appreciation(decimal.Parse(wert, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("16.5", Distinktion.Felicitations)]
    [InlineData("14", Distinktion.Encouragements)]
    [InlineData("12.4", Distinktion.TableauHonneur)]
    [InlineData("11.996", Distinktion.TableauHonneur)]
    [InlineData("11.99", Distinktion.Keine)]
    public void Distinktion_Vorschlag(string wert, Distinktion erwartet)
        => Assert.Equal(erwartet, NotenTexte.DistinktionVorschlag(decimal.Parse(wert, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Texte_fuer_Sanktionen_Entscheidung_und_Titel()
    {
        Assert.Equal(["Avertissement travail", "Blâme conduite"], NotenTexte.SanktionenTexte(Sanktionen.AvertissementTravail | Sanktionen.BlameConduite));
        Assert.Equal("Admis(e) en classe supérieure", NotenTexte.Entscheidung(JahresEntscheidungsArt.Admis));
        Assert.Equal("BULLETIN TRIMESTRIEL DE NOTES", NotenTexte.BulletinTitel(PeriodenArt.Trimestre, true));
        Assert.Equal("BULLETIN D'EXAMEN BLANC", NotenTexte.BulletinTitel(PeriodenArt.Trimestre, false));
        Assert.Equal("Notes incomplètes", NotenTexte.Mittel(Mittel.Incomplet));
        Assert.Equal("12,50", NotenTexte.Mittel(Mittel.Von(12.5m)));
    }

    [Theory]
    [InlineData("11.02", "onze virgule zéro deux")]
    [InlineData("14.5", "quatorze virgule cinquante")]
    [InlineData("17.75", "dix-sept virgule soixante-quinze")]
    [InlineData("8", "huit")]
    [InlineData("20", "vingt")]
    [InlineData("0.71", "zéro virgule soixante et onze")]
    [InlineData("12.81", "douze virgule quatre-vingt-un")]
    [InlineData("9.8", "neuf virgule quatre-vingts")]
    public void Moyenne_in_Worten(string wert, string text)
        => Assert.Equal(text, NoteInWorten.Text(decimal.Parse(wert, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Zelleingabe_Zahlen_und_Kuerzel()
    {
        Assert.Equal(new ZellEingabe(false, NotenStatus.Notee, 12.5m), NotenEingabe.Lese("12,5", 20));
        Assert.Equal(new ZellEingabe(false, NotenStatus.Notee, 12.25m), NotenEingabe.Lese(" 12.25 ", 20));
        Assert.Equal(new ZellEingabe(false, NotenStatus.Notee, 7m), NotenEingabe.Lese("7/10", 10));
        Assert.Equal(NotenStatus.Absent, NotenEingabe.Lese("ABS", 20).Status);
        Assert.Equal(NotenStatus.AbsentJustifie, NotenEingabe.Lese("aj", 20).Status);
        Assert.Equal(NotenStatus.Dispense, NotenEingabe.Lese("disp", 20).Status);
        Assert.True(NotenEingabe.Lese("  ", 20).Leer);
        Assert.Throws<FachFehler>(() => NotenEingabe.Lese("21", 20));
        Assert.Throws<FachFehler>(() => NotenEingabe.Lese("12,555", 20));
        Assert.Throws<FachFehler>(() => NotenEingabe.Lese("-1", 20));
        Assert.Throws<FachFehler>(() => NotenEingabe.Lese("douze", 20));
    }

    [Fact]
    public void Klassenrechner_rechnet_Faecher_Moyenne_Raenge_und_Bilan()
    {
        var math = Guid.NewGuid();
        var fr = Guid.NewGuid();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var faecher = new List<FachSpalte> { new(math, 3), new(fr, 2) };
        SchuelerWertungen S(Guid id, Wertung m, Wertung f) => new(id, new Dictionary<Guid, IReadOnlyList<Wertung>> { [math] = [m], [fr] = [f] });
        var p1 = KlassenRechner.Berechne(faecher, [S(a, Wertung.Note(15), Wertung.Note(10)), S(b, Wertung.Note(13), Wertung.Note(13)), S(c, Wertung.Note(12), Wertung.Fehlt())], false);
        Assert.Equal(13m, p1.Fuer(a)!.Moyenne.Wert);
        Assert.Equal(13m, p1.Fuer(b)!.Moyenne.Wert);
        Assert.Equal(new RangInfo(1, true), p1.Fuer(a)!.Rang);
        Assert.False(p1.Fuer(c)!.Rang.Klassiert);
        Assert.Equal(new RangInfo(1, false), p1.Fuer(a)!.Faecher[math].Rang);
        Assert.Equal(new RangInfo(3, false), p1.Fuer(c)!.Faecher[math].Rang);
        Assert.Equal(2, p1.Statistik.Klassiert);

        var p2 = KlassenRechner.Berechne(faecher, [S(a, Wertung.Note(10), Wertung.Note(10)), S(b, Wertung.Note(16), Wertung.Note(16)), S(c, Wertung.Note(12), Wertung.Note(12))], false);
        var bilan = KlassenRechner.Bilan([a, b, c], [math, fr], [(p1, 1m, true), (p2, 1m, true)]);
        Assert.Equal(11.5m, bilan[a].Moyenne.Wert);
        Assert.Equal(14.5m, bilan[b].Moyenne.Wert);
        Assert.Equal(new RangInfo(1, false), bilan[b].Rang);
        Assert.False(bilan[c].Rang.Klassiert);
        Assert.Equal(12.5m, bilan[a].JeFach[math].Wert);
    }

    [Fact]
    public void Letzte_Periode_ist_die_letzte_zaehlende()
    {
        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid(), blanc = Guid.NewGuid();
        var liste = new[] { (p1, new DateOnly(2026, 12, 18), true), (p2, new DateOnly(2027, 6, 30), true), (blanc, new DateOnly(2027, 7, 5), false) };
        Assert.True(NotenRechner.IstLetztePeriode(liste, p2));
        Assert.False(NotenRechner.IstLetztePeriode(liste, p1));
        Assert.False(NotenRechner.IstLetztePeriode(liste, blanc));
    }
}
