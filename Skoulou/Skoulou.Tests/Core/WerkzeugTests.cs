using Skoulou.Core.Entitaeten;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.Tests.Core;

/// <summary>Identifiant, Startpasswort, Nummernformate, Kultur und Schulzeit.</summary>
public class WerkzeugTests
{
    [Theory]
    [InlineData("Ama Mensah", "ama.mensah")]
    [InlineData("Éléonore D'Almeida", "eleonore.d.almeida")]
    [InlineData("  Koffi   AGBÉKO ", "koffi.agbeko")]
    [InlineData("Jean-Marc_Ndjoli", "jean-marc_ndjoli")]
    [InlineData("Straße", "strasse")]
    [InlineData("..ab..cd--", "ab.cd")]
    [InlineData("Kossi’ Adjo!", "kossi.adjo")]
    public void Identifiant_wird_normalisiert(string eingabe, string erwartet)
        => Assert.Equal(erwartet, IdentifiantRegel.Normalisiere(eingabe));

    [Theory]
    [InlineData("ama.mensah", true)]
    [InlineData("ab", false)]
    [InlineData("Ama.mensah", false)]
    [InlineData(".ama", false)]
    [InlineData("ama.", false)]
    [InlineData("a_b-c.d9", true)]
    [InlineData("ama mensah", false)]
    public void Identifiant_Format(string identifiant, bool gueltig) => Assert.Equal(gueltig, IdentifiantRegel.IstGueltig(identifiant));

    [Fact]
    public void Identifiant_hoechstens_40_Zeichen()
    {
        var lang = IdentifiantRegel.Normalisiere(new string('a', 60));
        Assert.Equal(40, lang.Length);
        Assert.True(IdentifiantRegel.IstGueltig(lang));
    }

    [Fact]
    public void Vorschlag_zaehlt_bei_Kollision_hoch()
    {
        var vergeben = new HashSet<string> { "ama.mensah", "ama.mensah2" };
        Assert.Equal("ama.mensah3", IdentifiantRegel.Vorschlag("Ama", "Mensah", vergeben.Contains));
        Assert.Equal("kofi.mensah", IdentifiantRegel.Vorschlag("Kofi", "Mensah", vergeben.Contains));
    }

    [Fact]
    public void Kurze_Grundform_bekommt_Praefix_und_Platz_fuer_Zaehler()
    {
        Assert.Equal("compte.al", IdentifiantRegel.Grundform("Al", ""));
        var basis = IdentifiantRegel.Grundform(new string('x', 30), new string('y', 30));
        Assert.True(basis.Length <= 37);
        Assert.True(IdentifiantRegel.IstGueltig(basis + "99"));
    }

    [Fact]
    public void Startpasswort_ist_lesbar_und_erfuellt_die_Regeln()
    {
        var gesehen = new HashSet<string>();
        for (var i = 0; i < 2000; i++)
        {
            var pw = StartPasswort.Erzeuge();
            Assert.Equal(8, pw.Length);
            Assert.True(StartPasswort.IstLesbar(pw));
            Assert.DoesNotContain(pw, c => "0O1lI".Contains(c));
            Assert.True(pw.Count(char.IsDigit) >= 2);
            Assert.True(pw.Count(char.IsLetter) >= 2);
            Assert.Null(PasswortRegel.Pruefe(pw));
            gesehen.Add(pw);
        }
        Assert.True(gesehen.Count > 1990);
    }

    [Theory]
    [InlineData(NummernArt.Quittung, 2026, 42, null, "REC-2026-00042")]
    [InlineData(NummernArt.Ecriture, 2026, 42, null, "ECR-2026-00042")]
    [InlineData(NummernArt.Bulletin, 2027, 7, null, "BUL-2027-00007")]
    [InlineData(NummernArt.Plattformrechnung, 2026, 1, null, "SKL-2026-0001")]
    [InlineData(NummernArt.Matrikel, 2026, 1, "cdp", "CDP260001")]
    [InlineData(NummernArt.Matrikel, 2031, 123, "", "SK310123")]
    public void Nummernformate(NummernArt art, int jahr, long nr, string? praefix, string erwartet)
        => Assert.Equal(erwartet, NummernFormat.Formatiere(art, jahr, nr, praefix));

    [Fact]
    public void Nummernformat_Grenzen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NummernFormat.Formatiere(NummernArt.Quittung, 2026, 0));
        Assert.True(NummernFormat.IstGlobal(NummernArt.Plattformrechnung));
        Assert.False(NummernFormat.IstGlobal(NummernArt.Quittung));
        Assert.Equal("CDLP", NummernFormat.PraefixAusName("Collège Démo Les Palmiers"));
        Assert.Equal("ABCDEF", NummernFormat.MatrikelPraefix("ab-cd ef gh"));
    }

    [Fact]
    public void Kultur_mit_Tausenderpunkt_und_Komma()
    {
        Assert.Equal("150.000 FCFA", SkoulouKultur.Fcfa(150_000));
        Assert.Equal("12.500 FCFA", SkoulouKultur.Fcfa(12_500));
        Assert.Equal("0 FCFA", SkoulouKultur.Fcfa(0));
        Assert.Equal("1.250.000", SkoulouKultur.Zahl(1_250_000));
        Assert.Equal("13,67", SkoulouKultur.Zahl(13.666m));
        Assert.Equal("12/11/2026", SkoulouKultur.Datum(new DateOnly(2026, 11, 12)));
        Assert.Equal("jeudi 12 novembre 2026", SkoulouKultur.DatumLang(new DateOnly(2026, 11, 12)));
        Assert.Equal("08:30", SkoulouKultur.Uhrzeit(new TimeOnly(8, 30)));
        Assert.True(SkoulouKultur.Kultur.IsReadOnly);
    }

    [Fact]
    public void Schulzeit_Lome_ist_UTC()
    {
        var zeit = new SchulZeit(new TestZeit(new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 12), zeit.Heute);
        Assert.Equal(new DateTime(2026, 11, 12, 23, 30, 0), zeit.JetztLokal);
        Assert.Equal(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), zeit.TagesbeginnUtc(new DateOnly(2026, 11, 12)));
        Assert.Equal(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), zeit.TagesendeUtc(new DateOnly(2026, 11, 12)));
        Assert.Equal("12/11/2026 à 23:30", zeit.AlsText(new DateTime(2026, 11, 12, 23, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Schulzeit_andere_Zone_und_Rueckfall()
    {
        var paris = new SchulZeit(new TestZeit(new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.Zero)), "Europe/Paris");
        Assert.Equal(new DateOnly(2026, 11, 13), paris.Heute); // UTC+1 im November
        Assert.Equal(new DateTime(2026, 11, 12, 22, 30, 0, DateTimeKind.Utc), paris.ZuUtc(new DateTime(2026, 11, 12, 23, 30, 0)));
        var unbekannt = new SchulZeit(TimeProvider.System, "Mars/Olympus");
        Assert.Equal(TimeSpan.Zero, unbekannt.Zone.GetUtcOffset(new DateTime(2026, 6, 1)));
    }

    [Theory]
    [InlineData("abc12", "Le mot de passe doit comporter au moins 6 caractères.")]
    [InlineData("abcdefg", "Le mot de passe doit contenir au moins un chiffre.")]
    [InlineData("ABCDEF12", "Le mot de passe doit contenir au moins une lettre minuscule.")]
    [InlineData("abcde1", null)]
    public void Passwortregel(string pw, string? meldung) => Assert.Equal(meldung, PasswortRegel.Pruefe(pw));

    [Fact]
    public void Textschluessel_ohne_Akzente_und_Satzzeichen()
        => Assert.Equal("college demo les palmiers", TextNormalisierung.Schluessel("  Collège  Démo – Les Palmiers! "));
}
