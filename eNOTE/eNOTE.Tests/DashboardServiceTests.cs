using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

public class DashboardServiceTests
{
    private static readonly DateTime Heute = new(2026, 11, 10, 15, 0, 0);

    [Fact]
    public async Task Kennzahlen_Nur_Fuer_Besitzer_Oder_Finanzrecht_Und_Korrekt_Aufgeteilt()
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon", IstAktiv = true, IstFreigegeben = true };
        var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), FullName = "Awa" };
        var kassiererMitRecht = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi" };
        var shampoo = new Leistung { SalonId = salon.Id, Name = "Shampoing", Preis = 4000, IstAktiv = true, HatStueckzahl = true, Stueckzahl = 2, MindestbestandWarnung = 5 };
        var sitzung = new KassenSitzung { SalonId = salon.Id, KassiererId = kassierer.Id, Startbestand = 10000, Status = KassenSitzungStatus.Offen };

        Verkauf V(DateTime zeit, params (ZahlungsArt Art, decimal Betrag)[] anteile)
        {
            var v = new Verkauf { SalonId = salon.Id, KassenSitzungId = sitzung.Id, ZeitUtc = zeit, GesamtSumme = anteile.Sum(a => a.Betrag) };
            foreach (var (art, betrag) in anteile)
                v.Zahlungen.Add(new VerkaufZahlung { Art = art, Betrag = betrag, AnbieterName = art == ZahlungsArt.MobileMoney ? "Flooz" : null });
            v.ZahlungsArt = KassenRechner.Zusammenfassen(anteile.Select(a => a.Art));
            v.Positionen.Add(new VerkaufsPosition { LeistungId = shampoo.Id, LeistungName = "Shampoing", Menge = 1, EinzelPreis = v.GesamtSumme, ZeilenSumme = v.GesamtSumme });
            return v;
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.Roles.Add(rolle);
            db.Users.AddRange(besitzer, kassierer, kassiererMitRecht);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.UserSalons.AddRange(
                new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id },
                new UserSalon { AppUserId = kassierer.Id, SalonId = salon.Id },
                new UserSalon { AppUserId = kassiererMitRecht.Id, SalonId = salon.Id, KannFinanzenSehen = true });
            db.Leistungen.Add(shampoo);
            db.KassenSitzungen.Add(sitzung);
            db.Verkaeufe.AddRange(
                V(Heute.AddHours(-2), (ZahlungsArt.Bar, 3000), (ZahlungsArt.MobileMoney, 5000)),
                V(Heute.AddHours(-1), (ZahlungsArt.Karte, 2000)),
                V(Heute.AddDays(-1), (ZahlungsArt.Bar, 6000)));
            await db.SaveChangesAsync();
        }

        var service = new DashboardService(factory);

        Assert.Null(await service.LadeAsync(kassierer.Id, salon.Id, Heute, TimeSpan.Zero));
        Assert.NotNull(await service.LadeAsync(kassiererMitRecht.Id, salon.Id, Heute, TimeSpan.Zero));

        var d = (await service.LadeAsync(besitzer.Id, salon.Id, Heute, TimeSpan.Zero))!;
        Assert.Equal((10000m, 2), (d.UmsatzHeute, d.VerkaeufeHeute));
        Assert.Equal(5000m, d.PanierMoyen);
        Assert.Equal(6000m, d.DurchschnittTag);
        Assert.Equal((9000m, 2000m, 5000m), (d.Bar, d.Karte, d.MobileMoney));
        Assert.Equal(DashboardService.VerlaufTage, d.Verlauf.Count);
        Assert.Equal(10000m, d.Verlauf[^1].Umsatz);
        Assert.Equal("Shampoing", Assert.Single(d.Lager).Name);
        Assert.Equal(19000m, d.Kasse!.BarSoll); // 10.000 Start + 3.000 + 6.000 Baranteile aller Verkäufe der offenen Sitzung
    }
}
