using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace ENote.Tests.Simulation;

/// <summary>
/// eNOTE-Daten-Simulation, nur mit <c>ENOTE_SIM=1</c>. Optional: <c>ENOTE_SIM_FAKTOR</c> (Verkäufe je Tag, Standard 1),
/// <c>ENOTE_SIM_BEHALTEN=ENOTEDB_SIM</c> (Datenbank für die Klick-Simulation behalten). Ergebnis zusätzlich in
/// %TEMP%\sim\enote\simulation-ergebnis.txt (ohne Passwörter).
/// </summary>
public class SimulationTests(ITestOutputHelper ausgabe)
{
    /// <summary>S2: Eine behaltene SIM-DB läuft in der echten App – Belegnamen müssen deren Regeln erfüllen (sonst 500 auf /ausgaben).</summary>
    [Fact]
    public async Task Medienspeicher_der_Simulation_vergibt_Namen_nach_den_Regeln_der_App()
    {
        var speicher = new SimMedienSpeicher();
        var salonId = Guid.NewGuid();
        var r = await speicher.SaveAsync(new ENote.Core.Services.Storage.MediaSaveRequest
        {
            SalonId = salonId, DateiName = "recu.pdf", ContentType = "application/pdf", Inhalt = new MemoryStream("%PDF-1.4"u8.ToArray()),
        });
        Assert.True(ENote.WebApp.Services.Storage.MedienDateien.IstGueltigerName(r.BlobName), r.BlobName);
        Assert.StartsWith(salonId.ToString("D") + "/", r.BlobName);
        Assert.Equal(ENote.WebApp.Services.Storage.MedienDateien.Url(r.BlobName!), speicher.GetPublicUrl(r.BlobName!));
    }

    [EnoteSimFact]
    public async Task Achtzehn_Monate_Betrieb_mit_ueber_dreissig_Laeden_halten_alle_Invarianten()
    {
        var faktor = double.TryParse(Environment.GetEnvironmentVariable("ENOTE_SIM_FAKTOR"), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1.0;
        var datenbank = new SimDatenbank();
        await datenbank.AnlegenAsync();
        var gesamt = Stopwatch.StartNew();
        var text = new StringBuilder();
        var ordner = Path.Combine(Path.GetTempPath(), "sim", "enote");
        Directory.CreateDirectory(ordner);
        File.Delete(Path.Combine(ordner, "simulation-fortschritt.txt"));
        try
        {
            await using var dienste = new SimDienste(datenbank.Verbindung, "Adm" + Guid.NewGuid().ToString("N")[..12] + "9x");
            var sim = new Simulation(dienste, faktor);
            await sim.AusfuehrenAsync();
            var laufzeit = gesamt.Elapsed;
            await sim.InvariantenAsync();
            await sim.ZeitenMessenAsync();

            var mengen = await dienste.MitDbAsync(async db => new (string, int)[]
            {
                ("Läden", await db.Salons.CountAsync()), ("Konten", await db.Users.CountAsync()),
                ("Employés", await db.UserRoles.CountAsync(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == ENote.Core.DataModels.Identity.AppRollen.Kassierer))),
                ("Leistungen", await db.Leistungen.CountAsync()), ("Kunden", await db.Kunden.CountAsync()),
                ("Kassensitzungen", await db.KassenSitzungen.CountAsync()), ("Verkäufe", await db.Verkaeufe.CountAsync(v => v.StornoVonVerkaufId == null)),
                ("Erstattungen", await db.Verkaeufe.CountAsync(v => v.StornoVonVerkaufId != null)), ("Zahlungsanteile", await db.VerkaufZahlungen.CountAsync()),
                ("Besuche", await db.KundenBesuche.CountAsync()), ("Notizen", await db.KundenNotizen.CountAsync()),
                ("Aufenthaltszeilen", await db.AufenthaltsPositionen.CountAsync()), ("Geparkte Tickets", await db.GeparkteTickets.CountAsync()),
                ("Ausgaben", await db.Ausgaben.CountAsync()), ("Geldbewegungen", await db.GeldBewegungen.CountAsync()), ("Lagerabgänge", await db.LagerAbgaenge.CountAsync()),
                ("Abo-Rechnungen", await db.NutzungsAbos.CountAsync()), ("PO-Eingänge", await db.PoZahlungseingaenge.CountAsync()),
                ("Mails (Warteschlange)", await db.MailNachrichten.CountAsync()), ("Annonces", await db.Ankuendigungen.CountAsync()),
            });

            text.AppendLine($"eNOTE-Simulation {DateTime.Now:dd/MM/yyyy HH:mm} – DB {datenbank.Name}{(datenbank.Behalten ? " (behalten)" : "")}, Faktor {faktor}");
            text.AppendLine($"Zeitraum {Simulation.Beginn:dd/MM/yyyy} – {Simulation.Ende:dd/MM/yyyy}; Laufzeit Ablauf {laufzeit:hh\\:mm\\:ss}, gesamt {gesamt.Elapsed:hh\\:mm\\:ss}");
            text.AppendLine("Mengen: " + string.Join(", ", mengen.Select(m => $"{m.Item1} {m.Item2:N0}")));
            text.AppendLine("Zähler: " + string.Join(", ", sim.Zaehler.OrderBy(z => z.Key, StringComparer.Ordinal).Select(z => $"{z.Key} {z.Value:N0}")));
            text.AppendLine($"Zeitstempel aus der Simulationsuhr ersetzt: {dienste.Interceptor.Ersetzt:N0}");
            text.AppendLine("Läden: " + string.Join("; ", sim.Laeden.Where(l => l.Eingerichtet).Select(l =>
                $"{l.Name} ({l.Land ?? "Togo*"}) {l.Verkaeufe:N0} V/{l.Umsatz:N0} F{(l.KasseGesperrt > 0 ? $", {l.KasseGesperrt} Tage gesperrt" : "")}")));
            text.AppendLine("Geprüft:");
            foreach (var g in sim.Geprueft) text.AppendLine("  - " + g);
            text.AppendLine("Antwortzeiten (Median von 3, ms):");
            foreach (var (liste, ms) in sim.Zeiten) text.AppendLine($"  {(ms > 500 ? "!!" : "  ")} {liste}: {ms:0}");
            text.AppendLine("Ablauf:");
            foreach (var a in sim.Ablauf) text.AppendLine("  " + a);
            text.AppendLine($"Log-Warnungen ({dienste.Logs.Warnungen.Count} Arten):");
            foreach (var w in dienste.Logs.Warnungen.OrderByDescending(w => w.Value).Take(15)) text.AppendLine($"  {w.Value}× {w.Key}");
            text.AppendLine($"Log-Fehler ({dienste.Logs.Fehler.Count}):");
            foreach (var e in dienste.Logs.Fehler.Take(30)) text.AppendLine("  " + e);
            text.AppendLine($"Befunde ({sim.Fehler.Count}):");
            foreach (var e in sim.Fehler.Take(200)) text.AppendLine("  " + e);
            text.AppendLine($"Invarianten verletzt ({sim.Verletzungen.Count}):");
            foreach (var v in sim.Verletzungen.Take(200)) text.AppendLine("  " + v);
            Schreiben(text.ToString());

            Assert.True(sim.Fehler.IsEmpty, $"{sim.Fehler.Count} Befund(e), z. B.:\n" + string.Join("\n", sim.Fehler.Take(15)));
            Assert.True(sim.Verletzungen.Count == 0, $"{sim.Verletzungen.Count} Invariante(n) verletzt, z. B.:\n" + string.Join("\n", sim.Verletzungen.Take(15)));
            Assert.True(dienste.Logs.Fehler.IsEmpty, $"{dienste.Logs.Fehler.Count} Fehler im Log, z. B.:\n" + string.Join("\n", dienste.Logs.Fehler.Take(10)));
            Assert.True(sim.Laeden.Count(l => l.Eingerichtet) >= 30, "Mindestens 30 eingerichtete Läden.");
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

    /// <summary>Nur die Antwortzeiten auf einer vorhandenen Simulations-DB messen (ENOTE_SIM_MESSEN=ENOTEDB_SIM).</summary>
    [EnoteSimFact]
    public async Task Antwortzeiten_auf_vorhandener_Simulationsdatenbank()
    {
        var name = Environment.GetEnvironmentVariable("ENOTE_SIM_MESSEN");
        if (string.IsNullOrWhiteSpace(name)) return;
        var datenbank = new SimDatenbank(name);
        await using var dienste = new SimDienste(datenbank.Verbindung, "unbenutzt1");
        dienste.Uhr.Global = new DateTime(2026, 11, 13, 21, 0, 0, DateTimeKind.Utc);
        var sim = new Simulation(dienste, 1);
        await sim.ZeitenMessenAsync();
        var text = new StringBuilder($"Antwortzeiten auf {name} (Median von 3, ms):" + Environment.NewLine);
        foreach (var (liste, ms) in sim.Zeiten) text.AppendLine($"  {(ms > 500 ? "!!" : "  ")} {liste}: {ms:0}");
        foreach (var e in sim.Fehler) text.AppendLine("  " + e);
        ausgabe.WriteLine(text.ToString());
        var ordner = Path.Combine(Path.GetTempPath(), "sim", "enote");
        Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner, "antwortzeiten.txt"), text.ToString());
        Assert.True(sim.Fehler.IsEmpty, string.Join(Environment.NewLine, sim.Fehler));
    }

    private void Schreiben(string text)
    {
        ausgabe.WriteLine(text);
        var ordner = Path.Combine(Path.GetTempPath(), "sim", "enote");
        Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner, "simulation-ergebnis.txt"), text);
    }
}
