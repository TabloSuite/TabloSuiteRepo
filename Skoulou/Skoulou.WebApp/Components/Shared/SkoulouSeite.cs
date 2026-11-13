using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Components.Shared;

/// <summary>Angemeldete Person laut Cookie (nur Anzeige; Rechte prüfen die Services in der DB).</summary>
public sealed record Nutzer(Guid KontoId, string Rolle, Guid? SchuleId, string Name)
{
    public static readonly Nutzer Anonym = new(Guid.Empty, string.Empty, null, string.Empty);
    public bool Angemeldet => KontoId != Guid.Empty;
}

/// <summary>
/// Basisklasse aller Fachseiten: stellt den aktuellen Nutzer und den Schulkontext bereit und fängt
/// <see cref="FachFehler"/> (Meldung) sowie <see cref="KeinZugriffFehler"/> (Hinweis statt Absturz) zentral ab.
/// </summary>
public abstract class SkoulouSeite : ComponentBase
{
    [CascadingParameter] protected Task<AuthenticationState>? AuthZustand { get; set; }
    [Inject] protected ToastService Toast { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected SchulKontext Kontext { get; set; } = default!;

    /// <summary>Aktueller Nutzer (aus den Claims).</summary>
    protected Nutzer Ich { get; private set; } = Nutzer.Anonym;

    /// <summary>Letzte fachliche Fehlermeldung (für die Anzeige am Formular).</summary>
    protected string? Fehlermeldung { get; set; }

    /// <summary>true, wenn ein Service den Zugriff verweigert hat.</summary>
    protected bool KeinZugriff { get; private set; }

    /// <summary>true bis <see cref="InitialisierenAsync"/> fertig ist.</summary>
    protected bool Laedt { get; private set; } = true;

    protected sealed override async Task OnInitializedAsync()
    {
        if (AuthZustand is not null)
        {
            var user = (await AuthZustand).User;
            if (user.KontoId() is { } id)
            {
                Ich = new Nutzer(id, user.Rolle() ?? string.Empty, user.SchuleId(), user.AnzeigeName());
                await Kontext.LadeAsync(id);
            }
        }
        await AusfuehrenAsync(InitialisierenAsync);
        Laedt = false;
    }

    /// <summary>Erstes Laden der Seite (statt OnInitializedAsync überschreiben).</summary>
    protected virtual Task InitialisierenAsync() => Task.CompletedTask;

    /// <summary>Führt eine Aktion aus; Fehler werden angezeigt statt die Seite abstürzen zu lassen.</summary>
    protected async Task<bool> AusfuehrenAsync(Func<Task> aktion, string? erfolgsMeldung = null)
    {
        try
        {
            await aktion();
            Fehlermeldung = null;
            if (erfolgsMeldung is not null) Toast.ZeigeErfolg(erfolgsMeldung);
            return true;
        }
        catch (FachFehler f)
        {
            Fehlermeldung = f.Message;
            Toast.ZeigeFehler(f.Message);
            return false;
        }
        catch (KeinZugriffFehler k)
        {
            KeinZugriff = true;
            Fehlermeldung = k.Message;
            Toast.ZeigeFehler(k.Message);
            return false;
        }
    }
}
