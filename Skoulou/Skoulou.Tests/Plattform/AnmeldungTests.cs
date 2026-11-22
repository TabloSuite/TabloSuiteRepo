using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Anmelderegeln, Lockout und « eine Sitzung je Konto ».</summary>
public class AnmeldungTests
{
    [Theory]
    [InlineData(KontoStatus.Actif, Rollen.Lehrer, SchulStatus.Active, AnmeldeErgebnis.Erlaubt)]
    [InlineData(KontoStatus.Bloque, Rollen.Lehrer, SchulStatus.Active, AnmeldeErgebnis.KontoGesperrt)]
    [InlineData(KontoStatus.Archive, Rollen.Schueler, SchulStatus.Active, AnmeldeErgebnis.KontoArchiviert)]
    [InlineData(KontoStatus.Archive, Rollen.Lehrer, SchulStatus.Active, AnmeldeErgebnis.KontoArchiviert)]
    [InlineData(KontoStatus.Archive, Rollen.Responsable, SchulStatus.Active, AnmeldeErgebnis.KontoGesperrt)]
    [InlineData(KontoStatus.EnAttente, Rollen.Schueler, SchulStatus.Active, AnmeldeErgebnis.KontoInPruefung)]
    [InlineData(KontoStatus.Actif, Rollen.Lehrer, SchulStatus.Suspendue, AnmeldeErgebnis.SchuleSuspendiert)]
    [InlineData(KontoStatus.Actif, Rollen.Lehrer, SchulStatus.EnAttente, AnmeldeErgebnis.SchuleInPruefung)]
    [InlineData(KontoStatus.Actif, Rollen.Direktor, SchulStatus.EnAttente, AnmeldeErgebnis.Warteseite)]
    [InlineData(KontoStatus.EnAttente, Rollen.Direktor, SchulStatus.EnAttente, AnmeldeErgebnis.Warteseite)]
    [InlineData(KontoStatus.Actif, Rollen.Eltern, SchulStatus.Refusee, AnmeldeErgebnis.SchuleAbgelehnt)]
    [InlineData(KontoStatus.Actif, Rollen.Direktor, SchulStatus.Suspendue, AnmeldeErgebnis.SchuleSuspendiert)]
    public void Anmelderegeln(KontoStatus status, string rolle, SchulStatus schule, AnmeldeErgebnis erwartet)
        => Assert.Equal(erwartet, AnmeldeRegeln.Pruefe(status, rolle, schule));

    [Fact]
    public void Plattformrollen_brauchen_keine_Schule_Schulrollen_schon()
    {
        Assert.Equal(AnmeldeErgebnis.Erlaubt, AnmeldeRegeln.Pruefe(KontoStatus.Actif, Rollen.SuperAdmin, null));
        Assert.Equal(AnmeldeErgebnis.Erlaubt, AnmeldeRegeln.Pruefe(KontoStatus.Actif, Rollen.Responsable, null));
        Assert.Equal(AnmeldeErgebnis.KontoGesperrt, AnmeldeRegeln.Pruefe(KontoStatus.Actif, Rollen.Lehrer, null));
        foreach (var e in Enum.GetValues<AnmeldeErgebnis>().Where(e => !AnmeldeRegeln.IstZugelassen(e)))
            Assert.NotNull(AnmeldeRegeln.Meldung(AnmeldeRegeln.Code(e)));
        // Nutzer 22.11.2026: beim Passage archivierte Schüler bekommen eine verständliche Meldung statt « bloqué ».
        Assert.Contains("archivé", AnmeldeRegeln.Meldung(AnmeldeRegeln.Code(AnmeldeErgebnis.KontoArchiviert)));
        Assert.False(AnmeldeRegeln.IstZugelassen(AnmeldeErgebnis.KontoArchiviert));
    }

    [Fact]
    public async Task Sperrhinweise_erst_nach_richtigem_Passwort()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        await u.KontoAsync("prof.bloque", Rollen.Lehrer, schule.Id, KontoStatus.Bloque);
        var suspendiert = await u.SchuleAsync("S", SchulStatus.Suspendue);
        await u.KontoAsync("prof.susp", Rollen.Lehrer, suspendiert.Id);
        var wartend = await u.SchuleAsync("W", SchulStatus.EnAttente);
        await u.KontoAsync("prof.wait", Rollen.Lehrer, wartend.Id);
        await u.KontoAsync("dir.wait", Rollen.Direktor, wartend.Id);
        var anmeldung = u.Hole<AnmeldeService>();

        Assert.Equal(AnmeldeErgebnis.FalscheDaten, (await anmeldung.PruefeAsync("prof.bloque", "falsch123")).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.KontoGesperrt, (await anmeldung.PruefeAsync("prof.bloque", TestUmgebung.Passwort)).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.SchuleSuspendiert, (await anmeldung.PruefeAsync("prof.susp", TestUmgebung.Passwort)).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.SchuleInPruefung, (await anmeldung.PruefeAsync("PROF.WAIT", TestUmgebung.Passwort)).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.Warteseite, (await anmeldung.PruefeAsync(" dir.wait ", TestUmgebung.Passwort)).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.FalscheDaten, (await anmeldung.PruefeAsync("inconnu", TestUmgebung.Passwort)).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.FalscheDaten, (await anmeldung.PruefeAsync("", "")).Ergebnis);
    }

    [Fact]
    public async Task Fuenf_Fehlversuche_sperren_15_Minuten()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id);
        var anmeldung = u.Hole<AnmeldeService>();

        for (var i = 0; i < 4; i++)
            Assert.Equal(AnmeldeErgebnis.FalscheDaten, (await anmeldung.PruefeAsync("prof.a", "falsch123")).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.Verriegelt, (await anmeldung.PruefeAsync("prof.a", "falsch123")).Ergebnis);
        Assert.Equal(AnmeldeErgebnis.Verriegelt, (await anmeldung.PruefeAsync("prof.a", TestUmgebung.Passwort)).Ergebnis);
    }

    [Fact]
    public async Task Neue_Anmeldung_ersetzt_die_alte_Sitzung()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id);
        var anmeldung = u.Hole<AnmeldeService>();
        var pruefung = u.Hole<SitzungsPruefung>();

        var erste = (await anmeldung.PruefeAsync("prof.a", TestUmgebung.Passwort)).Konto!;
        await anmeldung.NeueSitzungAsync(erste);
        var cookieA = Principal(erste.Id, erste.AktiveSitzungId!.Value);
        Assert.True(await pruefung.IstGueltigAsync(cookieA));

        var zweitesGeraet = u.Hole<AnmeldeService>(); // eigene Anfrage (eigener Scope)
        var zweite = (await zweitesGeraet.PruefeAsync("prof.a", TestUmgebung.Passwort)).Konto!;
        await zweitesGeraet.NeueSitzungAsync(zweite);
        Assert.False(await pruefung.IstGueltigAsync(cookieA));
        Assert.True(await pruefung.IstGueltigAsync(Principal(zweite.Id, zweite.AktiveSitzungId!.Value)));
        Assert.False(await pruefung.IstGueltigAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task Sperre_beendet_Sitzung_nach_Cache_Ablauf_bzw_sofort_nach_Vergessen()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id);
        var anmeldung = u.Hole<AnmeldeService>();
        var pruefung = u.Hole<SitzungsPruefung>();
        var konto = (await anmeldung.PruefeAsync("prof.a", TestUmgebung.Passwort)).Konto!;
        await anmeldung.NeueSitzungAsync(konto);
        var cookie = Principal(konto.Id, konto.AktiveSitzungId!.Value);
        Assert.True(await pruefung.IstGueltigAsync(cookie));

        await using (var db = await u.Db.CreateDbContextAsync())
        {
            (await db.Schulen.SingleAsync()).Status = SchulStatus.Suspendue;
            await db.SaveChangesAsync();
        }
        Assert.True(await pruefung.IstGueltigAsync(cookie)); // 60-s-Cache
        pruefung.Vergessen(konto.Id);
        Assert.False(await pruefung.IstGueltigAsync(cookie));

        await anmeldung.SitzungBeendenAsync(konto.Id);
        await using var db2 = await u.Db.CreateDbContextAsync();
        Assert.Null((await db2.Users.SingleAsync()).AktiveSitzungId);
    }

    [Fact]
    public async Task Claims_enthalten_Sitzung_Schule_und_Rolle()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id);
        var anmeldung = u.Hole<AnmeldeService>();
        var konto = (await anmeldung.PruefeAsync("prof.a", TestUmgebung.Passwort)).Konto!;
        await anmeldung.NeueSitzungAsync(konto);
        var principal = await u.Hole<IUserClaimsPrincipalFactory<Konto>>().CreateAsync(konto);

        Assert.Equal(konto.AktiveSitzungId.ToString(), principal.FindFirstValue(SkoulouClaims.Sitzung));
        Assert.Equal(schule.Id, principal.SchuleId());
        Assert.Equal(Rollen.Lehrer, principal.Rolle());
        Assert.True(principal.IsInRole(Rollen.Lehrer));
        Assert.True(await u.Hole<SitzungsPruefung>().IstGueltigAsync(principal));
    }

    private static ClaimsPrincipal Principal(Guid kontoId, Guid sitzung)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, kontoId.ToString()),
            new Claim(SkoulouClaims.Sitzung, sitzung.ToString())
        ], "Test"));
}
