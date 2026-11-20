using Microsoft.JSInterop;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Klicktest 20.11.2026: Navigationsabbrüche verlassener Verbindungen (Sitzung auf einem anderen Gerät ersetzt) füllten das
/// Fehlerjournal. Nur genau diese werden ausgefiltert; andere Abbrüche und echte Fehler bleiben sichtbar.
/// </summary>
public class FehlerJournalFilterTests
{
    /// <summary>Gleicher Name wie der Blazor-Navigator, damit der Stacktrace die Herkunft trägt.</summary>
    private static class RemoteNavigationManager
    {
        public static Exception Werfe(Exception e) { try { throw e; } catch (Exception f) { return f; } }
    }

    private static Exception Anderswo(Exception e) { try { throw e; } catch (Exception f) { return f; } }

    [Fact]
    public void Abbruch_der_Navigation_einer_verlassenen_Verbindung_wird_nicht_protokolliert()
    {
        Assert.True(FehlerProtokollSink.IstVerlasseneVerbindung(RemoteNavigationManager.Werfe(new TaskCanceledException())));
        Assert.True(FehlerProtokollSink.IstVerlasseneVerbindung(RemoteNavigationManager.Werfe(new JSDisconnectedException("weg"))));
    }

    [Fact]
    public void Andere_Abbrueche_und_Fehler_bleiben_im_Journal()
    {
        Assert.False(FehlerProtokollSink.IstVerlasseneVerbindung(Anderswo(new TaskCanceledException())));
        Assert.False(FehlerProtokollSink.IstVerlasseneVerbindung(RemoteNavigationManager.Werfe(new InvalidOperationException("echt"))));
        Assert.False(FehlerProtokollSink.IstVerlasseneVerbindung(new TaskCanceledException()));   // ohne Stacktrace
    }
}
