namespace SkoulouApp.Core.Models.Admin;

/// <summary>Schule mit Medien in der Medienbibliothek (für den Schulfilter): Anzahl und Gesamtgröße.</summary>
public sealed record MediaLibrarySchule(Guid SchoolId, string SchoolName, int Anzahl, long Bytes);
