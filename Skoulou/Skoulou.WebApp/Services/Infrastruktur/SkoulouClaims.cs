using System.Security.Claims;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>Zusätzliche Claims im Anmelde-Cookie (Anzeige und Protokoll; Rechte prüft immer die DB).</summary>
public static class SkoulouClaims
{
    /// <summary>Wert der aktiven Sitzung (muss Konto.AktiveSitzungId entsprechen).</summary>
    public const string Sitzung = "skoulou_session";

    public const string Schule = "skoulou_schule";
    public const string Name = "skoulou_name";

    public static Guid? KontoId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid? SchuleId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(Schule), out var id) ? id : null;

    public static string AnzeigeName(this ClaimsPrincipal user)
        => user.FindFirstValue(Name) is { Length: > 0 } n ? n : user.Identity?.Name ?? string.Empty;

    /// <summary>Hauptrolle aus den Rollen-Claims (genau eine je Konto).</summary>
    public static string? Rolle(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Role);
}
