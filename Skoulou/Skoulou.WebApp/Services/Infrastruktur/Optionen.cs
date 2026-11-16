using System.ComponentModel.DataAnnotations;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>Abschnitt « App » (Basis-URL für Mail-Links und QR, Zeitzone).</summary>
public sealed class AppOptionen
{
    public const string Abschnitt = "App";

    /// <summary>Öffentliche Adresse ohne Schrägstrich am Ende; in Production Pflicht.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Zeitzone { get; set; } = "Africa/Lome";

    /// <summary>Absolute URL zu einem lokalen Pfad.</summary>
    public string Url(string pfad) => BaseUrl.TrimEnd('/') + "/" + pfad.TrimStart('/');
}

/// <summary>Abschnitt « Database ».</summary>
public sealed class DatenbankOptionen
{
    public const string Abschnitt = "Database";

    [Required] public string ConnectionStringName { get; set; } = "DefaultConnection";
    public bool ApplyMigrationsOnStartup { get; set; }
}

/// <summary>Abschnitt « Seed » (Start-Admin; nur anlegen, falls er fehlt).</summary>
public sealed class SeedOptionen
{
    public const string Abschnitt = "Seed";

    public string? SuperAdminEmail { get; set; }
    public string? SuperAdminPasswort { get; set; }
    public string? SuperAdminIdentifiant { get; set; }
    public string SuperAdminName { get; set; } = "Administrateur SKOULOU";
}

/// <summary>Abschnitt « Postmark » (Versand und Eingang; Schlüsselnamen wie in der Alt-App).</summary>
public sealed class PostmarkOptionen
{
    public const string Abschnitt = "Postmark";

    public bool Enabled { get; set; }
    public string? ServerToken { get; set; }
    public string FromEmail { get; set; } = SkoulouMarke.KontaktEmail;
    public string FromName { get; set; } = "SKOULOU";
    public string? SupportEmail { get; set; }
    public string MessageStream { get; set; } = "outbound";
    public string? InboundWebhookSecret { get; set; }
    public string? WeiterleitungEnoteUrl { get; set; }
    public string? WeiterleitungEnoteGeheimnis { get; set; }

    /// <summary>Versand möglich (eingeschaltet, Token und Absender gesetzt)?</summary>
    public bool IstVersandBereit => Enabled && !string.IsNullOrWhiteSpace(ServerToken) && !string.IsNullOrWhiteSpace(FromEmail);

    /// <summary>Ziel des Kontaktformulars (Support, sonst Absender).</summary>
    public string SupportAdresse => string.IsNullOrWhiteSpace(SupportEmail) ? FromEmail : SupportEmail;
}

/// <summary>Abschnitt « Storage » (= « AzureBlobStorage », « Storage » gewinnt).</summary>
public sealed class SpeicherOptionen
{
    public const string Abschnitt = "Storage";
    public const string AlterAbschnitt = "AzureBlobStorage";

    /// <summary>https://&lt;konto&gt;.blob.core.windows.net/ – Managed Identity, hat Vorrang.</summary>
    public string? ServiceUri { get; set; }

    public string? BlobConnectionString { get; set; }
    public string ContainerName { get; set; } = "etsetech-blobuploads";
    public string FinanzBelegContainerName { get; set; } = "etsetech-blobuploads";

    /// <summary>Lokaler Ordner (nur Development), außerhalb von wwwroot.</summary>
    public string? LocalDataRootPath { get; set; }
}

/// <summary>
/// Abschnitt « AzureSpeech » (Vorlesen der Audio-Aufgaben). Schlüssel nur in den App-Einstellungen bzw. User-Secrets,
/// nie im Repo. Fehlt Key oder Region, liest der Browser wie bisher mit seiner eigenen Stimme vor.
/// </summary>
public sealed class SprachausgabeOptionen
{
    public const string Abschnitt = "AzureSpeech";

    public string? Key { get; set; }

    /// <summary>Region der Speech-Ressource, z. B. « westeurope ».</summary>
    public string? Region { get; set; }

    /// <summary>Optional: andere Stimme je Sprache, Schlüssel = Sprachkürzel (« fr », « en », « de », « es »).</summary>
    public Dictionary<string, string> Stimmen { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Region in Kleinbuchstaben ohne Leerzeichen am Rand.</summary>
    public string RegionNormiert => Region?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>Nur Kleinbuchstaben und Ziffern (die Region wird Teil des Rechnernamens).</summary>
    public static bool IstGueltigeRegion(string? region)
        => region is { Length: > 1 and <= 40 } && region.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

    public bool IstBereit => !string.IsNullOrWhiteSpace(Key) && IstGueltigeRegion(RegionNormiert);
}

/// <summary>Öffentliche Adressen der Marke.</summary>
public static class SkoulouMarke
{
    public const string Domain = "skoulou.tablosuite.com";
    public const string Website = "https://" + Domain;
    public const string KontaktEmail = "contact@tablosuite.com";
    public const string FirmenSeite = "https://tablosuite.com";
    public const string Ort = "Lomé, Togo";
}
