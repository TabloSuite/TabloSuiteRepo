using ENote.Core.DataModels.Kasse;
using ENote.WebApp.Services;
using Microsoft.JSInterop;

namespace ENote.WebApp.Components.Pages;

/// <summary>
/// F5 in der Kasse (21.11.2026): Das angefangene Ticket steht im localStorage DIESES Geräts (<see cref="TicketMerker"/>)
/// und kommt nach dem Neuladen (oder beim Zurückkehren zur Kasse) wieder – Preise aus dem Katalog, gelöschte Leistungen
/// weg, Remise neu geprüft. Gemerkt wird erst, wenn feststeht, dass die Kasse an diesem Gerät läuft; jede Änderung danach
/// überschreibt den Eintrag, ein leeres Ticket (Verkauf, Abbruch, Parken), Abschluss oder Übernahme durch ein anderes Gerät
/// entfernt ihn.
/// </summary>
public partial class Caisse
{
    private bool initFertig;
    private bool ticketMerkerBereit;
    private bool merkerLaeuft;
    // Zuletzt im Browser stehender Inhalt (null = nichts) und sein Laden.
    private string? gemerkterStand;
    private Guid? gemerkterSalonId;

    protected override async Task OnInitializedAsync()
    {
        try { await InitialisiereKasseAsync(); }
        finally { initFertig = true; }
    }

    /// <summary>Nach jedem Rendern: einmal wiederherstellen (wenn die Kasse hier läuft), danach Änderungen merken.</summary>
    private async Task TicketMerkerNachRenderAsync()
    {
        if (!initFertig || mitarbeiterKonto || merkerLaeuft) return;
        merkerLaeuft = true;
        try
        {
            if (!ticketMerkerBereit)
            {
                if (aktiveSitzung is not null && kassenGeraet is null) return;   // Geräteprüfung steht noch aus
                ticketMerkerBereit = true;
                if (aktiveSitzung is { } sitzung && kassenGeraet == KassenService.GeraetStatus.DiesesGeraet
                    && await StelleTicketWiederHerAsync(sitzung))
                    StateHasChanged();
            }
            await MerkeTicketAsync();
        }
        finally
        {
            merkerLaeuft = false;
        }
    }

    /// <summary>Gemerktes Ticket dieses Geräts übernehmen; true = Ticket wiederhergestellt.</summary>
    private async Task<bool> StelleTicketWiederHerAsync(KassenSitzung sitzung)
    {
        string? json;
        try { json = await js.InvokeAsync<string?>("localStorage.getItem", TicketMerker.Schluessel(sitzung.SalonId)); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException) { return false; }
        if (json is null) return false;
        (gemerkterStand, gemerkterSalonId) = (json, sitzung.SalonId);   // wird beim Merken ersetzt oder entfernt

        // Ein schon laufendes Ticket (Kunde aus « Clients présents » → Encaisser) hat Vorrang.
        if (ticket.Count > 0 || ticketKunde is not null) return false;
        if (TicketMerker.Pruefe(TicketMerker.Lese(json), sitzung.Id, leistungen, rabatte) is not { } w) return false;

        await WendeUebernahmeAnAsync(new TicketParkService.Uebernahme(w.KundeId, w.RabattId, w.RabattBedingungBestaetigt, null,
                w.Zeilen.Select(z => new TicketParkService.PositionInput(z.Leistung.Id, z.Menge, z.FreierBetrag)).ToList(), w.Ausgelassen),
            ausDiesemGeraet: true, preiseGeaendert: w.PreiseGeaendert);
        return true;
    }

    /// <summary>Aktuellen Stand in den Browser schreiben (nur bei Änderung); kein Ticket/keine Kasse hier → Eintrag entfernen.</summary>
    private async Task MerkeTicketAsync()
    {
        while (true)
        {
            string? soll = null;
            var salon = aktiveSitzung?.SalonId ?? gemerkterSalonId;
            if (aktiveSitzung is { } s && kassenGeraet == KassenService.GeraetStatus.DiesesGeraet && HatNeueZeilen)
            {
                soll = TicketMerker.Serialisiere(new TicketMerker.Gemerkt(TicketMerker.Version, s.Id, ticketKunde?.Id,
                    ausgewaehlterRabatt?.Id, ausgewaehlterRabatt is not null && bedingungBestaetigt,
                    NeueZeilen.Select(p => new TicketMerker.Zeile(p.Leistung.Id, p.Menge, p.FreierBetrag, p.Leistung.Preis)).ToList()));
            }
            if (salon is not Guid salonId || (soll == gemerkterStand && salonId == gemerkterSalonId)) return;

            try
            {
                if (gemerkterSalonId is Guid alt && alt != salonId && gemerkterStand is not null)
                    await js.InvokeVoidAsync("localStorage.removeItem", TicketMerker.Schluessel(alt));
                if (soll is null) await js.InvokeVoidAsync("localStorage.removeItem", TicketMerker.Schluessel(salonId));
                else await js.InvokeVoidAsync("localStorage.setItem", TicketMerker.Schluessel(salonId), soll);
                (gemerkterStand, gemerkterSalonId) = (soll, salonId);
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
            {
                return;   // Merken ist eine Hilfe – die Kasse läuft auch ohne
            }
        }
    }
}
