using HalloApp.Core.DataModels.BaseModel;

namespace HalloApp.Core.DataModels.Schools;

/// <summary>Zentral durch den SuperAdmin verwaltete Kommissionsquote für alle Abos.</summary>
public class CommissionSettings : AuditableModel
{
    public decimal GlobalSubscriptionPrice { get; set; } = HalloAppKonstanten.StandardPreisAbo;
    public decimal SchoolLeaderPercent { get; set; } = 4m;
    public decimal TeacherPercent { get; set; } = 8m;
    public decimal ProductOwnerPercent { get; set; } = 0m;
}
