namespace ENote.WebApp.Services;

/// <summary>
/// Query-Parameter aus Links (Mails, « Encaisser » aus Présents) als Text binden und hier lesen. Fehlersuche 21.11.2026:
/// <c>[SupplyParameterFromQuery] Guid</c> warf bei « ?kunde=kaputt » bzw. « ?compte=kaputt » schon beim Binden –
/// Kasse in der Fehlergrenze, Passwort-Seite mit HTTP 500. Unlesbare Werte gelten jetzt als « kein Wert ».
/// </summary>
public static class LinkParameter
{
    public static Guid? AlsGuid(string? text) => Guid.TryParse(text?.Trim(), out var g) && g != Guid.Empty ? g : null;
}
