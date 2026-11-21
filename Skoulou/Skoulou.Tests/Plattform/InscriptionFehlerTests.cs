using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Registrierung bei langsamem Netz (21.11.2026): lehnt der Server das klassische Absenden ab, nennt « ?erreur= » den
/// konkreten Grund und das Feld – nur feste Texte, nie Text aus der Adresse.
/// </summary>
public class InscriptionFehlerTests
{
    [Fact]
    public async Task Vergebenes_Identifiant_ergibt_eigenen_Code_und_Meldung_am_Feld()
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction() with { Identifiant = "kossi.mensah" }, null);
        var f = await Assert.ThrowsAsync<FachFehler>(() =>
            u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction("Lycée Autre", "autre@exemple.tg") with { Identifiant = "kossi.mensah" }, null));
        var code = InscriptionFehler.Code(f);
        Assert.Equal("identifiant-pris", code);
        Assert.Equal(new InscriptionMeldung(IdentifiantRegel.MeldungVergeben, "identifiant"), InscriptionFehler.Meldung(code));

        var schule = await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction(email: "drei@exemple.tg"), null));
        Assert.Equal("ecole-existe", InscriptionFehler.Code(schule));
        Assert.Equal("ecole-nom", InscriptionFehler.Meldung("ecole-existe")!.Feld);
    }

    [Theory]
    [InlineData("telephone", "champ-telephone")]
    [InlineData("confirmation", "champ-confirmation")]
    [InlineData("ecole", "champ-ecole")]
    [InlineData("agb", "agb")]
    [InlineData("unbekannt", "saisie")]
    [InlineData(null, "saisie")]
    public void Felder_ergeben_Feldcodes(string? feld, string code)
        => Assert.Equal(code, InscriptionFehler.Code(new FachFehler("Texte quelconque", feld)));

    [Fact]
    public void Bekannte_Meldungen_haben_Vorrang_vor_dem_Feld()
    {
        Assert.Equal("bremse", InscriptionFehler.Code(new FachFehler(RegistrierungsBremse.Meldung)));
        Assert.Equal("ecole-fermee", InscriptionFehler.Code(new FachFehler(RegistrierungService.MeldungSchuleGeschlossen, "ecole")));
        Assert.Equal(new InscriptionMeldung(PasswortService.MeldungBestaetigung, "confirmation"), InscriptionFehler.Meldung("champ-confirmation"));
        Assert.Equal("telephone", InscriptionFehler.Meldung("champ-telephone")!.Feld);
    }

    [Theory]
    [InlineData("saisie")]
    [InlineData("champ-")]
    [InlineData("champ-<script>")]
    [InlineData("Votre compte est bloqué, appelez le +228…")]
    public void Unbekannte_Codes_zeigen_nur_die_allgemeine_Meldung(string code)
        => Assert.Equal(new InscriptionMeldung(InscriptionFehler.Allgemein), InscriptionFehler.Meldung(code));

    [Fact]
    public void Ohne_Code_keine_Meldung() => Assert.Null(InscriptionFehler.Meldung(null));

    [Fact]
    public async Task Schule_fuer_die_Wiederherstellung_nur_wenn_aktiv()
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        var aktiv = (await u.Basis.SchuleAsync("Collège Ouvert")).Id;
        Assert.Equal("Collège Ouvert", (await u.Registrierung.SchuleAsync(aktiv))?.Name);
        await u.LiesAsync(async db =>
        {
            db.Schulen.Single(s => s.Id == aktiv).Status = SchulStatus.Suspendue;
            return await db.SaveChangesAsync();
        });
        Assert.Null(await u.Registrierung.SchuleAsync(aktiv));
        Assert.Null(await u.Registrierung.SchuleAsync(Guid.NewGuid()));
    }
}
