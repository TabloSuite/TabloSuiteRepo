using System.Security.Claims;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Lernvideos: jede Rolle sieht ihre Videos, Schüler und Eltern keine fremden; das Werbevideo ist öffentlich.</summary>
public class VideoKatalogTests
{
    private static ClaimsPrincipal Nutzer(params string[] rollen)
        => new(new ClaimsIdentity(rollen.Select(r => new Claim(ClaimTypes.Role, r)), "Test"));

    private static readonly ClaimsPrincipal Anonym = new(new ClaimsIdentity());

    private static string[] Gruppen(ClaimsPrincipal user) => VideoKatalog.FuerNutzer(user).Select(g => g.Fuer).ToArray();

    [Fact]
    public void Direktion_Sieht_Alle_Gruppen_Eigene_Zuerst()
        => Assert.Equal([VideoKatalog.PourTous, VideoKatalog.Direction, VideoKatalog.Enseignant, VideoKatalog.Eleve, VideoKatalog.Parent],
            Gruppen(Nutzer(Rollen.SchulLeiter)));

    [Fact]
    public void Lehrkraft_Sieht_Eigene_Und_Schuelervideos()
        => Assert.Equal([VideoKatalog.PourTous, VideoKatalog.Enseignant, VideoKatalog.Eleve], Gruppen(Nutzer(Rollen.Lehrer)));

    [Fact]
    public void Schueler_Sieht_Nur_Eigene()
        => Assert.Equal([VideoKatalog.PourTous, VideoKatalog.Eleve], Gruppen(Nutzer(Rollen.Schueler)));

    [Fact]
    public void Eltern_Sehen_Nur_Eigene()
        => Assert.Equal([VideoKatalog.PourTous, VideoKatalog.Parent], Gruppen(Nutzer(Rollen.Eltern)));

    [Fact]
    public void Abruf_Folgt_Der_Sichtbarkeit()
    {
        Assert.True(VideoKatalog.DarfAbrufen("skoulou-eleve-apprendre.mp4", Nutzer(Rollen.Schueler)));
        Assert.False(VideoKatalog.DarfAbrufen("skoulou-direction-notes.mp4", Nutzer(Rollen.Schueler)));
        Assert.True(VideoKatalog.DarfAbrufen("skoulou-visite-guidee.mp4", Nutzer(Rollen.Eltern)));
        Assert.False(VideoKatalog.DarfAbrufen("skoulou-direction-demarrer.mp4", Nutzer(Rollen.SuperAdmin)));   // abgelöst
        Assert.False(VideoKatalog.DarfAbrufen("skoulou-enseignant-cours.mp4", Nutzer(Rollen.Eltern)));
        Assert.False(VideoKatalog.DarfAbrufen("skoulou-pourquoi.mp4", Anonym));
        Assert.False(VideoKatalog.DarfAbrufen("unbekannt.mp4", Nutzer(Rollen.SuperAdmin)));
    }

    [Fact]
    public void Werbevideo_Ist_Oeffentlich()
        => Assert.True(VideoKatalog.DarfAbrufen(VideoKatalog.Werbung, Anonym));

    [Fact]
    public void Dateinamen_Passen_Zum_Endpunkt()
        => Assert.All(VideoKatalog.Videos.Select(v => v.Datei).Append(VideoKatalog.Werbung),
            datei => Assert.Matches(@"^[a-z0-9-]+\.mp4$", datei));
}
