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
    LadeZustand ladeZustand,
    KontoProfilService kontoProfil,
    ToastService toast)
{
    // Persönliche Daten (nur Admin, PO, Besitzer; Kassierer: null)
    private KontoProfilService.KontoProfil? profil;
    private string vorname = string.Empty;
    private string nachname = string.Empty;
    private string telefon = string.Empty;
    private string? datenFehler;
    private bool DatenGeaendert => profil is not null
        && (vorname.Trim() != profil.Vorname || nachname.Trim() != profil.Nachname || telefon.Trim() != (profil.Telefon ?? ""));

    // Pflichtangaben je Formular (wie KontoProfilService/AuthService): Knopf erst frei, wenn alles ausgefüllt ist.
    private bool DatenVollstaendig => !string.IsNullOrWhiteSpace(vorname) && !string.IsNullOrWhiteSpace(nachname);
    private bool EmailVollstaendig => !string.IsNullOrWhiteSpace(neueEmail) && !string.IsNullOrWhiteSpace(emailPasswort);
    private bool PseudonymVollstaendig => !string.IsNullOrWhiteSpace(neuesPseudonym) && !string.IsNullOrWhiteSpace(pseudonymPasswort);
    private bool PasswortVollstaendig => !string.IsNullOrWhiteSpace(aktuellesPasswort) && !string.IsNullOrWhiteSpace(neuesPasswort);

    private bool emailDialogOffen;
    private string neueEmail = string.Empty;
    private string emailPasswort = string.Empty;
    private string? emailFehler;

    private bool pseudonymDialogOffen;
    private string neuesPseudonym = string.Empty;
    private string pseudonymPasswort = string.Empty;
    private string? pseudonymFehler;

    private bool isLoading = true;
    private Guid userId;
    private string fullName = string.Empty;
    private string email = string.Empty;
    private List<string> rollen = [];
    private string? pseudonym;
    private List<string> laeden = [];

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

        // Anzeigename, Pseudonym und Läden aus der Datenbank (Claims enthalten nur das Pseudonym).
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            var konto = await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new { u.FullName, u.UserName, u.Email }).FirstOrDefaultAsync();
            if (konto is not null)
            {
                if (!string.IsNullOrWhiteSpace(konto.FullName)) fullName = konto.FullName;
                pseudonym = konto.UserName;
                if (!string.IsNullOrWhiteSpace(konto.Email)) email = konto.Email;
            }

            laeden = await db.UserSalons.AsNoTracking().Where(us => us.AppUserId == userId && us.Salon.IstAktiv)
                .Select(us => us.Salon.Name).OrderBy(n => n).ToListAsync();
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = email;
        }

        await LadeProfilDatenAsync();

        // Profilbild laden
        await LadeProfilBildAsync();

        isLoading = false;
    }



    private async Task LadeProfilDatenAsync()
    {
        profil = await kontoProfil.GetAsync(userId);
        if (profil is null) return;
        vorname = profil.Vorname;
        nachname = profil.Nachname;
        telefon = profil.Telefon ?? string.Empty;
        // Ältere Konten ohne getrennte Felder: Vor-/Nachname aus dem Anzeigenamen vorschlagen.
        if (vorname.Length == 0 && nachname.Length == 0 && profil.Name.Contains(' '))
        {
            var teile = profil.Name.Trim().Split(' ', 2);
            vorname = teile[0];
            nachname = teile[1];
        }
        if (!string.IsNullOrWhiteSpace(profil.Name)) fullName = profil.Name;
        email = profil.Email;
        pseudonym = profil.Pseudonym;
    }

    private async Task SpeichereDatenAsync()
    {
        datenFehler = null;
        var (erfolg, fehler) = await kontoProfil.AenderePersoenlicheDatenAsync(userId, vorname, nachname, telefon);
        if (!erfolg)
        {
            datenFehler = fehler;
            return;
        }

        await LadeProfilDatenAsync();
        toast.ZeigeErfolg("Informations enregistrées.");
    }

    private void OeffneEmailDialog()
    {
        neueEmail = string.Empty;
        emailPasswort = string.Empty;
        emailFehler = null;
        emailDialogOffen = true;
    }

    private async Task AendereEmailAsync()
    {
        emailFehler = null;
        var (erfolg, fehler) = await kontoProfil.AendereEmailAsync(userId, neueEmail, emailPasswort);
        emailPasswort = string.Empty;
        if (!erfolg)
        {
            emailFehler = fehler;
            return;
        }

        emailDialogOffen = false;
        await LadeProfilDatenAsync();
        toast.ZeigeErfolg("Adresse e-mail modifiée.");
    }

    private void OeffnePseudonymDialog()
    {
        neuesPseudonym = string.Empty;
        pseudonymPasswort = string.Empty;
        pseudonymFehler = null;
        pseudonymDialogOffen = true;
    }

    private async Task AenderePseudonymAsync()
    {
        pseudonymFehler = null;
        var (erfolg, fehler) = await kontoProfil.AenderePseudonymAsync(userId, neuesPseudonym, pseudonymPasswort);
        pseudonymPasswort = string.Empty;
        if (!erfolg)
        {
            pseudonymFehler = fehler;
            return;
        }

        // Sitzung wurde beendet → mit dem neuen Identifiant neu anmelden.
        navigationManager.NavigateTo("/auth/login?info=identifiant-modifie", forceLoad: true);
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
            user.UpdatedAtUtc = ENote.Core.Services.AppUhr.UtcNow;
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
                user.UpdatedAtUtc = ENote.Core.Services.AppUhr.UtcNow;
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
        "Kassierer" => "Employé",
        _ => rolle
    };
}
