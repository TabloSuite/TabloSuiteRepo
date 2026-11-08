using System.Text.RegularExpressions;
using Azure.Identity;
using Azure.Storage.Blobs;
using ENote.Core.Options;
using ENote.Core.Services.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

namespace ENote.WebApp.Services.Storage;

/// <summary>Umgebungsgrenzen werden beim Start geprüft, bevor Dienste auf Daten zugreifen.</summary>
public static class EnoteInfrastruktur
{
    public static IServiceCollection AddEnoteInfrastruktur(this IServiceCollection services,
        IConfiguration configuration, IWebHostEnvironment environment, string connectionString)
    {
        services.Configure<AzureBlobStorageOptions>(configuration.GetSection(AzureBlobStorageOptions.SectionName));
        var storage = configuration.GetSection(AzureBlobStorageOptions.SectionName).Get<AzureBlobStorageOptions>() ?? new();
        if (environment.IsDevelopment())
        {
            _ = LokalerMedienPfad(storage, environment);
            services.AddSingleton<IMediaStorageService, LocalMediaStorageService>();
        }
        else
        {
            PruefeProduktionsSql(connectionString);
            var serviceUri = PruefeBlobKonfiguration(storage);
            // Keine Credential-Kette, kein Shared Key, keine Entwickleranmeldung in Production.
            services.AddSingleton(new BlobServiceClient(serviceUri, new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)));
            services.AddSingleton<IMediaStorageService, AzureBlobMediaStorageService>();
        }

        var protection = services.AddDataProtection()
            .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "eNOTE");
        if (!environment.IsDevelopment())
        {
            var home = configuration["HOME"];
            var keyPath = configuration["DataProtection:KeysPath"];
            if (string.IsNullOrWhiteSpace(keyPath) && !string.IsNullOrWhiteSpace(home))
                keyPath = Path.Combine(home, "data", "enote", "keys");
            if (string.IsNullOrWhiteSpace(keyPath) || !Path.IsPathFullyQualified(keyPath))
                throw new InvalidOperationException("DataProtection:KeysPath muss auf einen persistenten absoluten Pfad zeigen (App Service: /home/data/enote/keys).");
            PruefeAusserhalbWebRoot(keyPath, environment);
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        }
        return services;
    }

    public static void PruefeProduktionsSql(string connectionString)
    {
        var sql = new SqlConnectionStringBuilder(connectionString);
        if (sql.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity
            || !string.IsNullOrEmpty(sql.UserID) || !string.IsNullOrEmpty(sql.Password)
            || sql.IntegratedSecurity || sql.TrustServerCertificate
            || !sql.Encrypt.Equals(SqlConnectionEncryptOption.Mandatory)
                && !sql.Encrypt.Equals(SqlConnectionEncryptOption.Strict))
            throw new InvalidOperationException("Production-SQL erfordert systemseitige Managed Identity, Encrypt=True und TrustServerCertificate=False; Benutzername/Passwort sind nicht erlaubt.");
    }

    public static Uri PruefeBlobKonfiguration(AzureBlobStorageOptions options)
    {
        if (!Uri.TryCreate(options.ServiceUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/" || !uri.IsDefaultPort
            || !uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage:ServiceUri muss eine HTTPS-Account-URI ohne SAS sein.");
        if (options.ContainerName.Length is < 3 or > 63
            || !Regex.IsMatch(options.ContainerName, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new InvalidOperationException("Storage:ContainerName ist ungültig.");
        return uri;
    }

    public static string LokalerMedienPfad(AzureBlobStorageOptions options, IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Lokaler Medienspeicher ist nur in Development erlaubt.");
        if (string.IsNullOrWhiteSpace(options.LocalDataRootPath))
            throw new InvalidOperationException("Storage:LocalDataRootPath fehlt.");
        var path = Path.GetFullPath(options.LocalDataRootPath, environment.ContentRootPath);
        PruefeAusserhalbWebRoot(path, environment);
        return path;
    }

    private static void PruefeAusserhalbWebRoot(string path, IWebHostEnvironment environment)
    {
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var relative = Path.GetRelativePath(webRoot, Path.GetFullPath(path));
        if (relative == "." || !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Private Dateien dürfen nicht innerhalb von wwwroot liegen.");
    }
}
