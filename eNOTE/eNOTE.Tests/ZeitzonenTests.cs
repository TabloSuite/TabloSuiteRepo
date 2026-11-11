using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>Ortszeit je Laden: Togo UTC+0, Bénin/Cameroun UTC+1, unbekannt = Lomé.</summary>
public class ZeitzonenTests
{
    private static readonly DateTime Utc = new(2026, 11, 11, 23, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Togo", 0)]
    [InlineData("Sénégal", 0)]
    [InlineData("Bénin", 1)]
    [InlineData("Cameroun", 1)]
    [InlineData("Gabon", 1)]
    [InlineData("RDC (Congo-Kinshasa)", 1)]
    [InlineData("Kenya", 3)]
    [InlineData(null, 0)]
    [InlineData("Atlantis", 0)]
    public void Versatz_Je_Land(string? land, int stunden)
        => Assert.Equal(TimeSpan.FromHours(stunden), Zeitzonen.Versatz(Zeitzonen.Fuer(land), Utc));

    [Fact]
    public void Abends_Ist_In_Kamerun_Schon_Der_Naechste_Tag()
    {
        var douala = Zeitzonen.Fuer("Cameroun");
        Assert.Equal(new DateTime(2026, 11, 12, 0, 30, 0), Utc.ImLaden(douala));
        Assert.Equal(new DateTime(2026, 11, 12), Zeitzonen.Heute(douala, Utc));
        Assert.Equal(new DateTime(2026, 11, 11), Zeitzonen.Heute(Zeitzonen.Fuer("Togo"), Utc));
    }

    [Fact]
    public void Kalendertag_Im_Laden_Als_Utc_Bereich()
    {
        var (von, bis) = Zeitzonen.TageInUtc(new DateTime(2026, 11, 11), new DateTime(2026, 11, 11), Zeitzonen.Fuer("Cameroun"));
        Assert.Equal(new DateTime(2026, 11, 10, 23, 0, 0), von);
        Assert.Equal(new DateTime(2026, 11, 11, 23, 0, 0).AddTicks(-1), bis);
        var (vonTogo, _) = Zeitzonen.TageInUtc(new DateTime(2026, 11, 11), new DateTime(2026, 11, 11), Zeitzonen.Fuer("Togo"));
        Assert.Equal(new DateTime(2026, 11, 11), vonTogo);
    }

    [Fact]
    public void Alle_Zugeordneten_Laender_Stehen_In_Der_Laenderliste()
    {
        foreach (var land in new[] { "Togo", "Bénin", "Cameroun", "Sénégal", "Côte d'Ivoire", "Burkina Faso", "Mali", "Niger", "Gabon",
                     "Congo", "RDC (Congo-Kinshasa)", "Tchad", "République centrafricaine", "Guinée équatoriale", "Guinée" })
            Assert.Contains(land, Laender.Alle);
    }

    [Fact]
    public async Task Tagesumsatz_Zaehlt_Nach_Dem_Tag_Im_Laden()
    {
        var f = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon Douala", Land = "Cameroun", IstAktiv = true, IstFreigegeben = true };
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            var sitzung = new KassenSitzung { SalonId = salon.Id, KassiererId = Guid.NewGuid(), Status = KassenSitzungStatus.Offen };
            db.KassenSitzungen.Add(sitzung);
            // 23:30 UTC = 00:30 am 12.11. in Douala
            db.Verkaeufe.Add(new Verkauf { KassenSitzungId = sitzung.Id, SalonId = salon.Id, ZeitUtc = Utc, GesamtSumme = 3000, ZahlungsArt = ZahlungsArt.Bar });
            db.Verkaeufe.Add(new Verkauf { KassenSitzungId = sitzung.Id, SalonId = salon.Id, ZeitUtc = Utc.AddHours(-12), GesamtSumme = 1000, ZahlungsArt = ZahlungsArt.Bar });
            await db.SaveChangesAsync();
        }
        var zone = Zeitzonen.Fuer(salon.Land);
        var (von, bis) = Zeitzonen.TageInUtc(new DateTime(2026, 11, 11), new DateTime(2026, 11, 12), zone);
        var tage = await new FinanzService(f).GetTagesUmsaetzeImZeitraumAsync(salon.Id, von, bis, versatz: Zeitzonen.Versatz(zone, Utc));
        Assert.Equal([(new DateTime(2026, 11, 12), 3000m), (new DateTime(2026, 11, 11), 1000m)], tage.Select(t => (t.Datum, t.Gesamt)));
    }
}
