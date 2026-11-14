using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Components.Layout;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>E7b-Zusatz 7: Administratoren verwalten wie eNOTE – nur der Hauptadministrator, nie sich selbst oder ihn.</summary>
public class AdministratorenTests
{
    /// <summary>Start-Admin (ältestes Konto → Hauptadministrator) und ein zweiter, später angelegter Admin.</summary>
    private static async Task<(Konto Haupt, Konto Zweiter)> AdminsAsync(PlattformUmgebung u)
    {
        var haupt = await u.StartAsync();
        u.Zeit.Jetzt = u.Zeit.Jetzt.AddMinutes(5);
        var zweiter = await u.Basis.KontoAsync("admin.zwei", Rollen.SuperAdmin, email: "zwei@exemple.tg");
        return (haupt, zweiter);
    }

    [Fact]
    public async Task Aeltestes_Admin_Konto_wird_Hauptadministrator()
    {
        await using var u = new PlattformUmgebung();
        var (haupt, zweiter) = await AdminsAsync(u);
        Assert.True(await u.Admins.IstHauptAdminAsync(haupt.Id));
        Assert.False(await u.Admins.IstHauptAdminAsync(zweiter.Id));
        Assert.Equal(1, await u.LiesAsync(db => db.Users.CountAsync(k => k.IstHauptAdmin)));
        Assert.Contains(NavKarte.Fuer(Rollen.SuperAdmin, _ => false, hauptAdmin: true).SelectMany(g => g.Eintraege), e => e.Href == "/admin/administrateurs");
        Assert.DoesNotContain(NavKarte.Fuer(Rollen.SuperAdmin, _ => false).SelectMany(g => g.Eintraege), e => e.Href == "/admin/administrateurs");
    }

    [Fact]
    public async Task Hauptadministrator_legt_an_aendert_sperrt_und_loescht()
    {
        await using var u = new PlattformUmgebung();
        var (haupt, zweiter) = await AdminsAsync(u);

        var zugang = await u.Admins.AnlegenAsync(haupt.Id, new AdminEingabe("Akossiwa", "Mensah", "akossiwa@exemple.tg", "akossiwa.admin"));
        Assert.Equal(("akossiwa.admin", "Administrateur"), (zugang.Identifiant, zugang.Rolle));
        Assert.False(string.IsNullOrEmpty(zugang.Passwort));
        var neu = await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == zugang.KontoId));
        Assert.Equal((Rollen.SuperAdmin, true, false, haupt.Id), (neu.Rolle, neu.MussPasswortAendern, neu.IstHauptAdmin, neu.ErstelltVonId));
        // Neue Administratoren haben die Rechte des SuperAdmin (z. B. Plattform verwalten), aber nicht diese Verwaltung.
        await u.Basis.Hole<Zugriff>().AdminAsync(neu.Id, default);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Admins.ListeAsync(neu.Id));

        await u.Admins.BearbeitenAsync(haupt.Id, zweiter.Id, new AdminEingabe("Kodjo", "Amouzou", "kodjo@exemple.tg"));
        var geaendert = await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == zweiter.Id));
        Assert.Equal(("Kodjo", "Amouzou", "kodjo@exemple.tg"), (geaendert.Vorname, geaendert.Nachname, geaendert.Email));

        var sitzung = geaendert.AktiveSitzungId;
        await u.Admins.SperrenAsync(haupt.Id, zweiter.Id, true);
        var gesperrt = await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == zweiter.Id));
        Assert.Equal(KontoStatus.Bloque, gesperrt.Status);
        Assert.NotEqual(sitzung, gesperrt.AktiveSitzungId); // sofort abgemeldet
        await u.Admins.SperrenAsync(haupt.Id, zweiter.Id, false);
        Assert.Equal(KontoStatus.Actif, (await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == zweiter.Id))).Status);

        await u.Admins.LoeschenAsync(haupt.Id, zugang.KontoId);
        Assert.Equal(KontoStatus.Archive, (await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == zugang.KontoId))).Status);
        var liste = await u.Admins.ListeAsync(haupt.Id);
        Assert.Equal(new[] { haupt.Id, zweiter.Id }, liste.Select(a => a.Id));
        Assert.True(liste[0].IstHauptAdmin);

        var aktionen = await u.LiesAsync(db => db.AuditEintraege.Where(a => a.AkteurId == haupt.Id).Select(a => a.Aktion).ToListAsync());
        Assert.Contains(AuditAktionen.KontoAngelegt, aktionen);
        Assert.Contains(AuditAktionen.KontoGesperrt, aktionen);
        Assert.Contains(PlattformAudit.KontoEntsperrt, aktionen);
        Assert.Contains(AuditAktionen.KontoArchiviert, aktionen);
    }

    [Fact]
    public async Task Nur_der_Hauptadministrator_und_nie_sich_selbst_oder_ihn()
    {
        await using var u = new PlattformUmgebung();
        var (haupt, zweiter) = await AdminsAsync(u);
        var eingabe = new AdminEingabe("A", "B", "ab@exemple.tg");

        // Ein anderer Administrator darf nichts davon.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Admins.AnlegenAsync(zweiter.Id, eingabe));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Admins.SperrenAsync(zweiter.Id, haupt.Id, true));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Admins.LoeschenAsync(zweiter.Id, haupt.Id));
        // … auch nicht über « Comptes »: Hauptadministrator zurücksetzen bzw. einen anderen Admin entsperren/zurücksetzen.
        await Assert.ThrowsAsync<FachFehler>(() => u.Konten.PasswortZuruecksetzenAsync(zweiter.Id, haupt.Id));
        var dritter = await u.Basis.KontoAsync("admin.drei", Rollen.SuperAdmin, status: KontoStatus.Bloque);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Konten.EntsperrenAsync(zweiter.Id, dritter.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Konten.PasswortZuruecksetzenAsync(zweiter.Id, dritter.Id));
        await u.Konten.EntsperrenAsync(haupt.Id, dritter.Id);

        // Der Hauptadministrator ändert, sperrt oder löscht weder sich selbst noch wird er hier geändert.
        await Assert.ThrowsAsync<FachFehler>(() => u.Admins.SperrenAsync(haupt.Id, haupt.Id, true));
        await Assert.ThrowsAsync<FachFehler>(() => u.Admins.LoeschenAsync(haupt.Id, haupt.Id));
        await Assert.ThrowsAsync<FachFehler>(() => u.Admins.BearbeitenAsync(haupt.Id, haupt.Id, eingabe));
        // Nur Administratorkonten (kein Direktor o. Ä.).
        var schule = await u.Basis.SchuleAsync("A");
        var direktor = await u.Basis.KontoAsync("dir.a", Rollen.Direktor, schule.Id);
        await Assert.ThrowsAsync<FachFehler>(() => u.Admins.LoeschenAsync(haupt.Id, direktor.Id));
        // E-Mail ist Pflicht.
        Assert.Equal("email", (await Assert.ThrowsAsync<FachFehler>(() => u.Admins.AnlegenAsync(haupt.Id, eingabe with { Email = null }))).Feld);
    }
}
