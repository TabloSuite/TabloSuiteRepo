using Microsoft.EntityFrameworkCore;
using Serilog.Events;
using Serilog.Parsing;
using Skoulou.Core.Entitaeten;
using Skoulou.Tests.Hilfen;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>
/// Klicktest 21.11.2026: Doppeltipp auf « Retirer l'affectation » – beide Aufrufe lasen die Zeile, das zweite Löschen traf
/// 0 Zeilen → DbUpdateConcurrencyException → « Une erreur inattendue s'est produite », und der Absturz fehlte im Fehlerjournal.
/// </summary>
public class DoppeltippTests
{
    [Fact]
    public async Task Zweites_Loeschen_derselben_Zeile_gilt_als_erledigt()
    {
        var fabrik = TestDbContextFactory.Create();
        var id = Guid.NewGuid();
        await using (var db = await fabrik.CreateDbContextAsync())
        {
            db.Kategorien.Add(new Kategorie { Id = id, Name = "Cantine", Richtung = BuchungsRichtung.Recette });
            await db.SaveChangesAsync();
        }

        // Beide « Geräte » haben die Zeile geladen; das erste löscht sie.
        await using var erstes = await fabrik.CreateDbContextAsync();
        await using var zweites = await fabrik.CreateDbContextAsync();
        var a = await erstes.Kategorien.SingleAsync(k => k.Id == id);
        var b = await zweites.Kategorien.SingleAsync(k => k.Id == id);
        erstes.Kategorien.Remove(a);
        Assert.True(await erstes.LoeschenSpeichernAsync());

        zweites.Kategorien.Remove(b);
        Assert.False(await zweites.LoeschenSpeichernAsync());
        await using var pruefen = await fabrik.CreateDbContextAsync();
        Assert.False(await pruefen.Kategorien.AnyAsync(k => k.Id == id));
    }

    [Fact]
    public async Task Andere_Konflikte_bleiben_Fehler()
    {
        var fabrik = TestDbContextFactory.Create();
        var id = Guid.NewGuid();
        await using (var db = await fabrik.CreateDbContextAsync())
        {
            db.Kategorien.Add(new Kategorie { Id = id, Name = "Cantine", Richtung = BuchungsRichtung.Recette });
            await db.SaveChangesAsync();
        }
        await using var erstes = await fabrik.CreateDbContextAsync();
        await using var zweites = await fabrik.CreateDbContextAsync();
        var a = await erstes.Kategorien.SingleAsync(k => k.Id == id);
        var b = await zweites.Kategorien.SingleAsync(k => k.Id == id);
        erstes.Kategorien.Remove(a);
        await erstes.SaveChangesAsync();
        b.Name = "Cantine scolaire";   // Ändern einer gelöschten Zeile ist kein « schon erledigt »
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => zweites.LoeschenSpeichernAsync());
    }

    private static LogEvent Ereignis(LogEventLevel stufe, string quelle) => new(DateTimeOffset.UtcNow, stufe, new InvalidOperationException("x"),
        new MessageTemplateParser().Parse("Unhandled exception rendering component: {Message}"),
        [new LogEventProperty("SourceContext", new ScalarValue(quelle))]);

    [Fact]
    public void Absturz_hinter_der_ErrorBoundary_kommt_ins_Fehlerjournal()
    {
        Assert.True(FehlerProtokollSink.Relevant(Ereignis(LogEventLevel.Warning, "Microsoft.AspNetCore.Components.Web.ErrorBoundary")));
        Assert.True(FehlerProtokollSink.Relevant(Ereignis(LogEventLevel.Error, "Irgendwas")));
        Assert.False(FehlerProtokollSink.Relevant(Ereignis(LogEventLevel.Warning, "Skoulou.WebApp.Services.Infrastruktur.FehlerProtokollSchreiber")));
        Assert.False(FehlerProtokollSink.Relevant(Ereignis(LogEventLevel.Information, "Microsoft.AspNetCore.Components.Web.ErrorBoundary")));
    }
}
