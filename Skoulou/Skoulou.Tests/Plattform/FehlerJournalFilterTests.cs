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
    private static Serilog.Events.LogEvent EfFehler(Exception ex) => new(DateTimeOffset.UtcNow, Serilog.Events.LogEventLevel.Error, ex,
        new Serilog.Parsing.MessageTemplateParser().Parse("An exception occurred in the database while saving changes"),
        [new Serilog.Events.LogEventProperty("SourceContext", new Serilog.Events.ScalarValue("Microsoft.EntityFrameworkCore.Update"))]);

    [Fact]
    public void Abgefangene_Kollisionen_von_EF_nicht_ins_Journal_echte_Fehler_schon()
    {
        Assert.False(FehlerProtokollSink.Relevant(EfFehler(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("0 Zeilen"))));
        Assert.True(FehlerProtokollSink.Relevant(EfFehler(new Microsoft.EntityFrameworkCore.DbUpdateException("Spalte zu lang", new InvalidOperationException("x")))));
    }

    [Fact]
    public void Zeitstempel_kommt_von_der_App_Uhr_nicht_vom_Log_Ereignis()
    {
        // Klicktest 21.11.2026 mit Test-Uhr: Einträge trugen die Serilog-Zeit; der Aufräumdienst rechnet aber mit der App-Uhr.
        var puffer = new FehlerProtokollPuffer();
        var sink = new FehlerProtokollSink(puffer, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        sink.Emit(new Serilog.Events.LogEvent(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), Serilog.Events.LogEventLevel.Error,
            new InvalidOperationException("x"), new Serilog.Parsing.MessageTemplateParser().Parse("Fehler"), []));
        Assert.True(puffer.Leser.TryRead(out var eintrag));
        Assert.True(Math.Abs((eintrag!.ZeitUtc - Skoulou.Core.Werkzeuge.AppUhr.UtcNow).TotalMinutes) < 1);
    }
}