using System.ComponentModel.DataAnnotations;
using HalloApp.Core.Validation;

namespace HalloApp.Core.Models.Admin;

public class UserFormModel
{
    public Guid? UserId { get; set; }

    [Required(ErrorMessage = "Veuillez saisir un prénom.")]
    [StringLength(100)]
    public string Vorname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir un nom de famille.")]
    [StringLength(100)]
    public string Nachname { get; set; } = string.Empty;

    public string FullName => $"{Vorname} {Nachname}".Trim();

    [Required(ErrorMessage = "Veuillez saisir une adresse e-mail.")]
    [EmailAddress(ErrorMessage = "Veuillez saisir une adresse e-mail valide.")]
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

    [Required(ErrorMessage = "Veuillez choisir un rôle.")]
    public string RoleName { get; set; } = "Schüler";

    public bool IsBlocked { get; set; }
    public bool IsSelfRegistered { get; set; }
    public List<Guid> SchoolIds { get; set; } = [];
    public List<Guid> FachIds { get; set; } = [];
    public Guid? CurrentKlasseId { get; set; }
    public List<TeacherClassAssignmentFormModel> TeacherClassAssignments { get; set; } = [];

    [DataType(DataType.Password)]
    [MinLength(6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
    public string? Password { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Les mots de passe ne correspondent pas.")]
    public string? PasswordConfirmation { get; set; }
}
