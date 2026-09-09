using HalloApp.Core.DataModels;
using HalloApp.Core.Services;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für <see cref="SchuelerProfilService"/> (In-Memory-Verwaltung von Schülerprofilen).
/// </summary>
public class SchuelerProfilServiceTests
{
    [Fact]
    public async Task SpeichernAsync_NeuesProfil_VergibtIdUndErstellungszeit()
    {
        // Arrange
        var service = new SchuelerProfilService();
        var profil = new SchuelerProfilModell
        {
            BenutzerID = Guid.NewGuid(),
            SchulID = Guid.NewGuid(),
            Anzeigename = "Anna"
        };

        // Act
        var gespeichert = await service.SpeichernAsync(profil);

        // Assert
        Assert.NotEqual(Guid.Empty, gespeichert.Id);
        Assert.True(gespeichert.ErstelltAmUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task HoleNachBenutzerIDAsync_LiefertGespeichertesProfil()
    {
        // Arrange
        var service = new SchuelerProfilService();
        var benutzerId = Guid.NewGuid();
        await service.SpeichernAsync(new SchuelerProfilModell
        {
            BenutzerID = benutzerId,
            SchulID = Guid.NewGuid(),
            Anzeigename = "Ben"
        });

        // Act
        var profil = await service.HoleNachBenutzerIDAsync(benutzerId);

        // Assert
        Assert.NotNull(profil);
        Assert.Equal("Ben", profil!.Anzeigename);
    }

    [Fact]
    public async Task HoleNachSchulIDAsync_LiefertProfileAlphabetischSortiert()
    {
        // Arrange
        var service = new SchuelerProfilService();
        var schulId = Guid.NewGuid();
        await service.SpeichernAsync(new SchuelerProfilModell { BenutzerID = Guid.NewGuid(), SchulID = schulId, Anzeigename = "Zoe" });
        await service.SpeichernAsync(new SchuelerProfilModell { BenutzerID = Guid.NewGuid(), SchulID = schulId, Anzeigename = "Alex" });
        await service.SpeichernAsync(new SchuelerProfilModell { BenutzerID = Guid.NewGuid(), SchulID = Guid.NewGuid(), Anzeigename = "Andere Schule" });

        // Act
        var profile = await service.HoleNachSchulIDAsync(schulId);

        // Assert
        Assert.Equal(2, profile.Count);
        Assert.Equal("Alex", profile[0].Anzeigename);
        Assert.Equal("Zoe", profile[1].Anzeigename);
    }

    [Fact]
    public async Task SpeichernAsync_VorhandenesProfil_AktualisiertUndSetztGeaendertAm()
    {
        // Arrange
        var service = new SchuelerProfilService();
        var profil = await service.SpeichernAsync(new SchuelerProfilModell
        {
            BenutzerID = Guid.NewGuid(),
            SchulID = Guid.NewGuid(),
            Anzeigename = "Original"
        });

        profil.Anzeigename = "Geändert";

        // Act
        var aktualisiert = await service.SpeichernAsync(profil);

        // Assert
        Assert.Equal("Geändert", aktualisiert.Anzeigename);
        Assert.NotNull(aktualisiert.GeaendertAmUtc);
    }
}
