using HalloApp.Core.DataModels;
using HalloApp.Core.Interfaces;

namespace HalloApp.Core.Services;

public class LektionsFortschrittService : ILektionsFortschrittService
{
    // In-memory list to store lesson progress. In a real application, this would typically be replaced with a database or other persistent storage.
    private readonly List<LektionsFortschrittModell> _fortschritte = [];

    public Task<LektionsFortschrittModell?> HoleNachSchuelerUndLektionAsync(Guid schuelerID, Guid lektionID, CancellationToken cancellationToken = default)
    {
        // Check for cancellation before proceeding. Z.B. if the operation is taking too long or the user navigates away from the page.
        cancellationToken.ThrowIfCancellationRequested();

        // Find the progress entry for the specified student and lesson in the list of progress entries.
        var fortschritt = _fortschritte.FirstOrDefault(eintrag =>
            eintrag.SchuelerID == schuelerID && eintrag.LektionID == lektionID);

        return Task.FromResult(fortschritt);
    }
    public Task<IReadOnlyList<LektionsFortschrittModell>> HoleNachSchuelerIDAsync(Guid schuelerID, CancellationToken cancellationToken = default)
    {
        // Check for cancellation before proceeding. Z.B. if the operation is taking too long or the user navigates away from the page.
        cancellationToken.ThrowIfCancellationRequested();

        // Filter the progress entries based on the provided student ID and order them by the last activity timestamp in descending order.
        IReadOnlyList<LektionsFortschrittModell> fortschritte = _fortschritte
            .Where(fortschritt => fortschritt.SchuelerID == schuelerID)
            .OrderByDescending(fortschritt => fortschritt.LetzteAktivitaetAmUtc)
            .ToList();

        return Task.FromResult(fortschritte);
    }
    public Task<LektionsFortschrittModell> AlsAbgeschlossenMarkierenAsync(Guid schuelerID, Guid lektionID, CancellationToken cancellationToken = default)
    {
        // Check for cancellation before proceeding. Z.B. if the operation is taking too long or the user navigates away from the page.
        cancellationToken.ThrowIfCancellationRequested();

        // Find the progress entry for the specified student and lesson in the list of progress entries.
        var fortschritt = _fortschritte.FirstOrDefault(eintrag =>
            eintrag.SchuelerID == schuelerID && eintrag.LektionID == lektionID);
        // If no progress entry exists, create a new one and add it to the list.
        if (fortschritt is null)
        {
            fortschritt = new LektionsFortschrittModell
            {
                Id = Guid.NewGuid(),
                SchuelerID = schuelerID,
                LektionID = lektionID,
                ErstelltAmUtc = DateTime.UtcNow
            };

            _fortschritte.Add(fortschritt);
        }
        // Mark the progress entry as completed and update the relevant timestamps.
        fortschritt.IstAbgeschlossen = true;
        fortschritt.AbgeschlossenAmUtc = DateTime.UtcNow;
        fortschritt.LetzteAktivitaetAmUtc = DateTime.UtcNow;
        fortschritt.GeaendertAmUtc = DateTime.UtcNow;

        return Task.FromResult(fortschritt);
    }
    public Task<LektionsFortschrittModell> SpeichernAsync(LektionsFortschrittModell fortschritt, CancellationToken cancellationToken = default)
    {
        // Check for cancellation before proceeding. Z.B. if the operation is taking too long or the user navigates away from the page.
        cancellationToken.ThrowIfCancellationRequested();

        // If the progress entry ID is empty, generate a new GUID and set the creation timestamp.
        if (fortschritt.Id == Guid.Empty)
        {
            fortschritt.Id = Guid.NewGuid();
            fortschritt.ErstelltAmUtc = DateTime.UtcNow;
        }
        // Update the last activity timestamp for the progress entry.
        fortschritt.LetzteAktivitaetAmUtc = DateTime.UtcNow;
        // Check if the progress entry already exists in the list. If it does, update it; otherwise, add it to the list.
        var vorhandenerFortschritt = _fortschritte.FirstOrDefault(eintrag => eintrag.Id == fortschritt.Id);
        if (vorhandenerFortschritt is null)
        {
            _fortschritte.Add(fortschritt);
        }
        else
        {
            var index = _fortschritte.IndexOf(vorhandenerFortschritt);
            fortschritt.GeaendertAmUtc = DateTime.UtcNow;
            _fortschritte[index] = fortschritt;
        }

        return Task.FromResult(fortschritt);
    }
}

