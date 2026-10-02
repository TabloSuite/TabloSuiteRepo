using System.ComponentModel.DataAnnotations;

namespace SkoulouApp.Core.Models.Admin;

public class SchoolFormModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Veuillez saisir un nom d'école.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir une ville.")]
    [StringLength(120)]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir un pays.")]
    [StringLength(120)]
    public string Country { get; set; } = string.Empty;

    public string DescriptionHtml { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optionaler schulspezifischer Abo-Preis (FCFA). Leer = globaler Standardpreis.
    /// </summary>
    [Range(0, 10_000_000, ErrorMessage = "Veuillez saisir un prix valide.")]
    public decimal? SubscriptionPriceOverride { get; set; }

    [Range(0, 100, ErrorMessage = "Veuillez saisir un pourcentage valide (0-100).")]
    public decimal? SchoolLeaderCommissionPercentOverride { get; set; }

    [Range(0, 100, ErrorMessage = "Veuillez saisir un pourcentage valide (0-100).")]
    public decimal? TeacherCommissionPercentOverride { get; set; }

    [Range(0, 100, ErrorMessage = "Veuillez saisir un pourcentage valide (0-100).")]
    public decimal? ProductOwnerCommissionPercentOverride { get; set; }

}
