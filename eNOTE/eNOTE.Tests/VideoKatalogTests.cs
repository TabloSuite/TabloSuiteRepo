using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>Erklärvideos: Admin/Responsable alle, Besitzer seins + Kassierer, Kassierer nur seins.</summary>
public class VideoKatalogTests
{
    private static ClaimsPrincipal Mit(string? rolle) => rolle is null
        ? new ClaimsPrincipal(new ClaimsIdentity())
        : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, rolle)], "test"));

    [Theory]
    [InlineData(AppRollen.SuperAdmin, "enote-caissier.mp4,enote-proprietaire.mp4,enote-responsable.mp4,enote-admin.mp4")]
    [InlineData(AppRollen.ProduktOwner, "enote-caissier.mp4,enote-proprietaire.mp4,enote-responsable.mp4,enote-admin.mp4")]
    [InlineData(AppRollen.Besitzer, "enote-caissier.mp4,enote-proprietaire.mp4")]
    [InlineData(AppRollen.Kassierer, "enote-caissier.mp4")]
    public void Sichtbarkeit_Je_Rolle(string rolle, string erwartet)
    {
        var user = Mit(rolle);
        var sichtbar = VideoKatalog.Videos.Where(v => v.DarfSehen(user)).Select(v => v.Datei);
        Assert.Equal(erwartet.Split(','), sichtbar);
        foreach (var v in VideoKatalog.Videos)
            Assert.Equal(erwartet.Split(',').Contains(v.Datei), VideoKatalog.DarfAbrufen(v.Datei, user));
    }

    [Fact]
    public void Werbevideo_Oeffentlich_Erklaervideos_Nicht()
    {
        Assert.True(VideoKatalog.DarfAbrufen(VideoKatalog.Werbung, Mit(null)));
        Assert.False(VideoKatalog.DarfAbrufen("enote-caissier.mp4", Mit(null)));
        Assert.False(VideoKatalog.DarfAbrufen("unbekannt.mp4", Mit(AppRollen.SuperAdmin)));
    }
}
