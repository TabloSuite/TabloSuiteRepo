using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;

namespace Skoulou.Tests.Paedagogik;

/// <summary>Die eine Bewertungs-Engine: jeder Fragetyp, Teilpunkte, Normalisierung, manuelle Korrektur.</summary>
public class BewertungsEngineTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    private static FrageDefinition Auswahl(FragenTyp typ, decimal punkte, params Guid[] richtige)
        => new(typ, punkte, new[] { A, B, C, D }.Select(id => new OptionDefinition(id, richtige.Contains(id))).ToList(), [], []);

    private static FrageDefinition Text(FragenTyp typ, decimal punkte, bool akzente = false, params string[] varianten)
        => new(typ, punkte, [], varianten, [], akzente);

    private static FrageDefinition Luecken(decimal punkte, params string[][] luecken)
        => new(FragenTyp.TexteATrous, punkte, [], [], luecken.Select(l => (IReadOnlyList<string>)l).ToList());

    private static AntwortEingabe Wahl(params Guid[] ids) => new() { Optionen = ids };
    private static AntwortEingabe Satz(string? text) => new() { Text = text };

    [Theory]
    [InlineData(FragenTyp.ChoixUnique)]
    [InlineData(FragenTyp.AudioChoixUnique)]
    public void ChoixUnique_volle_Punkte_oder_null(FragenTyp typ)
    {
        var f = Auswahl(typ, 2, B);
        Assert.Equal((BewertungsStatus.Richtig, 2m), Kurz(BewertungsEngine.Bewerte(f, Wahl(B))));
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A))));
        Assert.Equal((BewertungsStatus.Leer, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl())));
        Assert.Equal((BewertungsStatus.Leer, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl(Guid.NewGuid()))));
        // Zwei Optionen bei Choix unique gelten als falsch (kein Raten über Mehrfachwahl).
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A, B))));
        Assert.Equal((BewertungsStatus.Leer, 0m), Kurz(BewertungsEngine.Bewerte(f, null)));
    }

    [Theory]
    [InlineData(FragenTyp.ChoixMultiple)]
    [InlineData(FragenTyp.AudioChoixMultiple)]
    public void ChoixMultiple_Teilpunkte_richtige_minus_falsche(FragenTyp typ)
    {
        var f = Auswahl(typ, 4, A, B);
        Assert.Equal((BewertungsStatus.Richtig, 4m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A, B))));
        Assert.Equal((BewertungsStatus.Teilweise, 2m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A))));
        // 2 richtig − 1 falsch = 1 von 2.
        Assert.Equal((BewertungsStatus.Teilweise, 2m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A, B, C))));
        // 1 richtig − 1 falsch = 0, nie negativ.
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A, C))));
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl(C, D))));
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A, B, C, D))));
        Assert.Equal((BewertungsStatus.Leer, 0m), Kurz(BewertungsEngine.Bewerte(f, Wahl())));
        // Doppelte und unbekannte IDs zählen nicht.
        Assert.Equal((BewertungsStatus.Teilweise, 2m), Kurz(BewertungsEngine.Bewerte(f, Wahl(A, A, Guid.NewGuid()))));
    }

    [Fact]
    public void ChoixMultiple_rundet_auf_zwei_Stellen()
    {
        var f = Auswahl(FragenTyp.ChoixMultiple, 1, A, B, C);
        Assert.Equal(0.33m, BewertungsEngine.Bewerte(f, Wahl(A)).Punkte);
        Assert.Equal(0.67m, BewertungsEngine.Bewerte(f, Wahl(A, B)).Punkte);
    }

    [Theory]
    [InlineData(FragenTyp.Texte)]
    [InlineData(FragenTyp.ConstructionPhrase)]
    [InlineData(FragenTyp.AudioTexte)]
    public void Text_mit_Varianten_tolerant(FragenTyp typ)
    {
        var f = Text(typ, 3, false, "Le chat dort sur le tapis", "Le chat dort");
        foreach (var antwort in new[] { "le chat dort sur le tapis", "  Le   chat dort sur le tapis. ", "LE CHAT DORT", "<p>Le chat dort sur le <b>tapis</b></p>" })
            Assert.Equal((BewertungsStatus.Richtig, 3m), Kurz(BewertungsEngine.Bewerte(f, Satz(antwort))));
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(f, Satz("Le chien dort"))));
        Assert.Equal((BewertungsStatus.Leer, 0m), Kurz(BewertungsEngine.Bewerte(f, Satz("   "))));
    }

    [Fact]
    public void Text_Akzente_optional_typografische_Zeichen_vereinheitlicht()
    {
        var ohne = Text(FragenTyp.Texte, 1, false, "l’élève écoute");
        Assert.Equal(BewertungsStatus.Richtig, BewertungsEngine.Bewerte(ohne, Satz("l'eleve ecoute")).Status);
        Assert.Equal(BewertungsStatus.Richtig, BewertungsEngine.Bewerte(ohne, Satz("L’Élève écoute !")).Status);
        var mit = Text(FragenTyp.Texte, 1, true, "l’élève écoute");
        Assert.Equal(BewertungsStatus.Falsch, BewertungsEngine.Bewerte(mit, Satz("l'eleve ecoute")).Status);
        Assert.Equal(BewertungsStatus.Richtig, BewertungsEngine.Bewerte(mit, Satz("L'ÉLÈVE écoute")).Status);
        Assert.Equal(BewertungsStatus.Richtig, BewertungsEngine.Bewerte(Text(FragenTyp.Texte, 1, false, "« cœur »"), Satz("coeur")).Status);
    }

    [Fact]
    public void Text_Zahlen_und_Rechenzeichen_bleiben_bedeutsam()
    {
        var f = Text(FragenTyp.Texte, 1, false, "3,5", "x = 2");
        Assert.Equal(BewertungsStatus.Richtig, BewertungsEngine.Bewerte(f, Satz("3.5")).Status);
        Assert.Equal(BewertungsStatus.Falsch, BewertungsEngine.Bewerte(f, Satz("35")).Status);
        Assert.Equal(BewertungsStatus.Richtig, BewertungsEngine.Bewerte(f, Satz("x=2")).Status);
        Assert.Equal(BewertungsStatus.Falsch, BewertungsEngine.Bewerte(f, Satz("x=3")).Status);
        Assert.Equal(BewertungsStatus.Richtig, BewertungsEngine.Bewerte(Text(FragenTyp.Texte, 1, false, "3/4"), Satz(" 3 / 4 ")).Status);
    }

    [Fact]
    public void Text_ohne_Musterloesung_wird_manuell_korrigiert()
    {
        var f = Text(FragenTyp.Texte, 5, false, "", "   ");
        var e = BewertungsEngine.Bewerte(f, Satz("Une fraction est un partage."));
        Assert.Equal(BewertungsStatus.Manuell, e.Status);
        Assert.Null(e.Punkte);
        Assert.True(e.BrauchtKorrektur);
        Assert.Equal(5m, e.MaxPunkte);
        // Leere Antwort braucht keine Korrektur.
        Assert.Equal(BewertungsStatus.Leer, BewertungsEngine.Bewerte(f, Satz("")).Status);
    }

    [Fact]
    public void Luecken_Teilpunkte_je_Luecke_mit_Varianten()
    {
        var f = Luecken(3, ["chantons"], ["5", "cinq"], ["été"]);
        var alle = BewertungsEngine.Bewerte(f, new AntwortEingabe { Luecken = ["Chantons", "cinq", "ete"] });
        Assert.Equal((BewertungsStatus.Richtig, 3m), Kurz(alle));
        Assert.Equal(3, alle.RichtigeLuecken);
        var zwei = BewertungsEngine.Bewerte(f, new AntwortEingabe { Luecken = ["chantons", "six", "été"] });
        Assert.Equal((BewertungsStatus.Teilweise, 2m), Kurz(zwei));
        Assert.Equal(2, zwei.RichtigeLuecken);
        Assert.Equal(3, zwei.AnzahlLuecken);
        // Fehlende Einträge zählen als leer.
        Assert.Equal((BewertungsStatus.Teilweise, 1m), Kurz(BewertungsEngine.Bewerte(f, new AntwortEingabe { Luecken = ["chantons"] })));
        Assert.Equal((BewertungsStatus.Leer, 0m), Kurz(BewertungsEngine.Bewerte(f, new AntwortEingabe { Luecken = ["", null] })));
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(f, new AntwortEingabe { Luecken = ["x", "y", "z"] })));
        Assert.Equal((BewertungsStatus.Falsch, 0m), Kurz(BewertungsEngine.Bewerte(Luecken(2), new AntwortEingabe { Luecken = ["x"] })));
    }

    [Fact]
    public void Antwort_JSON_hin_und_zurueck_und_unlesbar_ist_leer()
    {
        var e = new AntwortEingabe { Optionen = [A], Text = "x", Luecken = ["a", null] };
        var zurueck = AntwortEingabe.AusJson(e.AlsJson());
        Assert.Equal(e.Optionen, zurueck.Optionen);
        Assert.Equal("x", zurueck.Text);
        Assert.Equal(e.Luecken, zurueck.Luecken);
        Assert.True(AntwortEingabe.AusJson("{kaputt").IstLeer);
        Assert.True(AntwortEingabe.AusJson(null).IstLeer);
    }

    [Fact]
    public void Normalisierung_einzeln()
    {
        Assert.Equal("l'ecole", AntwortVergleich.Normalisiere("  L’École. "));
        Assert.Equal("l'école", AntwortVergleich.Normalisiere("L’École", akzenteBeachten: true));
        Assert.Equal(string.Empty, AntwortVergleich.Normalisiere("?!."));
        Assert.Equal("a b", AntwortVergleich.Normalisiere("<p>a</p><p>b</p>"));
        Assert.False(AntwortVergleich.Passt("", [""]));
    }

    [Fact]
    public void Automatische_Bewertung_je_Typ()
    {
        Assert.True(FragenTypen.HatAutomatischeBewertung(FragenTyp.ChoixUnique, []));
        Assert.True(FragenTypen.HatAutomatischeBewertung(FragenTyp.TexteATrous, []));
        Assert.False(FragenTypen.HatAutomatischeBewertung(FragenTyp.Texte, ["  "]));
        Assert.True(FragenTypen.HatAutomatischeBewertung(FragenTyp.AudioTexte, ["oui"]));
        Assert.Equal(8, FragenTypen.Alle.Count);
        Assert.All(FragenTypen.Alle, t => Assert.False(string.IsNullOrEmpty(FragenTypen.Bezeichnung(t))));
    }

    private static (BewertungsStatus, decimal?) Kurz(BewertungsErgebnis e) => (e.Status, e.Punkte);
}
