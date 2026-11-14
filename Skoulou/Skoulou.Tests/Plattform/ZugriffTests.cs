using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.Tests.Plattform;

/// <summary>Zugriff lädt den Akteur aus der DB und prüft Fähigkeit, Status, AGB und Schule der Ressource.</summary>
public class ZugriffTests
{
    [Fact]
    public async Task Direktor_darf_in_eigener_Schule_nicht_in_fremder()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var andere = await u.SchuleAsync("B");
        var direktor = await u.KontoAsync("dir.a", Rollen.Direktor, schule.Id);
        var zugriff = u.Hole<Zugriff>();

        var (akteur, art) = await zugriff.PruefeAsync(direktor.Id, Faehigkeit.KlassenVerwalten, schule.Id, schreiben: true);
        Assert.Equal(Zugriffsart.Ja, art);
        Assert.True(akteur.IstDirektion);
        var fehler = await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(direktor.Id, Faehigkeit.KlassenVerwalten, andere.Id));
        Assert.Equal(Zugriff.MeldungAndereSchule, fehler.Message);
    }

    [Fact]
    public async Task Gesperrtes_Konto_und_suspendierte_Schule_haben_keinen_Zugriff()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var gesperrt = await u.KontoAsync("dir.b", Rollen.Direktor, schule.Id, KontoStatus.Bloque);
        var suspendiert = await u.SchuleAsync("S", SchulStatus.Suspendue);
        var lehrer = await u.KontoAsync("prof.s", Rollen.Lehrer, suspendiert.Id);
        var zugriff = u.Hole<Zugriff>();

        Assert.Equal(Zugriff.MeldungKontoInaktiv,
            (await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(gesperrt.Id, Faehigkeit.KlassenVerwalten))).Message);
        Assert.Equal(Zugriff.MeldungSchuleGesperrt,
            (await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(lehrer.Id, Faehigkeit.NotenErfassen))).Message);
    }

    [Fact]
    public async Task Bedingungen_sperren_keinen_Zugriff()
    {
        // Wie eNOTE: Konten der Schule fallen unter die Zustimmung der Direktion bei der Registrierung.
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var lehrer = await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id, agbAkzeptiert: false);
        var (akteur, _) = await u.Hole<Zugriff>().PruefeAsync(lehrer.Id, Faehigkeit.NotenErfassen);
        Assert.Equal(lehrer.Id, akteur.Id);
    }

    [Fact]
    public async Task Lehrer_nur_eigene_und_kein_Schreiben_auf_Lesen()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var lehrer = await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id);
        var schueler = await u.KontoAsync("eleve.a", Rollen.Schueler, schule.Id);
        var zugriff = u.Hole<Zugriff>();

        Assert.Equal(Zugriffsart.Eigene, (await zugriff.PruefeAsync(lehrer.Id, Faehigkeit.NotenErfassen, schule.Id, schreiben: true)).Art);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(lehrer.Id, Faehigkeit.KontenVerwalten));
        Assert.Equal(Zugriffsart.Lesen, (await zugriff.PruefeAsync(schueler.Id, Faehigkeit.NotenErfassen)).Art);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(schueler.Id, Faehigkeit.NotenErfassen, schreiben: true));
    }

    [Fact]
    public async Task Koordinator_Finanzen_nur_mit_Recht()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var ohne = await u.KontoAsync("coord.a", Rollen.Koordinator, schule.Id);
        var mit = await u.KontoAsync("coord.b", Rollen.Koordinator, schule.Id, rechte: KoordinatorRechte.ZahlungenErfassen);
        var zugriff = u.Hole<Zugriff>();

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(ohne.Id, Faehigkeit.ZahlungenErfassen, schule.Id));
        Assert.Equal(Zugriffsart.Ja, (await zugriff.PruefeAsync(mit.Id, Faehigkeit.ZahlungenErfassen, schule.Id, true)).Art);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(mit.Id, Faehigkeit.BuchhaltungErfassen, schule.Id));
    }

    [Fact]
    public async Task Responsable_nur_seine_Schulen_Admin_alle()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var responsable = await u.KontoAsync("resp.a", Rollen.Responsable);
        var admin = await u.KontoAsync("admin.a", Rollen.SuperAdmin);
        var eigene = await u.SchuleAsync("A", responsable: responsable.Id);
        var fremde = await u.SchuleAsync("B");
        var zugriff = u.Hole<Zugriff>();

        Assert.Equal(Zugriffsart.Lesen, (await zugriff.PruefeAsync(responsable.Id, Faehigkeit.FinanzenEinsehen, eigene.Id)).Art);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(responsable.Id, Faehigkeit.FinanzenEinsehen, fremde.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(responsable.Id, Faehigkeit.SchuleVerwalten, eigene.Id, schreiben: true));
        Assert.Equal(Zugriffsart.Ja, (await zugriff.PruefeAsync(admin.Id, Faehigkeit.SchuleVerwalten, fremde.Id, true)).Art);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => zugriff.PruefeAsync(admin.Id, Faehigkeit.NotenErfassen, fremde.Id));
    }

    [Fact]
    public async Task Unbekanntes_Konto_wird_abgewiesen()
    {
        await using var u = new TestUmgebung();
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Hole<Zugriff>().LadeAkteurAsync(Guid.NewGuid()));
    }
}
