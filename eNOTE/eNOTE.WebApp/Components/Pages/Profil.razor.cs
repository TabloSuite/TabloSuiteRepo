using ENote.Core.Services.Storage;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Profil(
    AuthService authService,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    IMediaStorageService mediaStorage,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    LadeZustand ladeZustand)
{
    private bool isLoading = true;
    private Guid userId;
    private string fullName = string.Empty;
    private string email = string.Empty;
    private List<string> rollen = [];

    private string aktuellesPasswort = string.Empty;
    private string neuesPasswort = string.Empty;
    private string? pwFehler;
    private bool pwGeaendert;

    private string? profilBildUrl;
    private string? bildFehler;
    private bool bildErfolg;

    private string Initialen => string.Concat(fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => char.ToUpperInvariant(t[0])).Take(2));

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out userId))
        {
            isLoading = false;
            return;
        }

        fullName = user.FindFirstValue(ClaimTypes.GivenName) ?? user.Identity?.Name ?? "";
        email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? "";
        rollen = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = email;
        }

        // Profilbild laden
        await LadeProfilBildAsync();

        isLoading = false;
    }



    private async Task LadeProfilBildAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.ProfilBildBlobName is string blobName)
        {
            profilBildUrl = mediaStorage.GetPublicUrl(blobName);
        }
    }

    private async Task LadeProfilBildHochAsync(InputFileChangeEventArgs e)
    {
        bildFehler = null;
        bildErfolg = false;

        var datei = e.File;
        if (datei.Size > 8 * 1024 * 1024)
        {
            bildFehler = "Le fichier dépasse la limite de 8 Mo.";
            return;
        }

        try
        {
            // Altes Bild löschen (falls vorhanden)
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return;

            if (!string.IsNullOrWhiteSpace(user.ProfilBildBlobName))
            {
                await mediaStorage.DeleteAsync(user.ProfilBildBlobName);
            }

            // Neues Bild hochladen
            await using var stream = datei.OpenReadStream(maxAllowedSize: 8 * 1024 * 1024);
            // Blob-Name im erwarteten Format: global/{guid}{ext}
            // Wir speichern die userId separat, um später das Bild zu identifizieren
            var result = await mediaStorage.SaveAsync(new MediaSaveRequest
            {
                DateiName = datei.Name,
                ContentType = datei.ContentType,
                Inhalt = stream,
                SalonId = null // Profilbilder sind nicht salon-gebunden (werden als 'global' gespeichert)
            });

            if (!result.Erfolg || result.BlobName is null)
            {
                bildFehler = result.Fehler ?? "L'enregistrement de l'image a échoué.";
                return;
            }

            // Blob-Name in DB speichern
            user.ProfilBildBlobName = result.BlobName;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            profilBildUrl = result.OeffentlicheUrl;
            bildErfolg = true;
        }
        catch (Exception ex)
        {
            bildFehler = $"Erreur: {ex.Message}";
        }
    }

    private async Task EntferneProfilBildAsync()
    {
        bildFehler = null;
        bildErfolg = false;

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.ProfilBildBlobName is string blobName)
            {
                await mediaStorage.DeleteAsync(blobName);
                user.ProfilBildBlobName = null;
                user.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            profilBildUrl = null;
        }
        catch (Exception ex)
        {
            bildFehler = $"Erreur: {ex.Message}";
        }
    }

    private async Task AenderePasswortAsync()
    {
        pwFehler = null;
        pwGeaendert = false;

        if (string.IsNullOrWhiteSpace(aktuellesPasswort) || string.IsNullOrWhiteSpace(neuesPasswort))
        {
            pwFehler = "Veuillez remplir les deux champs.";
            return;
        }

        var (erfolg, fehler) = await authService.AenderePasswortAsync(userId, aktuellesPasswort, neuesPasswort);
        if (!erfolg)
        {
            pwFehler = fehler;
            return;
        }

        pwGeaendert = true;
        // Session rotiert → zur Anmeldung weiterleiten.
        navigationManager.NavigateTo("/auth/login?info=password-updated", forceLoad: true);
    }

    private static string RolleText(string rolle) => rolle switch
    {
        "SuperAdmin" => "Administrateur",
        "ProduktOwner" => "Propriétaire plateforme",
        "Besitzer" => "Propriétaire",
        "Kassierer" => "Caissier",
        _ => rolle
    };
}
