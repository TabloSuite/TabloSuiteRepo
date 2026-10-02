namespace SkoulouApp.Core.Models.Admin;

/// <summary>Verwaltbare Klassenstufe einer normalen Schule.</summary>
public class KlassenStufeViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int KlassenCount { get; set; }
    public bool CanDelete => KlassenCount == 0;
}
