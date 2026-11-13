using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Pages;

/// <summary>
/// Kasse: Leistungen anwesender Kunden schon während der Anwesenheit fest erfassen (« Enregistrer sans encaisser »).
/// Erfasste Leistungen stehen gesperrt oben im Ticket, sobald der Kunde am Ticket ist, und werden beim Encaissement mitbezahlt.
/// </summary>
public partial class Caisse
{
    [Inject] private AufenthaltService AufenthaltDienst { get; set; } = null!;

    private bool kontoDialogOffen;

    /// <summary>Kunde am Ticket ist als anwesend gemeldet (Erfassen ohne Kassieren möglich).</summary>
    private bool KundeIstAnwesend => ticketKunde is { } k && anwesende.Any(a => a.KundeId == k.Id);

    private IEnumerable<TicketPosition> ErfassteZeilen => ticket.Where(p => p.AufenthaltsPositionId is not null);
    private IEnumerable<TicketPosition> NeueZeilen => ticket.Where(p => p.AufenthaltsPositionId is null);
    private bool HatErfassteZeilen => ticket.Any(p => p.AufenthaltsPositionId is not null);
    private bool HatNeueZeilen => ticket.Any(p => p.AufenthaltsPositionId is null);

    /// <summary>
    /// Offene erfasste Leistungen des Ticket-Kunden (neu) ins Ticket laden – oben, gesperrt, mit Tag.
    /// Rückgabe: Anzahl aller Zeilen seines Kontos (auch zurückgenommene), -1 ohne Kunde/Fehler.
    /// </summary>
    private async Task<int> LadeErfassteAsync()
    {
        if (ticketKunde is not { } kunde || aktiveSitzung is null)
        {
            ticket.RemoveAll(p => p.AufenthaltsPositionId is not null);
            return -1;
        }
        AufenthaltService.Konto konto;
        try { konto = await AufenthaltDienst.GetKontoAsync(aktuellerBenutzerId, aktiveSitzung.SalonId, kunde.Id); }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { ticket.RemoveAll(p => p.AufenthaltsPositionId is not null); return -1; }

        // Erst nach dem Laden ersetzen (kein await dazwischen): zwei gleichzeitige Aktualisierungen dürfen nichts verdoppeln.
        ticket.RemoveAll(p => p.AufenthaltsPositionId is not null);
        if (ticketKunde?.Id != kunde.Id) return -1;   // Kunde inzwischen gewechselt

        var erfasst = konto.Offene.Select(z => new TicketPosition
        {
            Leistung = leistungen.FirstOrDefault(l => l.Id == z.LeistungId) ?? new Leistung { Id = z.LeistungId ?? Guid.Empty, Name = z.Name, Preis = z.EinzelPreis },
            Menge = z.Menge,
            AufenthaltsPositionId = z.Id,
            FesterPreis = z.EinzelPreis,
            FesterName = z.Name,
            ErbrachtAm = z.ErbrachtAm,
        }).ToList();
        ticket.InsertRange(0, erfasst);
        PruefeRabattNochGueltig();
        _ = SendeTicketAnDisplayAsync();
        return konto.Zeilen.Count;
    }

    /// <summary>Kunde am Ticket gewechselt (Auswahlfeld): erfasste Leistungen des neuen Kunden zeigen.</summary>
    private async Task TicketKundeGewechseltAsync(KundenService.Kurz? kunde)
    {
        ticketKunde = kunde;
        await LadeErfassteAsync();
    }

    /// <summary>Neue Zeilen des Tickets für den anwesenden Kunden fest erfassen, ohne zu kassieren; danach ist das Ticket frei.</summary>
    private async Task ErfasseOhneKassierenAsync()
    {
        if (aktiveSitzung is null || ticketKunde is not { } kunde) return;
        var neue = NeueZeilen.Select(p => new AufenthaltService.PositionInput(p.Leistung.Id, p.Menge, p.FreierBetrag)).ToList();
        if (neue.Count == 0) return;
        try
        {
            var ergebnis = await AufenthaltDienst.ErfasseAsync(aktuellerBenutzerId, aktiveSitzung.SalonId, kunde.Id, neue);
            SetzeTicketZurueck();
            letzterVerkauf = null;
            _ = SendeTicketAnDisplayAsync();
            toast.ZeigeErfolg($"Enregistré pour « {kunde.Name} ». À payer au départ : {ergebnis.Gesamt.Anzahl} prestation(s) · {ergebnis.Gesamt.Summe:N0} F.");
            foreach (var w in ergebnis.Warnungen) toast.ZeigeInfo(w);
            leistungen = (await katalogService.GetLeistungenAsync(aktiveSitzung.SalonId)).Where(l => l.IstAktiv).ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            toast.ZeigeFehler(ex.Message);
        }
    }

    private void OeffneKonto()
    {
        if (ticketKunde is not null) kontoDialogOffen = true;
    }

    /// <summary>Nach einer Rücknahme im Dialog: Ticket und Bestand aktualisieren.</summary>
    private async Task KontoGeaendertAsync()
    {
        await LadeErfassteAsync();
        if (aktiveSitzung is not null)
            leistungen = (await katalogService.GetLeistungenAsync(aktiveSitzung.SalonId)).Where(l => l.IstAktiv).ToList();
    }

    private static string ErfasstText(TicketPosition p)
        => $"{(p.ErbrachtAm is DateOnly t ? AufenthaltRegeln.Tag(t) + " · " : "")}{(p.Menge > 1 ? $"{p.Menge} × {p.EffektivPreis:N0} F" : $"{p.EffektivPreis:N0} F")}";
}
