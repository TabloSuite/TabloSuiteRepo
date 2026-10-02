using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Validation;
using System.ComponentModel.DataAnnotations;

namespace SkoulouApp.Core.Models.Profile;

public class UpdateUserProfileRequest
{
    [Required(ErrorMessage = "Veuillez saisir un prénom.")]
    [StringLength(100)]
    public string Vorname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir un nom.")]
    [StringLength(100)]
    public string Nachname { get; set; } = string.Empty;

    [OptionalPhone(ErrorMessage = "Veuillez saisir un numéro de téléphone valide.")]
    [StringLength(50)]
    public string? TelefonNummer { get; set; }

    public Geschlecht Geschlecht { get; set; } = Geschlecht.Divers;

    public Guid? ProfilBildAssetId { get; set; }

    public List<Guid> FachIds { get; set; } = [];
}
