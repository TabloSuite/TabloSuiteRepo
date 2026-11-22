using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: Nach « Déconnecter » eines Geräts mit bereits geschlossenem Tab versuchte der SitzungsWaechter
/// umzuleiten – « Navigation failed » + « Unhandled exception in circuit » im Journal. Umleitung nur mit Verbindung.
/// </summary>
public class VerbindungsStatusTests
{
    [Fact]
    public async Task Merkt_Verbindung_Und_Meldet_Wiederverbindung()
    {
        var status = new VerbindungsStatus();
        var gemeldet = 0;
        status.WiederVerbunden += () => gemeldet++;
        Assert.True(status.Verbunden);
        await status.OnConnectionDownAsync(null!, default);
        Assert.False(status.Verbunden);
        await status.OnConnectionUpAsync(null!, default);
        Assert.True(status.Verbunden);
        Assert.Equal(1, gemeldet);
    }

    [Fact]
    public void SitzungsWaechter_Leitet_Nur_Mit_Verbindung_Um_Und_Ist_Registriert()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eNOTE.WebApp"))) dir = dir.Parent;
        var app = Path.Combine(dir!.FullName, "eNOTE.WebApp");
        var waechter = File.ReadAllText(Path.Combine(app, "Components", "Shared", "SitzungsWaechter.razor"));
        Assert.Contains("if (!Verbindung.Verbunden) { umleitungAusstehend = true; return; }", waechter);
        Assert.Contains("Verbindung.WiederVerbunden -= NachWiederverbindung", waechter);
        Assert.Contains("CircuitHandler>(sp => sp.GetRequiredService<VerbindungsStatus>())", File.ReadAllText(Path.Combine(app, "Program.cs")));
    }
}
