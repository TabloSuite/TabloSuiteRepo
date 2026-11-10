using System.ComponentModel.DataAnnotations;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Validation;

namespace SkoulouApp.WebApp.Models.Auth;

/// Öffentliche Selbst-Registrierungsart (auf der Registrierungsseite wählbar).
public enum RegistrierungsArt
{
    // <summary>SchulLeiter registriert sich zusammen mit seiner neuen Schule.</summary>
    SchulLeiter = 0,

    // <summary>Schüler registriert sich für eine bereits vorhandene Schule.</summary>
    Schueler = 1
}

public class RegisterInputModel
{
    [Required(ErrorMessage = "Veuillez choisir un type d'inscription.")]
    public RegistrierungsArt Art { get; set; } = RegistrierungsArt.SchulLeiter;

    [Required(ErrorMessage = "Veuillez saisir ton prénom.")]
    [StringLength(100)]
    public string Vorname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir ton nom de famille.")]
    [StringLength(100)]
    public string Nachname { get; set; } = string.Empty;

    /// <summary>
    /// Geschlecht des Benutzers. Standardwert ist <see cref="Geschlecht.Divers"/>.
    /// </summary>
    [Required(ErrorMessage = "Veuillez choisir un genre.")]
    public Geschlecht Geschlecht { get; set; } = Geschlecht.Divers;

    /// <summary>Anmeldename (Identifiant); Vorschlag aus Vor- und Nachname.</summary>
    [Required(ErrorMessage = "Veuillez choisir un identifiant de connexion.")]
    [StringLength(40, MinimumLength = 3, ErrorMessage = "L'identifiant doit comporter 3 à 40 caractères.")]
    public string Identifiant { get; set; } = string.Empty;

    /// <summary>Kontaktadresse: Pflicht für die Direktion, optional für Schüler; nie Anmeldeschlüssel.</summary>
    [OptionalEmailAddress(ErrorMessage = "Veuillez saisir une adresse e-mail valide.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [OptionalPhone(ErrorMessage = "Veuillez saisir un numéro de téléphone valide.")]
    [StringLength(50)]
    public string? TelefonNummer { get; set; }

    [OptionalPhone(ErrorMessage = "Veuillez saisir un numéro de téléphone valide.")]
    [StringLength(50)]
    public string? ElternTelefonNummer { get; set; }

    [OptionalEmailAddress(ErrorMessage = "Veuillez saisir une adresse e-mail valide.")]
    [StringLength(256)]
    public string? ElternEmail { get; set; }

    /// <summary>
    /// Optionales Geburtsdatum des Nutzers. Die Aufnahmeentscheidung erfolgt durch die Schulleitung.
    /// </summary>
    [DataType(DataType.Date)]
    public DateTime? Geburtsdatum { get; set; }

    // --- Nur für Schüler: bestehende Schule wählen ---
    public Guid? SelectedSchoolId { get; set; }

    // --- Nur für SchulLeiter: Angaben zur neuen Schule ---
    [StringLength(200)]
    public string SchulName { get; set; } = string.Empty;

    [StringLength(120)]
    public string SchulStadt { get; set; } = string.Empty;

    [StringLength(120)]
    public string SchulLand { get; set; } = "Togo";

    [Required(ErrorMessage = "Veuillez saisir un mot de passe.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Le mot de passe doit comporter au moins 6 caractères.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez confirmer le mot de passe.")]
    [Compare(nameof(Password), ErrorMessage = "Les mots de passe ne correspondent pas.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool AgbAccepted { get; set; }

    public string FullName => $"{Vorname} {Nachname}".Trim();
}

