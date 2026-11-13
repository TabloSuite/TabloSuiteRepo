using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Components.Pages.Direction.Gemeinsam;

/// <summary>
/// Seite, deren Inhalt vom Schuljahr der Kopfzeile abhängt: lädt beim Start und erneut,
/// sobald dort ein anderes Jahr gewählt wird (nur dann – andere Kontextänderungen laden nichts nach).
/// </summary>
public abstract class JahrSeite : SkoulouSeite, IDisposable
{
    private Guid? geladenFuer;

    /// <summary>Gewähltes Schuljahr (Standard: laufendes Jahr).</summary>
    protected Guid? JahrId => Kontext.SchuljahrId;

    protected SchuljahrEintrag? Jahr => Kontext.Schuljahre.FirstOrDefault(j => j.Id == Kontext.SchuljahrId);

    protected override async Task InitialisierenAsync()
    {
        Kontext.Geaendert += KontextGeaendert;
        geladenFuer = JahrId;
        await JahrLadenAsync();
    }

    /// <summary>Lädt alles, was vom gewählten Jahr abhängt.</summary>
    protected abstract Task JahrLadenAsync();

    private void KontextGeaendert()
    {
        if (geladenFuer == JahrId) return;
        geladenFuer = JahrId;
        _ = InvokeAsync(async () =>
        {
            await AusfuehrenAsync(JahrLadenAsync);
            StateHasChanged();
        });
    }

    public virtual void Dispose()
    {
        Kontext.Geaendert -= KontextGeaendert;
        GC.SuppressFinalize(this);
    }
}
