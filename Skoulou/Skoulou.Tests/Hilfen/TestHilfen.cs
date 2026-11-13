using System.Net;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.Tests.Hilfen;

/// <summary>Steuerbare Uhr für Tests.</summary>
internal sealed class TestZeit(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Jetzt { get; set; } = start;

    public TestZeit() : this(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero)) { }

    public override DateTimeOffset GetUtcNow() => Jetzt;

    public void Vor(TimeSpan dauer) => Jetzt += dauer;
}

/// <summary>Fester Optionswert als IOptionsMonitor.</summary>
internal sealed class TestOptionen<T>(T wert) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = wert;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Versender ohne Netz: Ergebnis per Funktion steuerbar, Aufrufe werden gezählt.</summary>
internal sealed class FakeVersender : IMailVersender
{
    public bool IstBereit { get; set; } = true;
    public Func<MailAusgang, VersandErgebnis> Ergebnis { get; set; } = _ => new VersandErgebnis(true, "pm-1", null);
    public List<MailAusgang> Gesendet { get; } = [];

    public Task<VersandErgebnis> SendeAsync(MailAusgang mail, CancellationToken ct = default)
    {
        Gesendet.Add(mail);
        return Task.FromResult(Ergebnis(mail));
    }
}

/// <summary>HTTP ohne Netz: protokolliert Anfragen und antwortet mit festem Status.</summary>
internal sealed class FakeHttp(HttpStatusCode status) : HttpMessageHandler, IHttpClientFactory
{
    public List<(HttpRequestMessage Anfrage, string Inhalt)> Anfragen { get; } = [];
    public HttpStatusCode Status { get; set; } = status;

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var inhalt = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Anfragen.Add((request, inhalt));
        return new HttpResponseMessage(Status);
    }
}
