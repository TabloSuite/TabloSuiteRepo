using System.ComponentModel.DataAnnotations;

namespace SkoulouApp.Core.Models.Admin;

public class MediaUploadFormModel
{
    [Required(ErrorMessage = "Veuillez choisir une leçon.")]
    public Guid? LessonId { get; set; }

    [Required(ErrorMessage = "Veuillez choisir un type de média.")]
    public string MediaType { get; set; } = "image";

    [Required(ErrorMessage = "Veuillez saisir un texte alternatif.")]
    [StringLength(250)]
    public string AltText { get; set; } = string.Empty;
}
