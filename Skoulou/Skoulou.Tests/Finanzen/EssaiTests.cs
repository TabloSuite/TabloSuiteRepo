using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Finanzen;

/// <summary>Probezeit (18.11.2026): die ersten Abrechnungsperioden des ersten Schuljahres ohne SKOULOU-Rechnung.</summary>
public class EssaiTests
{
    // Test-Schule A: angemeldet am 13.11.2026, Jahr 2026-2027 mit drei Trimestern (P1 bis 18.12., P2 bis 26.03., P3 bis 30.06.)
    private static readonly DateOnly NachP1 = new(2026, 12, 19), NachP2 = new(2027, 3, 27);

    private static EssaiService Dienst(FinanzWelt w)
    {
        var mailAusgang = new MailAusgangService(w.Db, w.Org.D.Versender, new MailSignal(), w.Zeit, NullLogger<MailAusgangService>.Instance);
        var app = new OptionsWrapper<AppOptionen>(new AppOptionen { BaseUrl = "https://skoulou.test" });
        return new EssaiService(w.Db, new Zugriff(w.Db), new AuditService(w.Zeit), new PlattformMails(mailAusgang, app, NullLogger<PlattformMails>.Instance), w.Zeit);
    }

    [Fact]
    public async Task Probe_Periode_ohne_Rechnung_danach_fest()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var essai = Dienst(w);
        var stand = await essai.SetzenAsync(w.Org.Admin, w.Org.SchuleA, 1);
        Assert.Equal((1, 3, 0, true), (stand.Perioden, stand.Max, stand.Min, stand.Aenderbar));
        Assert.Equal([w.Org.P1], stand.Offert.Select(p => p.Id));
        Assert.Contains("1er trimestre 2026-2027", stand.Text);

        w.Heute(NachP1);
        Assert.Equal(0, await w.Abrechnung.FaelligeAbrechnenAsync());
        var fehler = await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1));
        Assert.Contains("période d'essai", fehler.Message);
        Assert.Contains(await w.Rechnungen.AbrechenbareAsync(w.Org.Admin, w.Org.SchuleA), p => p.PeriodeId == w.Org.P1 && p.Essai);
        // Eine schon vergangene Probe-Periode bleibt geschenkt
        await Assert.ThrowsAsync<FachFehler>(() => essai.SetzenAsync(w.Org.Admin, w.Org.SchuleA, 0));

        w.Heute(NachP2);
        Assert.Equal(1, await w.Abrechnung.FaelligeAbrechnenAsync());
        var rechnung = await w.MitDbAsync(db => db.Plattformrechnungen.SingleAsync());
        Assert.Equal(w.Org.P2, rechnung.PeriodeId);
        // Mit der ersten Rechnung ist die Probezeit fest
        Assert.False((await essai.StandAsync(w.Org.Admin, w.Org.SchuleA)).Aenderbar);
        await Assert.ThrowsAsync<FachFehler>(() => essai.SetzenAsync(w.Org.Admin, w.Org.SchuleA, 2));
    }

    [Fact]
    public async Task Responsable_nur_eigene_Schulen_Admin_wird_benachrichtigt()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.MitDbAsync(async db =>
        {
            var admin = await db.Users.FirstAsync(k => k.Id == w.Org.Admin);
            admin.Email = "admin@skoulou.test";
            return await db.SaveChangesAsync();
        });
        var essai = Dienst(w);

        var stand = await essai.SetzenAsync(w.Org.Responsable, w.Org.SchuleA, 2);
        Assert.Equal(2, stand.Perioden);
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == PlattformAudit.EssaiGeaendert && a.SchuleId == w.Org.SchuleA)));
        Assert.Equal(1, await w.MitDbAsync(db => db.MailAusgaenge.CountAsync(m => m.Bezug == "admin-essai")));

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => essai.SetzenAsync(w.Org.Responsable, w.Org.SchuleB, 1));
        // Die Direktion sieht den Stand, ändert ihn aber nicht
        var direktion = await essai.EigeneSchuleAsync(w.D);
        Assert.Equal((2, false), (direktion.Perioden, direktion.DarfAendern));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => essai.SetzenAsync(w.D, w.Org.SchuleA, 1));
    }

    [Fact]
    public async Task Grenzen_null_bis_Periodenzahl()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var essai = Dienst(w);
        await Assert.ThrowsAsync<FachFehler>(() => essai.SetzenAsync(w.Org.Admin, w.Org.SchuleA, 4));   // nur 3 Trimester
        await Assert.ThrowsAsync<FachFehler>(() => essai.SetzenAsync(w.Org.Admin, w.Org.SchuleA, -1));
        var ganzesJahr = await essai.SetzenAsync(w.Org.Admin, w.Org.SchuleA, 3);
        Assert.Equal(3, ganzesJahr.Offert.Count);

        w.Heute(new DateOnly(2027, 7, 1));
        Assert.Equal(0, await w.Abrechnung.FaelligeAbrechnenAsync());
    }

    [Fact]
    public void Neue_Schulen_ohne_Probezeit_der_Admin_entscheidet() => Assert.Equal(0, new Schule().EssaiPerioden);
}
