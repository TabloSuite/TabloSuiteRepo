using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Skoulou.Tests.Simulation;

/// <summary>
/// Simulation (Bauplan §9.4), nur mit <c>SKOULOU_SIM=1</c>. Optional: <c>SKOULOU_SIM_FAKTOR</c> (Klassengröße, Standard 1),
/// <c>SKOULOU_SIM_BEHALTEN=SkoulouDB_SIM</c> (Datenbank für den Browser-Rundgang behalten). Ergebnis zusätzlich in
/// %TEMP%\skoulou-neu\E7\simulation-ergebnis.txt (ohne Passwörter).
/// </summary>
public class SimulationTests(ITestOutputHelper ausgabe)
{
    [SimFact]
    public async Task Drei_Schuljahre_mit_ueber_zwanzig_Schulen_halten_alle_Invarianten()
    {
        var faktor = double.TryParse(Environment.GetEnvironmentVariable("SKOULOU_SIM_FAKTOR"), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1.0;
        var datenbank = new SimDatenbank();
        await datenbank.AnlegenAsync();
        var gesamt = Stopwatch.StartNew();
        var text = new StringBuilder();
        try
        {
            await using var dienste = new SimDienste(datenbank.Verbindung, new TestZeit(), "admin@skoulou-sim.tg", "Admin" + Guid.NewGuid().ToString("N")[..12] + "9");
            var sim = new Simulation(dienste, faktor);
            await sim.AusfuehrenAsync();
            var laufzeit = gesamt.Elapsed;
            await sim.InvariantenAsync();
            await sim.ZeitenMessenAsync();

            var mengen = await dienste.MitDbAsync(async db => new (string, int)[]
            {
                ("Schulen", await db.Schulen.CountAsync()), ("Konten", await db.Users.CountAsync()),
                ("Schüler", await db.Users.CountAsync(k => k.Rolle == Skoulou.Core.Rechte.Rollen.Schueler)),
                ("Eltern", await db.Users.CountAsync(k => k.Rolle == Skoulou.Core.Rechte.Rollen.Eltern)),
                ("Einschreibungen", await db.Einschreibungen.CountAsync()), ("Klassen", await db.Klassen.CountAsync()),
                ("Noten", await db.Noten.CountAsync()), ("Bulletins", await db.Bulletins.CountAsync()),
                ("Zahlungen", await db.Zahlungen.CountAsync()), ("Buchungen", await db.Buchungen.CountAsync()),
                ("Plattformrechnungen", await db.Plattformrechnungen.CountAsync()), ("Rechnungspositionen", await db.Rechnungspositionen.CountAsync()),
                ("Responsable-Anteile", await db.ResponsableAnteile.CountAsync()), ("Versuche", await db.Versuche.CountAsync()),
                ("Mails (Outbox)", await db.MailAusgaenge.CountAsync()), ("Audit", await db.AuditEintraege.CountAsync())
            });

            text.AppendLine($"Simulation {DateTime.Now:dd/MM/yyyy HH:mm} – DB {datenbank.Name}{(datenbank.Behalten ? " (behalten)" : "")}, Faktor {faktor}");
            text.AppendLine($"Laufzeit Ablauf {laufzeit:hh\\:mm\\:ss}, gesamt {gesamt.Elapsed:hh\\:mm\\:ss}");
            text.AppendLine("Mengen: " + string.Join(", ", mengen.Select(m => $"{m.Item1} {m.Item2:N0}")));
            text.AppendLine("Zähler: " + string.Join(", ", sim.Zaehler.OrderBy(z => z.Key, StringComparer.Ordinal).Select(z => $"{z.Key} {z.Value:N0}")));
            text.AppendLine("Geprüft:");
            foreach (var g in sim.Geprueft) text.AppendLine("  - " + g);
            text.AppendLine("Antwortzeiten (Median von 3, ms):");
            foreach (var (liste, ms) in sim.Zeiten) text.AppendLine($"  {(ms > 500 ? "!!" : "  ")} {liste}: {ms:0}");
            text.AppendLine("Ablauf:");
            foreach (var a in sim.Ablauf) text.AppendLine("  " + a);
            text.AppendLine($"Befunde ({sim.Fehler.Count}):");
            foreach (var e in sim.Fehler.Take(150)) text.AppendLine("  " + e);
            text.AppendLine($"Invarianten verletzt ({sim.Verletzungen.Count}):");
            foreach (var v in sim.Verletzungen.Take(150)) text.AppendLine("  " + v);
            Schreiben(text.ToString());

            Assert.True(sim.Fehler.IsEmpty, $"{sim.Fehler.Count} Befund(e), z. B.:\n" + string.Join("\n", sim.Fehler.Take(15)));
            Assert.True(sim.Verletzungen.Count == 0, $"{sim.Verletzungen.Count} Invariante(n) verletzt, z. B.:\n" + string.Join("\n", sim.Verletzungen.Take(15)));
            Assert.True(sim.Schulen.Count >= 20, "Mindestens 20 Schulen.");
        }
        catch (Exception ex)
        {
            text.AppendLine("ABBRUCH: " + ex);
            Schreiben(text.ToString());
            throw;
        }
        finally
        {
            if (!datenbank.Behalten) await datenbank.LoeschenAsync();
        }
    }

    /// <summary>Nur die Antwortzeiten auf einer vorhandenen Simulations-DB messen (SKOULOU_SIM_MESSEN=SkoulouDB_SIM).</summary>
    [SimFact]
    public async Task Antwortzeiten_auf_vorhandener_Simulationsdatenbank()
    {
        var name = Environment.GetEnvironmentVariable("SKOULOU_SIM_MESSEN");
        if (string.IsNullOrWhiteSpace(name)) return;
        var verbindung = $"Server=localhost;Database={name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True";
        await using var dienste = new SimDienste(verbindung, new TestZeit(), "admin@skoulou-sim.tg", "unbenutzt");
        var sim = new Simulation(dienste, 1);
        await sim.ZeitenMessenAsync();
        var text = new StringBuilder($"Antwortzeiten auf {name} (Median von 3, ms):" + Environment.NewLine);
        foreach (var (liste, ms) in sim.Zeiten) text.AppendLine($"  {(ms > 500 ? "!!" : "  ")} {liste}: {ms:0}");
        foreach (var e in sim.Fehler) text.AppendLine("  " + e);
        ausgabe.WriteLine(text.ToString());
        var ordner = Path.Combine(Path.GetTempPath(), "skoulou-neu", "E7");
        Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner, "antwortzeiten.txt"), text.ToString());
        Assert.True(sim.Fehler.IsEmpty, string.Join(Environment.NewLine, sim.Fehler));
    }

    private void Schreiben(string text)
    {
        ausgabe.WriteLine(text);
        var ordner = Path.Combine(Path.GetTempPath(), "skoulou-neu", "E7");
        Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner, "simulation-ergebnis.txt"), text);
    }
}
