using System.ComponentModel.DataAnnotations.Schema;
using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Schools;

public class School : AuditableModel
{
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string DescriptionHtml { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Optionaler schulspezifischer Abo-Preis.</summary>
    public decimal? SubscriptionPriceOverride { get; set; }

    /// <summary>
    /// Optionaler schulspezifischer Provisionsanteil für den Schulleiter in Prozent.
    /// Leer = globaler Standard aus CommissionSettings.
    /// </summary>
    public decimal? SchoolLeaderCommissionPercentOverride { get; set; }

    /// <summary>
    /// Optionaler schulspezifischer Provisionsanteil für Lehrer in Prozent.
    /// Leer = globaler Standard aus CommissionSettings.
    /// </summary>
    public decimal? TeacherCommissionPercentOverride { get; set; }

    /// <summary>
    /// Optionaler schulspezifischer Provisionsanteil für ProductOwner in Prozent.
    /// Leer = globaler Standard aus CommissionSettings.
    /// </summary>
    public decimal? ProductOwnerCommissionPercentOverride { get; set; }

    public ICollection<UserSchool> UserSchools { get; set; } = new List<UserSchool>();
    public ICollection<StudentProfile> Students { get; set; } = new List<StudentProfile>();
    public ICollection<KlassenStufe> KlassenStufen { get; set; } = new List<KlassenStufe>();
    public ICollection<Abrechnungsperiode> Abrechnungsperioden { get; set; } = new List<Abrechnungsperiode>();

    /// <summary>
    /// Liefert den effektiven Abo-Preis pro Klasse.
    /// </summary>
    public decimal EffektiverPreis(decimal globalSubscriptionPrice)
        => SubscriptionPriceOverride ?? globalSubscriptionPrice;

    /// <summary>
    /// Kompatibilitätsüberladung für ältere Aufrufer mit AboTyp.
    /// </summary>
    public decimal EffektiverPreis(AboTyp aboTyp)
        => SubscriptionPriceOverride ?? SkoulouAppKonstanten.StandardPreisAbo;
}







