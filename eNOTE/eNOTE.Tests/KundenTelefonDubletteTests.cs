using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: « +228 91 00 10 01 » war gesperrt, « 91 00 10 01 » (lokal geschrieben) legte dieselbe Kundin
/// ein zweites Mal an. Gleich ist jetzt auch, wenn die kürzere Nummer (mind. 8 Ziffern) das Ende der längeren ist.
/// </summary>
public class KundenTelefonDubletteTests
{
    private static async Task<(KundenService Dienst, Guid Laden, Guid Besitzer)> WeltAsync()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Tel " + Guid.NewGuid().ToString("N")[..6], IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Besitzer" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle);
            db.Users.Add(besitzer);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(laden);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = laden.Id });
            await db.SaveChangesAsync();
        }
        return (new KundenService(f, TimeProvider.System), laden.Id, besitzer.Id);
    }

    [Theory]
    [InlineData("+228 91 00 10 01", "91 00 10 01")]
    [InlineData("91 00 10 01", "+228 91 00 10 01")]
    [InlineData("+237 6 99 00 30 01", "699003001")]
    [InlineData("+228 91 00 10 01", "00228 91 00 10 01")]
    public async Task Lokal_Und_International_Geschrieben_Ist_Dieselbe_Nummer(string erste, string zweite)
    {
        var (dienst, laden, besitzer) = await WeltAsync();
        await dienst.LegeAnAsync(besitzer, laden, "Abla Ahadji", erste, true);
        var fehler = await Assert.ThrowsAsync<ArgumentException>(() => dienst.LegeAnAsync(besitzer, laden, "Abla bis", zweite, true));
        Assert.Contains("Abla Ahadji", fehler.Message);
    }

    [Fact]
    public async Task Verschiedene_Nummern_Bleiben_Erlaubt()
    {
        var (dienst, laden, besitzer) = await WeltAsync();
        await dienst.LegeAnAsync(besitzer, laden, "Abla Ahadji", "+228 91 00 10 01", true);
        await dienst.LegeAnAsync(besitzer, laden, "Délali Sossou", "+228 91 00 10 02", true);
        await dienst.LegeAnAsync(besitzer, laden, "Kurz", "10 01 99", true);   // zu kurz für den Endvergleich → nur exakt
    }
}
