namespace HalloApp.Core.Models.Admin;

public class LevelEnrollmentItem
{
    public Guid KlasseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal SubscriptionPrice { get; set; }
    public decimal HalloBasisPreis => SubscriptionPrice;
    public decimal HalloPlusPreis => SubscriptionPrice;
    public decimal AboPreis { get; set; }
    public HalloApp.Core.DataModels.Schools.AboTyp AboTyp { get; set; }
    public bool IsUnlocked { get; set; }
    public bool IsPaid { get; set; }
    public bool IstAbgerechnet { get; set; }
    public DateTime? UnlockedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public bool HasActiveClassCycle { get; set; }
    public bool IsClassCycleClosed { get; set; }
    public bool IsCurrentKlasse { get; set; }
    public bool CanAssignOrUpgradeAbo => IsCurrentKlasse && HasActiveClassCycle && !IsClassCycleClosed && !IsUnlocked;
    public string? AboActionHint => !IsCurrentKlasse
        ? "L'abonnement HALLOAPP se gère uniquement pour la classe actuelle de l'élève."
        : !HasActiveClassCycle
            ? "Démarre d'abord le cycle de cette classe avant d'attribuer ou modifier un abonnement HALLOAPP."
            : IsClassCycleClosed
                ? "Le cycle de cette classe est clôturé. L'abonnement ne peut plus être modifié."
                : null;
}
