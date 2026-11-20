using Microsoft.JSInterop;
using Skoulou.WebApp.Components.Shared;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Klicktest 20.11.2026: Nach einer Anmeldung auf einem zweiten Gerät stand je verlassener Verbindung eine
/// TaskCanceledException aus AuswahlFeld.DisposeAsync als « Unhandled exception in circuit » im Fehlerjournal.
/// </summary>
public class JsAufraeumenTests
{
    private sealed class WerfendesJs(Exception fehler) : IJSRuntime
    {
        public int Aufrufe;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) { Aufrufe++; throw fehler; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) { Aufrufe++; throw fehler; }
    }

    public static TheoryData<Exception> Verbindungsende =>
        [new TaskCanceledException(), new OperationCanceledException(), new JSDisconnectedException("weg"), new JSException("fehlt"), new InvalidOperationException("vorgerendert")];

    [Theory]
    [MemberData(nameof(Verbindungsende))]
    public async Task Aufraeumen_ohne_Verbindung_wirft_nicht(Exception fehler)
    {
        var js = new WerfendesJs(fehler);
        await JsAufraeumen.VersuchenAsync(js, "skCloserEntfernen", "id");
        Assert.Equal(1, js.Aufrufe);
    }

    [Fact]
    public async Task Andere_Fehler_bleiben_sichtbar()
        => await Assert.ThrowsAsync<ArgumentException>(() => JsAufraeumen.VersuchenAsync(new WerfendesJs(new ArgumentException("echt")), "skCloserEntfernen", "id"));
}
