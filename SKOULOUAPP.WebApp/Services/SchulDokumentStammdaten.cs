using System.ComponentModel.DataAnnotations;

namespace SkoulouApp.WebApp.Services;

public sealed class SchulDokumentStammdaten
{
    [MaxLength(300)] public string? Slogan { get; set; }
    [MaxLength(300)] public string? OffizielleBezeichnung { get; set; }
    [MaxLength(300)] public string? DokumentAdresse { get; set; }
    [MaxLength(60)] public string? DokumentTelefon { get; set; }
    [MaxLength(256), EmailAddress(ErrorMessage = "Saisissez une adresse e-mail valide.")]
    public string? DokumentEmail { get; set; }
    [MaxLength(100)] public string? Zulassungsnummer { get; set; }
    [MaxLength(300)] public string? Ministerium { get; set; }
    [MaxLength(300)] public string? RepublikZeile { get; set; }
    [MaxLength(300)] public string? StaatsMotto { get; set; }
}

public sealed record SchuelerStammdatenZeile(Guid SchuelerID, string Name, string Klasse, string? Matrikelnummer, string? Geburtsort);
