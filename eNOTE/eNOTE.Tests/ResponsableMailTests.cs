using ENote.WebApp.Services.Mails;
using Xunit;

namespace ENote.Tests;

/// <summary>Testabend 18.11.2026: ein vom Admin angelegter Responsable bekam keine Mail; vom Responsable angelegte Besitzer ebenfalls nicht.</summary>
public class ResponsableMailTests
{
    [Fact]
    public void Responsable_Mail_nennt_Identifiant_und_Passwort_Link_und_ist_immer_aktiv()
    {
        var html = TabloSuiteMails.ResponsableKontoErstellt("Kodjo", "kodjo.resp", "https://enote.tablosuite.com", "https://enote.tablosuite.com/auth/reinitialiser?compte=1&code=x");
        var text = EmailService.AlsText(html);
        Assert.Contains("kodjo.resp", text);
        Assert.Contains("Choisir mon mot de passe", text);
        Assert.Contains("Mes établissements", text);
        Assert.True(MailArten.Finde("compte-responsable")?.Pflicht);
    }

    [Fact]
    public void Vom_Responsable_angelegter_Besitzer_erfaehrt_wer_das_Konto_erstellt_hat()
    {
        var html = TabloSuiteMails.KontoErstellt("Ama", "ama", "Salon A", "https://enote.tablosuite.com", null, erstelltVon: "Paul Responsable");
        var text = EmailService.AlsText(html);
        Assert.Contains("Paul Responsable", text);
        Assert.Contains("communiqué par votre responsable", text);
        Assert.DoesNotContain("L'équipe TabloSuite a créé", text);
    }
}
