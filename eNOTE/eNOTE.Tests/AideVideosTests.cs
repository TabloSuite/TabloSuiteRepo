using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// « Vidéos d'aide » (15.11.2026): am Handy sprang die Seite nach einem Video im Querformat nach oben; die Bereiche der
/// Nutzergruppen waren nur durch eine Überschrift getrennt.
/// </summary>
public class AideVideosTests
{
    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    [Fact]
    public void Seite_merkt_ihre_Position_solange_ein_Video_offen_ist()
    {
        var js = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "js", "enote.js"));
        Assert.Contains("window.enoteVideoPosition", js);
        Assert.Contains("'orientationchange'", js);   // Drehen ins Querformat (Vollbild am Handy)
        Assert.Contains("if (meiner !== lauf) return;", js);   // nächstes Video gleich danach: altes Aufräumen löscht nichts
        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "AideVideos.razor"));
        Assert.Contains("\"enoteVideoPosition.merken\"", seite);
        Assert.Contains("\"enoteVideoPosition.zurueck\"", seite);
        // 16.11.2026: Video im gewohnten Dialogfenster wie in SKOULOU (keine eigene Überlagerung, die Seite bleibt ruhig).
        Assert.Contains("<ModalDialog Open=\"offen is not null\"", seite);
        Assert.DoesNotContain("lp-video-overlay", seite);
    }

    [Fact]
    public void Jeder_Nutzerbereich_ist_ein_eigenes_farbiges_Feld()
    {
        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "AideVideos.razor"));
        Assert.Contains("class=\"vh-gruppe vh-rolle-@bereich.Klasse", seite);
        Assert.Contains("Votre rôle", seite);
        var css = File.ReadAllText(Path.Combine(WebApp(), "wwwroot", "app.css"));
        foreach (var rolle in new[] { "tous", "employe", "proprietaire", "responsable", "admin" })
            Assert.Contains($".vh-rolle-{rolle} {{ --vh-rgb:", css);
        Assert.Contains("border-top: 6px solid rgb(var(--vh-rgb));", css);
    }

    [Theory]
    [InlineData(AppRollen.SuperAdmin, "Administrateur")]
    [InlineData(AppRollen.ProduktOwner, "Responsable")]
    [InlineData(AppRollen.Besitzer, "Propriétaire")]
    [InlineData(AppRollen.Kassierer, "Employé")]
    public void Eigene_Gruppe_folgt_der_Rolle(string rolle, string erwartet)
    {
        var nutzer = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, rolle)], "Test"));
        Assert.Equal(erwartet, VideoKatalog.EigeneGruppe(nutzer));
        var gruppen = VideoKatalog.FuerNutzer(nutzer);
        Assert.Equal(VideoKatalog.Alle_, gruppen[0].Fuer);
        if (gruppen.Any(g => g.Fuer == erwartet)) Assert.Equal(erwartet, gruppen[1].Fuer);
    }
}
