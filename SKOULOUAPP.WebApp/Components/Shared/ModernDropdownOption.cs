namespace HalloApp.WebApp.Components.Shared;

public sealed record ModernDropdownOption<TValue>(TValue Value, string Label, string? Description = null, string? Badge = null);
