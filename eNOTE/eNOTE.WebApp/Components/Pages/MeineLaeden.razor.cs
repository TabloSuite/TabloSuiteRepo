using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class MeineLaeden(
    ProduktOwnerService produktOwnerService,
    AuthenticationStateProvider authStateProvider,
    UserManager<AppUser> userManager,
    ToastService toast,
    LadeZustand ladeZustand)
{
    private List<ProduktOwnerService.DashboardLaden> laeden = [];
    private bool isLoading = true;
    private Guid benutzerId;
    private string laedenSuche = string.Empty;

    private IEnumerable<ProduktOwnerService.DashboardLaden> GefilterteLaeden()
    {
        if (string.IsNullOrWhiteSpace(laedenSuche)) return laeden;
        var s = laedenSuche.Trim();
        return laeden.Where(l => l.SalonName.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    // PO legt selbst einen Laden an (mit neuem Besitzer-Konto)
    private bool anlegedialogOffen;
    private string? anlegeFehler;
    private string neuVorname = string.Empty;
    private string neuNachname = string.Empty;
    private string neuPseudonym = string.Empty;
    private string neuEmail = string.Empty;
    private string neuTelefon = string.Empty;
    private string neuPasswort = string.Empty;
    private string neuSalonName = string.Empty;
    private string? neuStandort;
    private string? neuLand = "Togo";
    private static readonly IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> landOptionen =
        ENote.Core.DataModels.Salons.Laender.Alle
            .Select(l => new ENote.WebApp.Components.Shared.EnoteSelectOption<string> { Value = l, Label = l })
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdValue, out var userId))
        {
            benutzerId = userId;
        }
        await LadeAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        laeden = benutzerId == Guid.Empty ? [] : await produktOwnerService.GetDashboardAsync(benutzerId);
        isLoading = false;
    }

    private static string ErzeugeInitialPasswort()
    {
        const string zeichen = "abcdefghjkmnpqrstuvwxyz23456789";
        var zufall = new Random();
        return new string(Enumerable.Range(0, 10).Select(_ => zeichen[zufall.Next(zeichen.Length)]).ToArray());
    }

    private void OeffneAnlegeDialog()
    {
        neuVorname = neuNachname = neuPseudonym = neuEmail = neuTelefon = neuSalonName = string.Empty;
        neuStandort = null;
        neuLand = "Togo";
        neuPasswort = ErzeugeInitialPasswort();
        anlegeFehler = null;
        anlegedialogOffen = true;
    }

    private async Task LegeSalonSelbstAnAsync()
    {
        if (string.IsNullOrWhiteSpace(neuVorname) || string.IsNullOrWhiteSpace(neuNachname)
            || string.IsNullOrWhiteSpace(neuPseudonym) || string.IsNullOrWhiteSpace(neuEmail)
            || string.IsNullOrWhiteSpace(neuSalonName) || string.IsNullOrWhiteSpace(neuPasswort) || string.IsNullOrWhiteSpace(neuTelefon))
        {
            anlegeFehler = "Veuillez remplir tous les champs obligatoires.";
            return;
        }

        var (erfolg, fehler, salon) = await produktOwnerService.LegeSalonSelbstAnAsync(
            benutzerId, neuVorname, neuNachname, neuPseudonym, neuEmail, neuPasswort,
            neuSalonName, neuStandort, neuLand, userManager, telefon: neuTelefon);
        if (!erfolg)
        {
            anlegeFehler = fehler;
            return;
        }

        anlegedialogOffen = false;
        toast.ZeigeErfolg($"Établissement « {salon!.Name} » créé avec un nouveau compte propriétaire. Il sera activé après la validation.");
        await LadeAsync();
    }

    private static string ProvisionText(ProduktOwnerService.DashboardLaden l)
    {
        var teile = new List<string>();
        if (l.ProzentProvision is not null)
        {
            teile.Add($"{l.ProzentProvision:N0} %");
        }

        if (l.FestbetragProvision is not null)
        {
            teile.Add($"{l.FestbetragProvision:N0} F");
        }

        return string.Join(" + ", teile);
    }
}
