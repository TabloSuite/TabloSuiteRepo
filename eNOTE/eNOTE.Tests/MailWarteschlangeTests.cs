using System.Net;
using System.Text;
using ENote.WebApp.Data;
using ENote.WebApp.Services.Mails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Klicktest 20.11.: Aktionen mit Mail (« J'ai payé », Bestätigungen, Zugänge) ließen den Knopf 2–8 s drehen, bei
/// Postmark-Ausfall bis 20 s. Seitdem legt <see cref="EmailService.SendeAsync(string,string,string,string?,CancellationToken)"/>
/// die Mail nur in die Warteschlange und weckt den <see cref="MailVersandDienst"/>, der sie im Hintergrund versendet.
/// </summary>
public class MailWarteschlangeTests
{
    /// <summary>Postmark-Attrappe: zählt Aufrufe, merkt die Empfänger, kann blockieren oder Fehler liefern.</summary>
    private sealed class PostmarkAttrappe : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Empfaenger { get; } = [];
        public TaskCompletionSource? Sperre { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = await request.Content!.ReadAsStringAsync(ct);
            var an = System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()
                .First(p => string.Equals(p.Name, "To", StringComparison.OrdinalIgnoreCase)).Value.GetString()!;
            lock (Empfaenger) Empfaenger.Add(an);
            if (Sperre is { } s) await s.Task.WaitAsync(ct);
            var antwort = Status == HttpStatusCode.OK
                ? """{"ErrorCode":0,"Message":"OK","MessageID":"pm-1"}"""
                : """{"ErrorCode":500,"Message":"Indisponible"}""";
            return new HttpResponseMessage(Status) { Content = new StringContent(antwort, Encoding.UTF8, "application/json") };
        }
    }

    private static IConfiguration PostmarkKonfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Mail:Provider"] = "Postmark",
        ["Mail:PostmarkServerToken"] = "test-token"
    }).Build();

    private static (EmailService Email, IDbContextFactory<ApplicationDbContext> F, PostmarkAttrappe Postmark, MailWeckruf Weckruf) Aufbau()
    {
        var f = TestDbContextFactory.Create();
        var postmark = new PostmarkAttrappe();
        var weckruf = new MailWeckruf();
        return (new EmailService(PostmarkKonfiguration(), f, NullLogger<EmailService>.Instance, postmark, weckruf), f, postmark, weckruf);
    }

    [Fact]
    public async Task Klick_wartet_nicht_auf_den_Versand_und_weckt_den_Dienst()
    {
        var (email, f, postmark, weckruf) = Aufbau();
        postmark.Sperre = new TaskCompletionSource();               // Postmark « hängt »

        var senden = email.SendeAsync("ama@exemple.com", "Paiement signalé", "<p>x</p>", "paiement-signale");
        var fertig = await Task.WhenAny(senden, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(senden, fertig);                                // kehrt sofort zurück, obwohl Postmark hängt
        Assert.True(await senden);                                  // eingereiht, wird versendet
        Assert.Empty(postmark.Empfaenger);                          // im Klick kein Versand
        await using var db = await f.CreateDbContextAsync();
        var m = await db.MailNachrichten.SingleAsync();
        Assert.Equal((false, 0, "[eNOTE] Paiement signalé"), (m.IstGesendet, m.Versuche, m.Betreff));
        Assert.True(await weckruf.WarteAsync(TimeSpan.Zero, CancellationToken.None));
        postmark.Sperre.SetResult();
    }

    [Fact]
    public async Task Hintergrunddienst_versendet_eingereihte_Mail_gleich_nach_dem_Weckruf()
    {
        var f = TestDbContextFactory.Create();
        var postmark = new PostmarkAttrappe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(PostmarkKonfiguration());
        services.AddSingleton(f);
        services.AddSingleton<IHttpClientFactory>(postmark);
        services.AddSingleton<MailWeckruf>();
        services.AddScoped<EmailService>();
        await using var sp = services.BuildServiceProvider();

        // Fehlversuche/Abo-Mails erst nach einer Stunde: hier zählt nur der Weckruf.
        var dienst = new MailVersandDienst(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<MailVersandDienst>.Instance,
            sp.GetRequiredService<MailWeckruf>()) { StartVerzoegerung = TimeSpan.Zero, PruefVerzoegerung = TimeSpan.FromHours(1) };
        await dienst.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(200);                                  // Dienst wartet jetzt auf den Weckruf (höchstens 1 Minute)
            using (var scope = sp.CreateScope())
                Assert.True(await scope.ServiceProvider.GetRequiredService<EmailService>()
                    .SendeAsync("ama@exemple.com", "Votre compte eNOTE est prêt", "<p>x</p>", "compte-cree"));

            var gesendet = false;
            for (var i = 0; i < 50 && !gesendet; i++)
            {
                await Task.Delay(100);
                await using var db = await f.CreateDbContextAsync();
                gesendet = await db.MailNachrichten.AnyAsync(m => m.IstGesendet && m.AnbieterId == "pm-1");
            }
            Assert.True(gesendet, "Die Mail wurde nicht innerhalb von 5 s im Hintergrund versendet.");
            Assert.Equal(["ama@exemple.com"], postmark.Empfaenger);
        }
        finally
        {
            await dienst.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Mot_de_passe_oublie_kommt_in_der_Warteschlange_zuerst()
    {
        var (email, _, postmark, _) = Aufbau();
        for (var i = 0; i < 3; i++)
            await email.SendeAsync($"po{i}@exemple.com", "Nouveautés", "<p>x</p>", "message");
        await email.SendeAsync("oublie@exemple.com", "Réinitialisation du mot de passe", "<p>x</p>", EmailService.KategorieVorrang);

        var (gesendet, mehr) = await email.VersendeNeueAsync();

        Assert.Equal((4, false), (gesendet, mehr));
        Assert.Equal("oublie@exemple.com", postmark.Empfaenger[0]);
    }

    [Fact]
    public async Task Fehlversuch_wird_nicht_bei_jedem_Weckruf_sondern_beim_Nachsenden_wiederholt()
    {
        var (email, f, postmark, _) = Aufbau();
        postmark.Status = HttpStatusCode.ServiceUnavailable;
        await email.SendeAsync("ama@exemple.com", "Facture", "<p>x</p>", "facture");

        await email.VersendeNeueAsync();
        await email.VersendeNeueAsync();                           // erneuter Weckruf: kein zweiter Versuch
        Assert.Single(postmark.Empfaenger);

        postmark.Status = HttpStatusCode.OK;
        Assert.Equal(1, await email.WiederholeAusstehendeAsync()); // alle 5 Minuten
        await using var db = await f.CreateDbContextAsync();
        var m = await db.MailNachrichten.SingleAsync();
        Assert.Equal((true, 2), (m.IstGesendet, m.Versuche));
    }

    [Fact]
    public async Task Pruefmail_der_Administration_sendet_direkt()
    {
        var (email, f, postmark, _) = Aufbau();

        Assert.True(await email.SendeSofortAsync("admin@exemple.com", "Vérification de l'envoi eNOTE", "<p>x</p>", "test"));

        Assert.Equal(["admin@exemple.com"], postmark.Empfaenger);
        await using var db = await f.CreateDbContextAsync();
        Assert.True((await db.MailNachrichten.SingleAsync()).IstGesendet);
    }

    [Fact]
    public async Task Abgeschaltete_oder_ungueltige_Mail_wird_nicht_eingereiht()
    {
        var (email, f, _, weckruf) = Aufbau();

        Assert.False(await email.SendeAsync("ama@exemple.com", "Bienvenue", "<p>x</p>", "bienvenue"));   // standardmäßig aus
        Assert.False(await email.SendeAsync("pas-une-adresse", "Test", "<p>x</p>", "message"));

        await using var db = await f.CreateDbContextAsync();
        Assert.Empty(await db.MailNachrichten.ToListAsync());
        Assert.False(await weckruf.WarteAsync(TimeSpan.Zero, CancellationToken.None));
    }
}
