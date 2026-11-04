using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace ENote.WebApp.Components.Pages;

/// <summary>Kunden-Display: zeigt das laufende Ticket einer Kassensitzung live per SignalR.</summary>
public partial class Display(NavigationManager navigationManager) : IAsyncDisposable
{
    [Parameter] public Guid SitzungId { get; set; }

    private HubConnection? hubVerbindung;
    private bool verbunden;
    private DisplayTicket? ticket;

    protected override async Task OnInitializedAsync()
    {
        hubVerbindung = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/umsatz"))
            .WithAutomaticReconnect()
            .Build();

        hubVerbindung.On<DisplayTicket>("DisplayAktualisiert", t =>
        {
            ticket = t;
            InvokeAsync(StateHasChanged);
        });

        try
        {
            await hubVerbindung.StartAsync();
            await hubVerbindung.InvokeAsync("AbonniereDisplay", SitzungId.ToString());
            verbunden = true;
        }
        catch
        {
            verbunden = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (hubVerbindung is not null)
        {
            try
            {
                await hubVerbindung.InvokeAsync("VerlasseDisplay", SitzungId.ToString());
                await hubVerbindung.DisposeAsync();
            }
            catch
            {
                // Beim Verlassen keine Fehler werfen.
            }
        }
    }
}
