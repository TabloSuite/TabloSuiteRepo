namespace HalloApp.Core.Models.Billing;

/// <summary>Bearbeitungsmodell für die globale Abo-Kommissionskonfiguration.</summary>
public class CommissionSettingsModel
{
    public decimal ReferencePrice { get; set; } = HalloApp.Core.DataModels.Schools.HalloAppKonstanten.StandardPreisAbo;
    public decimal GlobalSubscriptionPrice { get; set; } = HalloApp.Core.DataModels.Schools.HalloAppKonstanten.StandardPreisAbo;
    public decimal SchoolLeaderPercent { get; set; } = 4m;
    public decimal TeacherPercent { get; set; } = 8m;
    public decimal ProductOwnerPercent { get; set; } = 0m;
    public decimal SchoolLeaderAmount => Math.Round(GlobalSubscriptionPrice * SchoolLeaderPercent / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal TeacherAmount => Math.Round(GlobalSubscriptionPrice * TeacherPercent / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal ProductOwnerAmount => Math.Round(GlobalSubscriptionPrice * ProductOwnerPercent / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal PlatformPercent => 100m - SchoolLeaderPercent - TeacherPercent - ProductOwnerPercent;
    public decimal PlatformAmount => GlobalSubscriptionPrice - SchoolLeaderAmount - TeacherAmount - ProductOwnerAmount;
}
