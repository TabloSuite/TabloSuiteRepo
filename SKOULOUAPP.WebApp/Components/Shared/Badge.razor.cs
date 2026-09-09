using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public partial class Badge
{
    [Parameter] public string Accent { get; set; } = "slate";
    [Parameter] public bool ShowDot { get; set; } = true;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private string CssClass =>
        $"badge{(ShowDot ? "" : " badge--nodot")} inline-flex items-center gap-1.5";

    // helle Füllung (--badge-bg), Akzentfarbe (--badge-accent), Textfarbe (--badge-text)
    private string AccentStyle => Accent switch
    {
        "blue" => "--badge-bg:#eff6ff;--badge-accent:#3b82f6;--badge-text:#1d4ed8;",
        "indigo" => "--badge-bg:#eef2ff;--badge-accent:#6366f1;--badge-text:#4338ca;",
        "emerald" => "--badge-bg:#ecfdf5;--badge-accent:#10b981;--badge-text:#047857;",
        "amber" => "--badge-bg:#fffbeb;--badge-accent:#f59e0b;--badge-text:#b45309;",
        "rose" => "--badge-bg:#fff1f2;--badge-accent:#f43f5e;--badge-text:#be123c;",
        "cyan" => "--badge-bg:#ecfeff;--badge-accent:#06b6d4;--badge-text:#0e7490;",
        "violet" => "--badge-bg:#f5f3ff;--badge-accent:#8b5cf6;--badge-text:#6d28d9;",
        "teal" => "--badge-bg:#f0fdfa;--badge-accent:#14b8a6;--badge-text:#0f766e;",
        _ => "--badge-bg:#f8fafc;--badge-accent:#64748b;--badge-text:#334155;"
    };
}
