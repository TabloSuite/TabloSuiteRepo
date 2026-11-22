using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Core;

/// <summary>
/// Klicktest 22.11.2026: Endete die Sitzung eines Geräts mit bereits geschlossenem Tab, leitete ZugriffUmleitung trotzdem
/// um – « Navigation failed » + « Unhandled exception in circuit » im Log. Umleitung nur mit Verbindung (wie eNOTE).
/// </summary>
public class VerbindungsStatusTests
{
    [Fact]
    public async Task Merkt_Verbindung_und_meldet_Wiederverbindung()
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
    public void ZugriffUmleitung_leitet_nur_mit_Verbindung_um_und_Status_ist_registriert()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Skoulou.WebApp"))) dir = dir.Parent;
        var app = Path.Combine(dir!.FullName, "Skoulou.WebApp");
        var umleitung = File.ReadAllText(Path.Combine(app, "Components", "Shared", "ZugriffUmleitung.razor"));
        Assert.Contains("if (!Verbindung.Verbunden) { umleitungAusstehend = true; return; }", umleitung);
        Assert.Contains("Verbindung.WiederVerbunden -= NachWiederverbindung", umleitung);
        Assert.Contains("CircuitHandler>(sp => sp.GetRequiredService<VerbindungsStatus>())",
            File.ReadAllText(Path.Combine(app, "Services", "Plattform", "PlattformRegistrierung.cs")));
    }
}
