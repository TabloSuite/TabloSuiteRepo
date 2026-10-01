using System.ComponentModel.DataAnnotations.Schema;
using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Schools;

public class School : AuditableModel
{
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string DescriptionHtml { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Organisationsmodell der Schule. Wird bei der Registrierung oder Verwaltung festgelegt.
    /// </summary>
    /// <summary>
    /// Bisheriger Kompatibilitätswert. Intern wird ein einziger Abo-Preis verwendet.
    /// </summary>
    public decimal? PreisHalloBasis { get; set; }

    /// <summary>
    /// Bisheriger Kompatibilitätswert. Intern wird ein einziger Abo-Preis verwendet.
    /// </summary>
    public decimal? PreisHalloPlus { get; set; }

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

    [NotMapped]
    public decimal? SubscriptionPriceOverride
    {
        get => PreisHalloPlus ?? PreisHalloBasis;
        set
        {
            PreisHalloBasis = value;
            PreisHalloPlus = value;
        }
    }

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
    public decimal EffektiverPreis(AboTyp aboTyp) => aboTyp switch
    {
        AboTyp.HalloPlus => PreisHalloPlus ?? PreisHalloBasis ?? HalloAppKonstanten.StandardPreisAbo,
        _ => PreisHalloBasis ?? PreisHalloPlus ?? HalloAppKonstanten.StandardPreisAbo
    };
}







