namespace Skoulou.WebApp.Components.Pages.Admin.KontenTeile;

/// <summary>Aktionen auf ein Konto im /admin.</summary>
public enum KontoAktionArt
{
    Sperren,
    Entsperren,
    Passwort
}

/// <summary>Angefragte Kontoaktion (öffnet den Dialog).</summary>
public sealed record KontoAktionAnfrage(Guid KontoId, string Name, KontoAktionArt Aktion);
