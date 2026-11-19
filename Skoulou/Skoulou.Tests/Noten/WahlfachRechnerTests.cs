using Skoulou.Core.Entitaeten;
using Skoulou.Core.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>
/// « Matières facultatives » (Bauplan §5 Regel 2a): Bonus nur über 10 ohne Koeffizient in der Summe, « comme une matière normale »,
/// « information seulement », nicht gewählt, unvollständig, Deckel 20, Beitrag zur Zeile « TOTAUX », Klassenrechner und Bilan.
/// Pflichtteil der Beispiele: Mathématiques 14 (coef 3) + Français 11 (coef 2) = 64 Punkte / 5 Koeff. = 12,80.
/// </summary>
public class WahlfachRechnerTests
{
    private static readonly FachBeitrag Mathe = new(Mittel.Von(14), 3);
    private static readonly FachBeitrag Franz = new(Mittel.Von(11), 2);

    private static Mittel Moyenne(params FachBeitrag[] wahl) => NotenRechner.MoyenneGenerale([Mathe, Franz, .. wahl]);

    [Fact]
    public void Bonus_zaehlt_nur_die_Punkte_ueber_10_ohne_Koeffizient()
    {
        Assert.Equal(12.8m, Moyenne().Wert);
        // Ewe 16 (coef 2): Bonus (16 − 10) × 2 = 12 → (64 + 12) / 5 = 15,20
        Assert.Equal(15.2m, Moyenne(new FachBeitrag(Mittel.Von(16), 2, WahlfachRegel.PunkteUeber10)).Wert);
        // Ewe 8: kein Bonus, aber auch kein Abzug
        Assert.Equal(12.8m, Moyenne(new FachBeitrag(Mittel.Von(8), 2, WahlfachRegel.PunkteUeber10)).Wert);
        Assert.Equal(12.8m, Moyenne(new FachBeitrag(Mittel.Von(10), 2, WahlfachRegel.PunkteUeber10)).Wert);
        Assert.Equal(12m, NotenRechner.Bonus(16, 2));
        Assert.Equal(0m, NotenRechner.Bonus(8, 2));
    }

    [Fact]
    public void Normale_Regel_zaehlt_wie_ein_Pflichtfach_Info_Regel_gar_nicht()
    {
        // « Compte comme une matière normale »: (64 + 32) / 7
        Assert.Equal(96m / 7, Moyenne(new FachBeitrag(Mittel.Von(16), 2, WahlfachRegel.WieNormal)).Wert);
        Assert.Equal(12.8m, Moyenne(new FachBeitrag(Mittel.Von(16), 2, WahlfachRegel.NurInformation)).Wert);
        // « Ne compte pas » macht die Moyenne auch unvollständig nie unvollständig
        Assert.Equal(12.8m, Moyenne(new FachBeitrag(Mittel.Incomplet, 2, WahlfachRegel.NurInformation)).Wert);
    }

    [Fact]
    public void Nicht_gewaehlt_zaehlt_bei_keiner_Regel()
    {
        foreach (var regel in Enum.GetValues<WahlfachRegel>())
            Assert.Equal(12.8m, Moyenne(new FachBeitrag(Mittel.NonChoisi, 2, regel)).Wert);
        Assert.Equal("—", NotenTexte.Mittel(Mittel.NonChoisi));
        Assert.Equal("—", NotenTexte.MittelKurz(Mittel.NonChoisi));
    }

    [Fact]
    public void Unvollstaendiges_Wahlfach_das_zaehlt_macht_die_Moyenne_unvollstaendig()
    {
        Assert.Equal(MittelStatus.Incomplet, Moyenne(new FachBeitrag(Mittel.Incomplet, 2, WahlfachRegel.PunkteUeber10)).Status);
        Assert.Equal(MittelStatus.Incomplet, Moyenne(new FachBeitrag(Mittel.Incomplet, 2, WahlfachRegel.WieNormal)).Status);
        // Noch nicht bewertet: zählt (noch) nicht
        Assert.Equal(12.8m, Moyenne(new FachBeitrag(Mittel.NonEvalue, 2, WahlfachRegel.PunkteUeber10)).Wert);
        // Nur ein Bonus ohne Pflichtfach mit Wert: keine Moyenne
        Assert.Equal(MittelStatus.NonEvalue, NotenRechner.MoyenneGenerale([new FachBeitrag(Mittel.NonEvalue, 3), new FachBeitrag(Mittel.Von(18), 2, WahlfachRegel.PunkteUeber10)]).Status);
    }

    [Fact]
    public void Moyenne_mit_Bonus_hoechstens_20()
    {
        var m = NotenRechner.MoyenneGenerale([new FachBeitrag(Mittel.Von(19), 1), new FachBeitrag(Mittel.Von(20), 5, WahlfachRegel.PunkteUeber10)]);
        Assert.Equal(20m, m.Wert);
        Assert.Equal(19.5m, NotenRechner.MoyenneGenerale([new FachBeitrag(Mittel.Von(19), 2), new FachBeitrag(Mittel.Von(11), 1, WahlfachRegel.PunkteUeber10)]).Wert);
    }

    [Fact]
    public void Beitrag_zur_Zeile_Totaux()
    {
        Assert.Equal((42m, true), NotenRechner.Beitrag(Mathe));
        Assert.Equal((12m, false), NotenRechner.Beitrag(new FachBeitrag(Mittel.Von(16), 2, WahlfachRegel.PunkteUeber10)));
        Assert.Equal((0m, false), NotenRechner.Beitrag(new FachBeitrag(Mittel.Von(8), 2, WahlfachRegel.PunkteUeber10)));
        Assert.Equal((32m, true), NotenRechner.Beitrag(new FachBeitrag(Mittel.Von(16), 2, WahlfachRegel.WieNormal)));
        Assert.Equal(((decimal?)null, false), NotenRechner.Beitrag(new FachBeitrag(Mittel.Von(16), 2, WahlfachRegel.NurInformation)));
        Assert.Equal(((decimal?)null, false), NotenRechner.Beitrag(new FachBeitrag(Mittel.NonChoisi, 2, WahlfachRegel.PunkteUeber10)));
        Assert.Equal(((decimal?)null, false), NotenRechner.Beitrag(new FachBeitrag(Mittel.NonEvalue, 3)));
    }

    [Fact]
    public void Klassenrechner_bewertet_Wahlfach_nur_fuer_Waehlende()
    {
        Guid math = Guid.NewGuid(), ewe = Guid.NewGuid();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var faecher = new List<FachSpalte> { new(math, 3), new(ewe, 2, WahlfachRegel.PunkteUeber10) };
        SchuelerWertungen S(Guid id, Wertung m, Wertung? e, bool gewaehlt)
        {
            var je = new Dictionary<Guid, IReadOnlyList<Wertung>> { [math] = [m] };
            if (e is { } x) je[ewe] = [x];
            return new SchuelerWertungen(id, je, gewaehlt ? new HashSet<Guid> { ewe } : null);
        }
        // a wählt Ewe (16), b wählt Ewe (12), c wählt nicht – eine liegengebliebene Note zählt dann trotzdem nicht.
        var p = KlassenRechner.Berechne(faecher, [S(a, Wertung.Note(12), Wertung.Note(16), true), S(b, Wertung.Note(14), Wertung.Note(12), true),
            S(c, Wertung.Note(13), Wertung.Note(20), false)], false);
        Assert.Equal(16m, p.Fuer(a)!.Moyenne.Wert);          // (36 + 12) / 3
        Assert.Equal(46m / 3, p.Fuer(b)!.Moyenne.Wert);      // (42 + 4) / 3
        Assert.Equal(13m, p.Fuer(c)!.Moyenne.Wert);
        Assert.Equal(MittelStatus.NonChoisi, p.Fuer(c)!.Faecher[ewe].Mittel.Status);
        Assert.False(p.Fuer(c)!.Faecher[ewe].Rang.Klassiert);
        Assert.Equal(new RangInfo(1, false), p.Fuer(a)!.Faecher[ewe].Rang);
        Assert.Equal(new RangInfo(1, false), p.Fuer(a)!.Rang);
        // Fachstatistik nur über die Wählenden
        Assert.Equal((2, 2), (p.JeFach[ewe].Effectif, p.JeFach[ewe].Klassiert));
        Assert.Equal(3, p.JeFach[math].Effectif);

        // Ohne Wahlfach-Regel (Pflichtfach) ist die Wahl bedeutungslos.
        var pflicht = KlassenRechner.Berechne([new(math, 3), new(ewe, 2)], [S(c, Wertung.Note(13), Wertung.Note(20), false)], false);
        Assert.Equal(15.8m, pflicht.Fuer(c)!.Moyenne.Wert);

        // Bilan: nie gewählt bleibt « — », gewählt ergibt ein Fach-Jahresmittel.
        var bilan = KlassenRechner.Bilan([a, c], [math, ewe], [(p, 1m, true), (p, 1m, true)]);
        Assert.Equal(MittelStatus.NonChoisi, bilan[c].JeFach[ewe].Status);
        Assert.Equal(16m, bilan[a].JeFach[ewe].Wert);
        Assert.Equal(16m, bilan[a].Moyenne.Wert);
    }
}
