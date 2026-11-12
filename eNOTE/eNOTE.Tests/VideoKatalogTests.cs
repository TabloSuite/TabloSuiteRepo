using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>Lernvideos: « Pour bien commencer » für alle, mehrere Videos je Rolle, eigene Rolle zuerst.</summary>
public class VideoKatalogTests
{
    private static ClaimsPrincipal Mit(string? rolle) => rolle is null
        ? new ClaimsPrincipal(new ClaimsIdentity())
        : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, rolle)], "test"));

    [Theory]
    [InlineData(AppRollen.SuperAdmin, "Pour tous,Administrateur,Employé,Propriétaire,Responsable", 17)]
    [InlineData(AppRollen.ProduktOwner, "Pour tous,Responsable,Employé,Propriétaire,Administrateur", 16)]
    [InlineData(AppRollen.Besitzer, "Pour tous,Propriétaire,Employé", 12)]
    [InlineData(AppRollen.Kassierer, "Pour tous,Employé", 7)]
    public void Gruppen_Je_Rolle_Eigene_Zuerst(string rolle, string gruppen, int anzahl)
    {
        var user = Mit(rolle);
        var ergebnis = VideoKatalog.FuerNutzer(user);
        Assert.Equal(gruppen.Split(','), ergebnis.Select(g => g.Fuer));
        Assert.Equal(anzahl, ergebnis.Sum(g => g.Videos.Count));
        foreach (var v in VideoKatalog.Videos)
            Assert.Equal(ergebnis.Any(g => g.Videos.Contains(v)), VideoKatalog.DarfAbrufen(v.Datei, user));
    }

    [Fact]
    public void Kommunikation_Nur_Fuer_Admin_Pourquoi_Fuer_Alle()
    {
        Assert.False(VideoKatalog.DarfAbrufen("enote-admin-communication.mp4", Mit(AppRollen.ProduktOwner)));
        Assert.True(VideoKatalog.DarfAbrufen("enote-admin-communication.mp4", Mit(AppRollen.SuperAdmin)));
        Assert.True(VideoKatalog.DarfAbrufen("enote-premiers-pas.mp4", Mit(AppRollen.Kassierer)));
        Assert.True(VideoKatalog.DarfAbrufen("enote-visite-guidee.mp4", Mit(AppRollen.Kassierer)));
        Assert.False(VideoKatalog.DarfAbrufen("enote-proprietaire-catalogue.mp4", Mit(AppRollen.Kassierer)));
        Assert.True(VideoKatalog.DarfAbrufen("enote-employe-attente.mp4", Mit(AppRollen.Kassierer)));
    }

    [Fact]
    public void Werbevideo_Oeffentlich_Lernvideos_Nicht()
    {
        Assert.True(VideoKatalog.DarfAbrufen(VideoKatalog.Werbung, Mit(null)));
        Assert.False(VideoKatalog.DarfAbrufen("enote-premiers-pas.mp4", Mit(null)));
        Assert.False(VideoKatalog.DarfAbrufen("enote-caissier.mp4", Mit(AppRollen.SuperAdmin))); // alte Datei entfernt
        Assert.False(VideoKatalog.DarfAbrufen("enote-pourquoi.mp4", Mit(AppRollen.SuperAdmin)));  // abgelöst durch das Werbevideo
        Assert.False(VideoKatalog.DarfAbrufen("enote-proprietaire-demarrer.mp4", Mit(AppRollen.SuperAdmin)));
        Assert.False(VideoKatalog.DarfAbrufen("unbekannt.mp4", Mit(AppRollen.SuperAdmin)));
    }

    [Fact]
    public void Jedes_Video_Hat_Eindeutige_Datei_Und_Dauer()
    {
        Assert.Equal(VideoKatalog.Videos.Count, VideoKatalog.Videos.Select(v => v.Datei).Distinct().Count());
        Assert.All(VideoKatalog.Videos, v => Assert.Matches(@"^\d:\d\d$", v.Dauer));
    }
}
