using SkoulouApp.Core.DataModels;
using SkoulouApp.Core.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für <see cref="LektionsFortschrittService"/> (In-Memory-Verwaltung des Lernfortschritts).
/// </summary>
public class LektionsFortschrittServiceTests
{
    [Fact]
    public async Task AlsAbgeschlossenMarkierenAsync_NeuerEintrag_WirdAlsAbgeschlossenGesetzt()
    {
        // Arrange
        var service = new LektionsFortschrittService();
        var schuelerId = Guid.NewGuid();
        var lektionId = Guid.NewGuid();

        // Act
        var fortschritt = await service.AlsAbgeschlossenMarkierenAsync(schuelerId, lektionId);

        // Assert
        Assert.True(fortschritt.IstAbgeschlossen);
        Assert.NotNull(fortschritt.AbgeschlossenAmUtc);
        Assert.Equal(schuelerId, fortschritt.SchuelerID);
        Assert.Equal(lektionId, fortschritt.LektionID);
    }

    [Fact]
    public async Task AlsAbgeschlossenMarkierenAsync_VorhandenerEintrag_WirdAktualisiert()
    {
        // Arrange
        var service = new LektionsFortschrittService();
        var schuelerId = Guid.NewGuid();
        var lektionId = Guid.NewGuid();
        await service.SpeichernAsync(new LektionsFortschrittModell
        {
            SchuelerID = schuelerId,
            LektionID = lektionId,
            IstAbgeschlossen = false
        });

        // Act
        var fortschritt = await service.AlsAbgeschlossenMarkierenAsync(schuelerId, lektionId);

        // Assert
        Assert.True(fortschritt.IstAbgeschlossen);
        var alle = await service.HoleNachSchuelerIDAsync(schuelerId);
        Assert.Single(alle);
    }

    [Fact]
    public async Task HoleNachSchuelerIDAsync_SortiertNachLetzterAktivitaetAbsteigend()
    {
        // Arrange
        var service = new LektionsFortschrittService();
        var schuelerId = Guid.NewGuid();

        var alt = await service.SpeichernAsync(new LektionsFortschrittModell
        {
            SchuelerID = schuelerId,
            LektionID = Guid.NewGuid()
        });
        alt.LetzteAktivitaetAmUtc = DateTime.UtcNow.AddHours(-2);
        await service.SpeichernAsync(alt);

        var neu = await service.SpeichernAsync(new LektionsFortschrittModell
        {
            SchuelerID = schuelerId,
            LektionID = Guid.NewGuid()
        });

        // Act
        var fortschritte = await service.HoleNachSchuelerIDAsync(schuelerId);

        // Assert
        Assert.Equal(2, fortschritte.Count);
        Assert.Equal(neu.Id, fortschritte[0].Id);
    }

    [Fact]
    public async Task HoleNachSchuelerUndLektionAsync_LiefertPassendenEintrag()
    {
        // Arrange
        var service = new LektionsFortschrittService();
        var schuelerId = Guid.NewGuid();
        var lektionId = Guid.NewGuid();
        await service.SpeichernAsync(new LektionsFortschrittModell
        {
            SchuelerID = schuelerId,
            LektionID = lektionId
        });

        // Act
        var fortschritt = await service.HoleNachSchuelerUndLektionAsync(schuelerId, lektionId);

        // Assert
        Assert.NotNull(fortschritt);
        Assert.Equal(lektionId, fortschritt!.LektionID);
    }
}
