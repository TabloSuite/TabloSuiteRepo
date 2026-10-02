using System.ComponentModel.DataAnnotations;

namespace SkoulouApp.Core.Models.Admin;

public class LessonFormModel
{
    public Guid? LessonId { get; set; }
    public Guid CourseId { get; set; }
    public int AssignmentVersion { get; set; } = 1;
    public bool IsCurrentAssignmentVersionLocked { get; set; }
    public bool HasSubmissionsInCurrentAssignmentVersion { get; set; }
    public Guid? FachId { get; set; }
    public string? FachName { get; set; }
    public string? NeueFachBezeichnung { get; set; }

    [Required(ErrorMessage = "Veuillez saisir un titre de leçon.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir un contenu.")]
    public string Content { get; set; } = string.Empty;

    public bool IsPublished { get; set; } = true;
    public DateTime? ExerciseSubmissionDueAtUtc { get; set; }

    public Guid? VideoAssetId { get; set; }
    public string? VideoDisplayName { get; set; }
    public TimeSpan? VideoDuration { get; set; }
    public string? VideoUrl { get; set; }

    public List<LessonExerciseFormModel> Exercises { get; set; } = [];
}

public class LessonExerciseFormModel
{
    public Guid? ExerciseId { get; set; }

    [Required(ErrorMessage = "Veuillez saisir un titre d'exercice.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Instructions { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veuillez saisir une question.")]
    [StringLength(1000)]
    public string Question { get; set; } = string.Empty;

    [Range(1, 1000, ErrorMessage = "Les points doivent être compris entre 1 et 1000.")]
    public int Points { get; set; } = 10;

    public string ExerciseType { get; set; } = "SingleChoice";
    public int Order { get; set; }

    public string? AudioText { get; set; }
    public string TtsLanguage { get; set; } = "de-DE";
    public string? AcceptedAnswers { get; set; }

    public List<LessonExerciseGapFormModel> Gaps { get; set; } = [];
    public List<LessonExerciseOptionFormModel> Options { get; set; } = [];
}

public class LessonExerciseGapFormModel
{
    public string AcceptedAnswers { get; set; } = string.Empty;
}

public class LessonExerciseOptionFormModel
{
    public Guid? OptionId { get; set; }

    [Required(ErrorMessage = "Veuillez saisir un texte d'option.")]
    [StringLength(500)]
    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }
}
