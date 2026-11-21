using ENote.Core.DataModels.Katalog;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// F5 in der Kasse (21.11.2026): Das angefangene Ticket wird im Browser des Geräts gemerkt und nach dem Neuladen
/// wiederhergestellt – Preise aus dem Katalog, gelöschte Leistungen weg, Remise neu geprüft, nur in derselben Kassensitzung.
/// </summary>
public class TicketMerkerTests
{
    private static readonly Guid Sitzung = Guid.NewGuid();
    private static readonly Leistung Coupe = new() { Name = "Coupe", Preis = 2000m, IstAktiv = true };
    private static readonly Leistung Tresses = new() { Name = "Tresses", Preis = 5000m, IstAktiv = true };
    private static readonly Leistung Divers = new() { Name = "Divers", Preis = 0m, IstAktiv = true, IstStandardArtikel = true };

    private static TicketMerker.Gemerkt Ticket(Guid? rabatt = null, bool bedingung = false, params TicketMerker.Zeile[] zeilen)
        => new(TicketMerker.Version, Sitzung, null, rabatt, bedingung, zeilen);

    /// <summary>Wie im Browser: serialisieren und wieder lesen.</summary>
    private static TicketMerker.Gemerkt? UeberBrowser(TicketMerker.Gemerkt t) => TicketMerker.Lese(TicketMerker.Serialisiere(t));

    [Fact]
    public void Preise_kommen_beim_Wiederherstellen_aus_dem_Katalog()
    {
        var gemerkt = UeberBrowser(Ticket(zeilen: [new(Coupe.Id, 2, null, 1500m), new(Divers.Id, 1, 750m, 0m)]));
        var katalog = new[] { Coupe, Tresses, Divers };           // Coupe kostet inzwischen 2 000 statt 1 500

        var w = TicketMerker.Pruefe(gemerkt, Sitzung, katalog, []);

        Assert.NotNull(w);
        Assert.Equal(0, w.Ausgelassen);
        Assert.True(w.PreiseGeaendert);
        var coupe = Assert.Single(w.Zeilen, z => z.Leistung.Id == Coupe.Id);
        Assert.Equal((2, 2000m, (decimal?)null), (coupe.Menge, coupe.Leistung.Preis, coupe.FreierBetrag));
        Assert.Equal(750m, Assert.Single(w.Zeilen, z => z.Leistung.Id == Divers.Id).FreierBetrag);   // « Divers » behält seinen Betrag
    }

    [Fact]
    public void Geloeschte_oder_inaktive_Leistung_faellt_weg()
    {
        var alt = new Leistung { Name = "Ancienne", Preis = 900m, IstAktiv = false };
        var gemerkt = UeberBrowser(Ticket(zeilen: [new(Coupe.Id, 1, null, 2000m), new(alt.Id, 1, null, 900m), new(Guid.NewGuid(), 3, null, 100m)]));

        var w = TicketMerker.Pruefe(gemerkt, Sitzung, [Coupe, alt], []);

        Assert.NotNull(w);
        Assert.Equal(2, w.Ausgelassen);
        Assert.False(w.PreiseGeaendert);
        Assert.Equal([Coupe.Id], w.Zeilen.Select(z => z.Leistung.Id));

        // Nichts mehr verkäuflich: nichts anbieten.
        Assert.Null(TicketMerker.Pruefe(UeberBrowser(Ticket(zeilen: [new(alt.Id, 1, null, 900m)])), Sitzung, [Coupe, alt], []));
    }

    [Fact]
    public void Andere_Kassensitzung_bietet_nichts_an()
    {
        var gemerkt = UeberBrowser(Ticket(zeilen: [new(Coupe.Id, 1, null, 2000m)]));
        Assert.NotNull(TicketMerker.Pruefe(gemerkt, Sitzung, [Coupe], []));
        Assert.Null(TicketMerker.Pruefe(gemerkt, Guid.NewGuid(), [Coupe], []));   // Kasse abgeschlossen und neu geöffnet
        Assert.Null(TicketMerker.Pruefe(null, Sitzung, [Coupe], []));
    }

    [Fact]
    public void Remise_wird_neu_geprueft()
    {
        var aktiv = new Rabatt { Name = "Fidélité", IstAktiv = true, Bedingung = "Carte fidélité" };
        var inaktiv = new Rabatt { Name = "Été", IstAktiv = false };
        var nurTresses = new Rabatt { Name = "Tresses -10 %", IstAktiv = true, LeistungId = Tresses.Id };
        var zeilen = new TicketMerker.Zeile[] { new(Coupe.Id, 1, null, 2000m), new(Tresses.Id, 1, null, 5000m) };
        var rabatte = new[] { aktiv, inaktiv, nurTresses };

        var mit = TicketMerker.Pruefe(UeberBrowser(Ticket(aktiv.Id, true, zeilen)), Sitzung, [Coupe, Tresses], rabatte)!;
        Assert.Equal((aktiv.Id, true), (mit.RabattId, mit.RabattBedingungBestaetigt));

        Assert.Null(TicketMerker.Pruefe(UeberBrowser(Ticket(inaktiv.Id, false, zeilen)), Sitzung, [Coupe, Tresses], rabatte)!.RabattId);

        // Remise nur für « Tresses » – « Tresses » ist inzwischen gelöscht: Remise fällt mit weg.
        var ohneTresses = TicketMerker.Pruefe(UeberBrowser(Ticket(nurTresses.Id, false, zeilen)), Sitzung, [Coupe], rabatte)!;
        Assert.Equal((1, (Guid?)null, false), (ohneTresses.Ausgelassen, ohneTresses.RabattId, ohneTresses.RabattBedingungBestaetigt));
    }

    [Fact]
    public void Ungueltige_Eintraege_werden_verworfen()
    {
        Assert.Null(TicketMerker.Lese(null));
        Assert.Null(TicketMerker.Lese("pas du json"));
        Assert.Null(TicketMerker.Lese("""{"V":99,"S":"00000000-0000-0000-0000-000000000000","Z":[]}"""));

        // Freier Betrag nur beim Standard-Artikel, Menge mindestens 1.
        var w = TicketMerker.Pruefe(UeberBrowser(Ticket(zeilen: [new(Coupe.Id, 1, 10m, 2000m), new(Tresses.Id, 0, null, 5000m), new(Tresses.Id, 1, null, 5000m)])),
            Sitzung, [Coupe, Tresses], []);
        Assert.Equal((2, 1), (w!.Ausgelassen, w.Zeilen.Count));
    }

    [Fact]
    public void Kasse_merkt_nach_jedem_Rendern_und_ein_Schluessel_je_Laden()
    {
        var salon = Guid.NewGuid();
        Assert.Equal($"enote_ticket_{salon:N}", TicketMerker.Schluessel(salon));
        var seite = Path.Combine(RepoWurzel(), "eNOTE", "eNOTE.WebApp", "Components", "Pages");
        Assert.Contains("await TicketMerkerNachRenderAsync();", File.ReadAllText(Path.Combine(seite, "Caisse.razor.cs")));
    }

    private static string RepoWurzel([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(quelle)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eNOTE", "eNOTE.WebApp", "Program.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }
}
