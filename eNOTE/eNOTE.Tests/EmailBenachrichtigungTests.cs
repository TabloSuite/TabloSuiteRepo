using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Mails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

public class EmailBenachrichtigungTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private sealed record Basis(IDbContextFactory<ApplicationDbContext> F, Salon Salon, Uhr Uhr, MailBenachrichtigungen Mails);

    private static async Task<Basis> BasisAsync(DateTime? probeBis = null)
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon Test", IstAktiv = true, IstFreigegeben = true, ProbeBisUtc = probeBis };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Mensah", Vorname = "Ama", UserName = "ama", Email = "ama@exemple.com" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
            db.Roles.Add(rolle);
            db.Users.Add(besitzer);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(salon);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new EmailService(config, factory, NullLogger<EmailService>.Instance);
        var uhr = new Uhr(Start);
        return new Basis(factory, salon, uhr, new MailBenachrichtigungen(factory, email, config, NullLogger<MailBenachrichtigungen>.Instance, uhr));
    }

    private static async Task<List<string?>> KategorienAsync(Basis b)
    {
        await using var db = await b.F.CreateDbContextAsync();
        return await db.MailNachrichten.OrderBy(m => m.CreatedAtUtc).Select(m => m.Kategorie).ToListAsync();
    }

    private static async Task<NutzungsAbo> RechnungAsync(Basis b, DateTime erstellt)
    {
        var abo = new NutzungsAbo
        {
            SalonId = b.Salon.Id, Intervall = AboIntervall.Monatlich, PreisSnapshot = 5000, CreatedAtUtc = erstellt,
            ZeitraumVonUtc = erstellt.Date, ZeitraumBisUtc = erstellt.Date.AddMonths(1).AddTicks(-1),
            FaelligUtc = erstellt.Date, KassenSperreAbUtc = erstellt.Date.AddDays(15)
        };
        await using var db = await b.F.CreateDbContextAsync();
        db.NutzungsAbos.Add(abo);
        await db.SaveChangesAsync();
        return abo;
    }

    [Fact]
    public async Task Rechnung_Erinnerung_Und_Sperre_Werden_Je_Genau_Einmal_Versendet()
    {
        var b = await BasisAsync();
        await RechnungAsync(b, Start);

        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());          // idempotent

        b.Uhr.Jetzt = Start.Date.AddDays(12).AddHours(9);              // 3 Tage vor der Sperre
        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());

        b.Uhr.Jetzt = Start.Date.AddDays(15).AddHours(1);              // Sperre aktiv
        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());

        Assert.Equal(["facture", "rappel-paiement", "caisse-suspendue"], await KategorienAsync(b));
        await using var db = await b.F.CreateDbContextAsync();
        Assert.All(await db.MailNachrichten.ToListAsync(), m => Assert.Equal("ama@exemple.com", m.Empfaenger));
    }

    [Fact]
    public async Task Alte_Rechnungen_Loesen_Beim_Einschalten_Keine_Rechnungsmail_Aus()
    {
        var b = await BasisAsync();
        var alt = await RechnungAsync(b, Start.AddDays(-5));

        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());
        await using var db = await b.F.CreateDbContextAsync();
        Assert.NotNull((await db.NutzungsAbos.SingleAsync(a => a.Id == alt.Id)).RechnungMailUtc);
        Assert.Empty(await db.MailNachrichten.ToListAsync());
    }

    [Fact]
    public async Task Bezahlte_Rechnung_Bekommt_Keine_Erinnerung()
    {
        var b = await BasisAsync();
        var abo = await RechnungAsync(b, Start);
        await b.Mails.PruefeAboMailsAsync();

        await new AboService(b.F, b.Mails).SetzeZahlungsStatusAsync(abo.Id, AboZahlungStatus.Bestaetigt);
        b.Uhr.Jetzt = Start.Date.AddDays(16);
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());

        Assert.Equal(["facture", "paiement-confirme"], await KategorienAsync(b));
    }

    [Fact]
    public async Task Probe_Ende_Wird_Sieben_Tage_Vorher_Einmal_Angekuendigt()
    {
        var b = await BasisAsync(probeBis: Start.AddDays(10));
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());           // noch 10 Tage

        b.Uhr.Jetzt = Start.AddDays(4);                                 // noch 6 Tage
        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(["fin-essai"], await KategorienAsync(b));
    }

    [Fact]
    public async Task Ohne_Konfiguration_Bleiben_Mails_In_Der_Queue()
    {
        var b = await BasisAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new EmailService(config, b.F, NullLogger<EmailService>.Instance);

        Assert.False(email.IstKonfiguriert);
        Assert.False(await email.SendeAsync("test@exemple.com", "Test", "<p>Hallo</p>", "test"));
        Assert.False(await email.SendeAsync("pas-une-adresse", "Test", "<p>Hallo</p>", "test"));

        await using var db = await b.F.CreateDbContextAsync();
        var m = await db.MailNachrichten.SingleAsync();
        Assert.Equal((false, "test"), (m.IstGesendet, m.Kategorie));
    }

    [Fact]
    public void Konfiguration_Waehlt_Den_Versandweg()
    {
        EmailService Mit(params (string, string)[] werte) => new(
            new ConfigurationBuilder().AddInMemoryCollection(werte.ToDictionary(w => w.Item1, w => (string?)w.Item2)).Build(),
            null!, NullLogger<EmailService>.Instance);

        Assert.Equal("Aucun", Mit(("Mail:Provider", "Postmark")).Anbieter);              // Token fehlt
        Assert.Equal("Postmark", Mit(("Mail:Provider", "Postmark"), ("Mail:PostmarkServerToken", "x")).Anbieter);
        Assert.Equal("Smtp", Mit(("Mail:SmtpHost", "smtp.exemple.com")).Anbieter);
        Assert.Equal("Aucun", Mit().Anbieter);
    }

    [Fact]
    public void Textfassung_Behaelt_Links_Und_Entfernt_Html()
    {
        var text = EmailService.AlsText(TabloSuiteMails.RappelPaiement("Ama", "Salon Test", 5000, Start.AddDays(3), "https://enote.tablosuite.com/abonnement"));

        Assert.DoesNotContain("<", text);
        Assert.Contains("Bonjour Ama,", text);
        Assert.Contains("F CFA", text);
        Assert.Contains("(https://enote.tablosuite.com/abonnement)", text);
        Assert.Contains("Montant : ", text);                       // Label und Wert getrennt
        Assert.DoesNotContain(text.Split('\n'), zeile => zeile.Trim() == "T");   // keine Logo-Kachel
        Assert.False(text.StartsWith("La caisse sera suspendue"));  // keine versteckte Vorschauzeile
    }
}
