using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Components.Pages.Admin.SchulTeile;

/// <summary>Statusänderungen einer Schule im /admin.</summary>
public enum SchulAktion
{
    Validieren,
    Ablehnen,
    Suspendieren,
    Reaktivieren
}

/// <summary>Angefragte Statusänderung (öffnet den Dialog).</summary>
public sealed record SchulAktionAnfrage(SchulZeile Schule, SchulAktion Aktion);
