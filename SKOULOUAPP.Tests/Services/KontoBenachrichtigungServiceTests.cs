using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class KontoBenachrichtigungServiceTests
{
    private static KontoBenachrichtigungService Create(TestApplicationDbContextFactory factory, FakeEmailService email)
        => new(factory, email, NullLogger<KontoBenachrichtigungService>.Instance);

    [Fact]
    public async Task RegistrierungFreigegebenAsync_NurNachStatuswechsel_MitSkoulouVorlage()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "student", Email = "student@example.org", Vorname = "Afi", IsBlocked = true, IsSelfRegistered = true };
        await using (var db = await factory.CreateDbContextAsync()) { db.Users.Add(user); await db.SaveChangesAsync(); }
        var service = Create(factory, email);
        var vorher = await service.LadeEmpfaengerAsync(user.Id);
        await service.RegistrierungFreigegebenAsync(vorher);
        Assert.Empty(email.Gesendet);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.Users.SingleAsync(x => x.Id == user.Id);
            saved.IsBlocked = false;
            await db.SaveChangesAsync();
        }
        await service.RegistrierungFreigegebenAsync(vorher);
        var sent = Assert.Single(email.Gesendet);
        Assert.Equal(user.Email, sent.Recipient.Email);
        Assert.Contains("SKOULOU", sent.Message.HtmlBody);
        Assert.Contains("CONTACT", sent.Message.HtmlBody);
        Assert.Contains("Votre inscription a été validée", sent.Message.HtmlBody);
        Assert.False(string.IsNullOrWhiteSpace(sent.Message.TextBody));
        await service.RegistrierungFreigegebenAsync(await service.LadeEmpfaengerAsync(user.Id));
        Assert.Single(email.Gesendet);
    }

    [Fact]
    public async Task RegistrierungAbgelehntAsync_NurFuerOffeneSelbstregistrierung()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var service = Create(factory, email);
        await service.RegistrierungAbgelehntAsync(new(Guid.NewGuid(), "user@example.org", "Afi", false, true));
        Assert.Empty(email.Gesendet);
        await service.RegistrierungAbgelehntAsync(new(Guid.NewGuid(), "user@example.org", "Afi", true, true));
        Assert.Contains("n'a pas été acceptée", Assert.Single(email.Gesendet).Message.HtmlBody);
    }

    [Fact]
    public async Task AdresseModifieeAsync_BenachrichtigtAlteUndNeueAdresseNurBeiAenderung()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "user", Email = "old@example.org", Vorname = "Afi" };
        await using (var db = await factory.CreateDbContextAsync()) { db.Users.Add(user); await db.SaveChangesAsync(); }
        var service = Create(factory, email);
        var vorher = await service.LadeEmpfaengerAsync(user.Id);
        await service.AdresseModifieeAsync(vorher);
        Assert.Empty(email.Gesendet);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.Users.SingleAsync(x => x.Id == user.Id);
            saved.Email = "new@example.org";
            await db.SaveChangesAsync();
        }
        await service.AdresseModifieeAsync(vorher);
        Assert.Equal(["old@example.org", "new@example.org"], email.Gesendet.Select(item => item.Recipient.Email));
        Assert.All(email.Gesendet, item => Assert.Contains("Si vous n'êtes pas à l'origine", item.Message.HtmlBody));
    }

    [Fact]
    public async Task PaiementEnregistreAsync_SendetNurFuerGespeicherteBuchung()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var student = new AppUser { Id = Guid.NewGuid(), UserName = "student", Email = "student@example.org", Vorname = "Afi" };
        var zahlung = new RatenZahlung { Id = Guid.NewGuid(), StudentId = student.Id, Betrag = 2500m, ZahlungsArt = ZahlungsArt.Rate,
            Wirkungsdatum = new DateTime(2026, 8, 5), DurchlaufID = Guid.NewGuid(), ErfasstVonUserID = student.Id };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(student);
            db.RatenZahlungen.Add(zahlung);
            await db.SaveChangesAsync();
        }
        var service = Create(factory, email);
        await service.PaiementEnregistreAsync(zahlung.Id);
        Assert.Empty(email.Gesendet);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.RatenZahlungen.SingleAsync(item => item.Id == zahlung.Id);
            saved.FinanzBuchungID = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        await service.PaiementEnregistreAsync(zahlung.Id);
        var sent = Assert.Single(email.Gesendet);
        Assert.Equal(student.Email, sent.Recipient.Email);
        Assert.Contains("2.500 FCFA", sent.Message.HtmlBody);   // einheitlich wie eNOTE (SkoulouKultur)
        Assert.Contains("SKOULOU", sent.Message.HtmlBody);
        Assert.False(string.IsNullOrWhiteSpace(sent.Message.TextBody));
    }

    [Fact]
    public async Task VersandFehler_WirftKeineAusnahmeNachErfolgreicherAktion()
    {
        var service = Create(new TestApplicationDbContextFactory(), new FakeEmailService { WirftAusnahme = new HttpRequestException("Échec") });
        await service.RegistrierungAbgelehntAsync(new(Guid.NewGuid(), "user@example.org", "Afi", true, true));
    }

    [Fact]
    public async Task AboPaiementEnregistreAsync_NurBeiErstmaligerGebuchterZahlung()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var student = new AppUser { Id = Guid.NewGuid(), UserName = "student", Email = "student@example.org", Vorname = "Afi" };
        var klasseId = Guid.NewGuid();
        var enrollment = new LevelEnrollment { Id = Guid.NewGuid(), StudentId = student.Id, KlasseId = klasseId,
            IsPaid = true, PaidAtUtc = DateTime.UtcNow, PriceAtEnrollment = 12500m };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(student);
            db.LevelEnrollments.Add(enrollment);
            await db.SaveChangesAsync();
        }
        var service = Create(factory, email);
        Assert.True(await service.IstAboBezahltAsync(student.Id, klasseId));
        await service.AboPaiementEnregistreAsync(student.Id, klasseId, false);
        Assert.Empty(email.Gesendet);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var kategorie = new FinanzKategorie { Id = Guid.NewGuid(), Bezeichnung = "Abonnement", SystemCode = FinanzSystemCode.SkoulouAppAboZahlung };
            db.FinanzKategorien.Add(kategorie);
            db.FinanzBuchungen.Add(new FinanzBuchung { Id = Guid.NewGuid(), KategorieID = kategorie.Id, HerkunftReferenzID = enrollment.Id,
                Richtung = FinanzRichtung.Einnahme, Betrag = 12500m, SchulID = Guid.NewGuid(), ErfasstVonUserID = student.Id });
            db.Entry(db.FinanzBuchungen.Local.Single()).Property("Buchungsnummer").CurrentValue = "TEST-1";
            await db.SaveChangesAsync();
        }
        await service.AboPaiementEnregistreAsync(student.Id, klasseId, true);
        Assert.Empty(email.Gesendet);
        await service.AboPaiementEnregistreAsync(student.Id, klasseId, false);
        Assert.Contains("12", Assert.Single(email.Gesendet).Message.HtmlBody);
    }
}
