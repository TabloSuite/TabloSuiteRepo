using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Services.Infrastruktur.Medien;

/// <summary>Privater Dateispeicher (Azure Blob bzw. lokaler Ordner in Development).</summary>
public interface IMedienSpeicher
{
    /// <summary>Speichert (überschreibt) und liefert die serverseitig gezählte Länge.</summary>
    Task<long> SpeichernAsync(string blobName, Stream inhalt, string contentType, CancellationToken ct = default);

    /// <summary>Öffnet zum Lesen; null, wenn nicht vorhanden.</summary>
    Task<Stream?> OeffnenAsync(string blobName, CancellationToken ct = default);

    Task<bool> LoeschenAsync(string blobName, CancellationToken ct = default);
}

/// <summary>Einheitliches Pfadschema: schulen/{schuleId:N}/{zweck}/{mediumId:N} (ohne Schule: plattform/…).</summary>
public static class MedienPfad
{
    public static string Fuer(Guid? schuleId, MedienZweck zweck, Guid mediumId)
    {
        var zweckText = zweck.ToString().ToLowerInvariant();
        return schuleId is { } s
            ? $"schulen/{s:N}/{zweckText}/{mediumId:N}"
            : $"plattform/{zweckText}/{mediumId:N}";
    }

    /// <summary>Nur unser eigenes Schema ist erlaubt (Schutz vor Pfadmanipulation).</summary>
    public static bool IstGueltig(string? blobName)
        => !string.IsNullOrWhiteSpace(blobName)
           && blobName.Length <= 600
           && !blobName.Contains("..", StringComparison.Ordinal)
           && !blobName.Contains('\\')
           && !Path.IsPathRooted(blobName)
           && blobName.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_');
}
