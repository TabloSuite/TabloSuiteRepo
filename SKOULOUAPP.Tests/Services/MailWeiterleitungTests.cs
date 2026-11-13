using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Eine Adresse für alle Apps: Zuordnung über den Betreff, Weiterleitung an eNOTE mit Geheimnis.</summary>
public class MailWeiterleitungTests
{
    [Theory]
    [InlineData("Re: [eNOTE] Votre facture", true)]
    [InlineData("Question sur eNote", true)]
    [InlineData("Re: [SKOULOU] eNOTE et SKOULOU", false)]   // Antwort auf eine SKOULOU-Mail bleibt hier
    [InlineData("Re: [SKOULOU] Bulletin", false)]
    [InlineData("", false)]
    public void Zuordnung_ueber_den_Betreff(string betreff, bool erwartet)
        => Assert.Equal(erwartet, MailWeiterleitung.GehoertZuEnote(betreff));

    [Theory]
    [InlineData("TabloSuite <contact@tablosuite.com>", true)]
    [InlineData("Afi <afi@example.test>", false)]
    [InlineData("", false)]
    public void Eigene_Mails_der_Apps_kommen_nicht_doppelt_in_den_Posteingang(string von, bool erwartet)
        => Assert.Equal(erwartet, MailWeiterleitung.IstEigeneAbsenderAdresse(von, "contact@tablosuite.com", null));

    [Theory]
    [InlineData("Bulletin", "[SKOULOU] Bulletin")]
    [InlineData("Re: [SKOULOU] Bulletin", "Re: [SKOULOU] Bulletin")]
    public void Betreff_traegt_einmal_den_App_Namen(string betreff, string erwartet)
        => Assert.Equal(erwartet, SkoulouEmailService.MitAppName(betreff));

    [Fact]
    public async Task Weiterleitung_sendet_JSON_mit_Basic_Auth_und_meldet_Erfolg()
    {
        var abfang = new Abfang(HttpStatusCode.OK);
        var dienst = Dienst(abfang, "https://enote.example/api/postmark/inbound", "geheim");

        Assert.True(await dienst.AnEnoteAsync("{\"Subject\":\"Re: [eNOTE] x\"}"));
        Assert.Equal("https://enote.example/api/postmark/inbound", abfang.Url);
        Assert.Equal("Basic " + Convert.ToBase64String("enote:geheim"u8.ToArray()), abfang.Authorization);
        Assert.Contains("[eNOTE]", abfang.Inhalt);
    }

    [Fact]
    public async Task Ohne_Konfiguration_oder_bei_Ablehnung_bleibt_die_Mail_bei_SKOULOU()
    {
        Assert.False(await Dienst(new Abfang(HttpStatusCode.OK), null, null).AnEnoteAsync("{}"));
        Assert.False(await Dienst(new Abfang(HttpStatusCode.Unauthorized), "https://enote.example/x", "falsch").AnEnoteAsync("{}"));
    }

    private static MailWeiterleitung Dienst(Abfang abfang, string? url, string? geheimnis)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Postmark:WeiterleitungEnoteUrl"] = url,
            ["Postmark:WeiterleitungEnoteGeheimnis"] = geheimnis
        }).Build();
        return new MailWeiterleitung(new Fabrik(abfang), config, NullLogger<MailWeiterleitung>.Instance);
    }

    private sealed class Fabrik(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Abfang(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        public string Inhalt { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Inhalt = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
