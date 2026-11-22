using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: Paramètres speicherte ohne « maxlength » im Browser Telefon « abc », einen Slogan mit 500 und
/// eine Adresse mit 3.000 Zeichen (alles erscheint auf dem 80-mm-Bon). Der Server prüft jetzt selbst.
/// </summary>
public class SalonKontaktdatenTests
{
    [Theory]
    [InlineData("abc", null, null, "chiffres")]
    [InlineData("12 34", null, null, "incomplet")]
    [InlineData(null, 121, null, "Slogan")]
    [InlineData(null, null, 201, "Adresse")]
    public async Task Ungueltige_Kontaktdaten_Werden_Abgelehnt(string? telefon, int? sloganLaenge, int? adresseLaenge, string meldung)
    {
        var f = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Kontakt " + Guid.NewGuid().ToString("N")[..6], IstAktiv = true };
        await using (var db = await f.CreateDbContextAsync()) { db.Salons.Add(salon); await db.SaveChangesAsync(); }
        var dienst = new SalonService(f, null!, null!);
        var fehler = await Assert.ThrowsAsync<ArgumentException>(() => dienst.SetzeKontaktdatenAsync(salon.Id,
            sloganLaenge is int s ? new string('s', s) : null, telefon, adresseLaenge is int a ? new string('a', a) : null, null));
        Assert.Contains(meldung, fehler.Message);
        await using var db2 = await f.CreateDbContextAsync();
        var gespeichert = await db2.Salons.FindAsync(salon.Id);
        Assert.Null(gespeichert!.TelefonNummer);
        Assert.Null(gespeichert.Adresse);
    }

    [Fact]
    public async Task Gueltige_Kontaktdaten_Werden_Gespeichert()
    {
        var f = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Kontakt ok", IstAktiv = true };
        await using (var db = await f.CreateDbContextAsync()) { db.Salons.Add(salon); await db.SaveChangesAsync(); }
        Assert.True(await new SalonService(f, null!, null!).SetzeKontaktdatenAsync(salon.Id, new string('s', SalonService.SloganMax),
            "+228 90 11 22 01", new string('a', SalonService.AdresseMax), null));
        Assert.True(await new SalonService(f, null!, null!).SetzeKontaktdatenAsync(salon.Id, null, null, null, null));   // alles leeren erlaubt
    }
}
