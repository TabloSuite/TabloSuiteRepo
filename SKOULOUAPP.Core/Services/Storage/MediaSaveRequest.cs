namespace SkoulouApp.Core.Services.Storage;

/// <summary>
/// Anfrage zum Speichern einer Mediendatei (Bild oder Audio) einer Lektion.
/// </summary>
public class MediaSaveRequest
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public required string MediaType { get; init; }
    public required string AltText { get; init; }
    public required string LessonFolder { get; init; }
}
