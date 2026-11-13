using Skoulou.Core.Rechte;

namespace Skoulou.Tests.Core;

/// <summary>Die Rechte-Matrix des Bauplans §3.4: darf / darf nicht je Rolle.</summary>
public class RechteMatrixTests
{
    [Theory]
    [InlineData(Rollen.Direktor, Faehigkeit.SchuleVerwalten, Zugriffsart.Ja)]
    [InlineData(Rollen.Koordinator, Faehigkeit.SchuleVerwalten, Zugriffsart.Nein)]
    [InlineData(Rollen.Responsable, Faehigkeit.SchuleVerwalten, Zugriffsart.Lesen)]
    [InlineData(Rollen.SuperAdmin, Faehigkeit.SchuleVerwalten, Zugriffsart.Ja)]
    [InlineData(Rollen.Lehrer, Faehigkeit.SchuleVerwalten, Zugriffsart.Nein)]
    [InlineData(Rollen.Koordinator, Faehigkeit.KontenVerwalten, Zugriffsart.Ja)]
    [InlineData(Rollen.Lehrer, Faehigkeit.KontenVerwalten, Zugriffsart.Nein)]
    [InlineData(Rollen.Koordinator, Faehigkeit.KoordinatorenVerwalten, Zugriffsart.Nein)]
    [InlineData(Rollen.Direktor, Faehigkeit.KoordinatorenVerwalten, Zugriffsart.Ja)]
    [InlineData(Rollen.SuperAdmin, Faehigkeit.SchuljahrVerwalten, Zugriffsart.Nein)]
    [InlineData(Rollen.Koordinator, Faehigkeit.StundenplanVerwalten, Zugriffsart.Ja)]
    [InlineData(Rollen.Lehrer, Faehigkeit.AnwesenheitErfassen, Zugriffsart.Eigene)]
    [InlineData(Rollen.Lehrer, Faehigkeit.NotenErfassen, Zugriffsart.Eigene)]
    [InlineData(Rollen.Schueler, Faehigkeit.NotenErfassen, Zugriffsart.Lesen)]
    [InlineData(Rollen.Eltern, Faehigkeit.NotenErfassen, Zugriffsart.Lesen)]
    [InlineData(Rollen.Lehrer, Faehigkeit.NotenAllerFaecher, Zugriffsart.Nein)]
    [InlineData(Rollen.Lehrer, Faehigkeit.KlassenratErfassen, Zugriffsart.Eigene)]
    [InlineData(Rollen.Lehrer, Faehigkeit.BulletinsVeroeffentlichen, Zugriffsart.Nein)]
    [InlineData(Rollen.Koordinator, Faehigkeit.BulletinsVeroeffentlichen, Zugriffsart.Ja)]
    [InlineData(Rollen.Lehrer, Faehigkeit.InhalteErstellen, Zugriffsart.Eigene)]
    [InlineData(Rollen.Direktor, Faehigkeit.InhalteErstellen, Zugriffsart.Lesen)]
    [InlineData(Rollen.Direktor, Faehigkeit.InhalteFuerLehrerErfassen, Zugriffsart.Ja)]
    [InlineData(Rollen.Schueler, Faehigkeit.InhalteErstellen, Zugriffsart.Nein)]
    [InlineData(Rollen.Lehrer, Faehigkeit.Korrigieren, Zugriffsart.Eigene)]
    [InlineData(Rollen.Schueler, Faehigkeit.FinanzenEinsehen, Zugriffsart.Eigene)]
    [InlineData(Rollen.Responsable, Faehigkeit.FinanzenEinsehen, Zugriffsart.Lesen)]
    [InlineData(Rollen.SuperAdmin, Faehigkeit.FinanzenEinsehen, Zugriffsart.Lesen)]
    [InlineData(Rollen.Lehrer, Faehigkeit.FinanzenEinsehen, Zugriffsart.Nein)]
    [InlineData(Rollen.SuperAdmin, Faehigkeit.ZahlungenErfassen, Zugriffsart.Nein)]
    [InlineData(Rollen.Responsable, Faehigkeit.RechnungSkoulouBearbeiten, Zugriffsart.Lesen)]
    [InlineData(Rollen.SuperAdmin, Faehigkeit.PlattformVerwalten, Zugriffsart.Ja)]
    [InlineData(Rollen.Direktor, Faehigkeit.PlattformVerwalten, Zugriffsart.Nein)]
    [InlineData(Rollen.Responsable, Faehigkeit.SchulenBetreuen, Zugriffsart.Eigene)]
    [InlineData(Rollen.Eltern, Faehigkeit.SchulenBetreuen, Zugriffsart.Nein)]
    public void Matrix_entspricht_dem_Bauplan(string rolle, Faehigkeit faehigkeit, Zugriffsart erwartet)
        => Assert.Equal(erwartet, RechteMatrix.Zugriff(rolle, faehigkeit, KoordinatorRechte.Keine));

    [Theory]
    [InlineData(Faehigkeit.FinanzenEinsehen, KoordinatorRechte.FinanzenEinsehen)]
    [InlineData(Faehigkeit.ZahlungenErfassen, KoordinatorRechte.ZahlungenErfassen)]
    [InlineData(Faehigkeit.GebuehrenVerwalten, KoordinatorRechte.ZahlungenErfassen)]
    [InlineData(Faehigkeit.BuchhaltungErfassen, KoordinatorRechte.BuchhaltungErfassen)]
    [InlineData(Faehigkeit.RechnungSkoulouBearbeiten, KoordinatorRechte.RechnungSkoulou)]
    public void Koordinator_braucht_das_passende_Recht(Faehigkeit faehigkeit, KoordinatorRechte recht)
    {
        Assert.Equal(Zugriffsart.Nein, RechteMatrix.Zugriff(Rollen.Koordinator, faehigkeit, KoordinatorRechte.Keine));
        Assert.Equal(Zugriffsart.Nein, RechteMatrix.Zugriff(Rollen.Koordinator, faehigkeit, KoordinatorRechte.Alle & ~recht));
        Assert.Equal(Zugriffsart.Ja, RechteMatrix.Zugriff(Rollen.Koordinator, faehigkeit, recht));
        Assert.Equal(Zugriffsart.Ja, RechteMatrix.Zugriff(Rollen.Direktor, faehigkeit, KoordinatorRechte.Keine));
    }

    [Fact]
    public void Jede_Faehigkeit_steht_in_der_Matrix_und_unbekannte_Rollen_duerfen_nichts()
    {
        foreach (var f in Enum.GetValues<Faehigkeit>())
        {
            Assert.Contains(f, RechteMatrix.Faehigkeiten);
            Assert.Equal(Zugriffsart.Nein, RechteMatrix.Zugriff("Inconnu", f));
            Assert.Equal(Zugriffsart.Nein, RechteMatrix.Zugriff(null, f));
        }
    }

    [Fact]
    public void Lesen_ist_kein_Schreiben()
    {
        Assert.False(RechteMatrix.DarfSchreiben(Zugriffsart.Lesen));
        Assert.False(RechteMatrix.DarfSchreiben(Zugriffsart.Nein));
        Assert.True(RechteMatrix.DarfSchreiben(Zugriffsart.Eigene));
        Assert.True(RechteMatrix.DarfSchreiben(Zugriffsart.Ja));
    }

    [Fact]
    public void Rollen_haben_franzoesische_Bezeichnungen_und_Startseiten()
    {
        Assert.Equal("Responsable SKOULOU", Rollen.Bezeichnung(Rollen.Responsable));
        Assert.Equal("Élève", Rollen.Bezeichnung(Rollen.Schueler));
        Assert.Equal("Parent", Rollen.Bezeichnung(Rollen.Eltern));
        Assert.Equal("/direction", Rollen.Startseite(Rollen.Koordinator));
        Assert.All(Rollen.Alle, r => Assert.NotEqual("/", Rollen.Startseite(r)));
    }
}
