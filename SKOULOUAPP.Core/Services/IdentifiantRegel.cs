using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkoulouApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Anmeldung seit 10.11.2026: Identifiant + Passwort für alle Rollen (wie eNOTE).
/// Das Identifiant ist der Identity-<c>UserName</c> (eindeutig). Die E-Mail ist optional,
/// dient nur Kontakt, Benachrichtigungen und « Mot de passe oublié » und darf zu mehreren
/// Konten gehören (z. B. Lehrkraft an zwei Schulen, Eltern mehrerer Kinder).
/// Ältere Konten behalten ihren bisherigen Benutzernamen (früher die E-Mail) als Identifiant.
/// </summary>
public static partial class IdentifiantRegel
{
    public const int MinLaenge = 3;
    public const int MaxLaenge = 40;
    public const string MeldungFormat = "L'identifiant doit comporter 3 à 40 caractères : lettres minuscules sans accent, chiffres, point, tiret ou trait de soulignement.";
    public const string MeldungVergeben = "Cet identifiant est déjà utilisé. Choisissez-en un autre.";

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$")]
    private static partial Regex FormatRegex();

    /// <summary>Kleinschreibung, ohne Akzente, Leerzeichen → Punkt, nur erlaubte Zeichen.</summary>
    public static string Normalisieren(string? eingabe)
    {
        if (string.IsNullOrWhiteSpace(eingabe))
            return string.Empty;

        var zerlegt = eingabe.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(zerlegt.Length);
        foreach (var zeichen in zerlegt)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(zeichen) == UnicodeCategory.NonSpacingMark)
                continue;
            var klein = char.ToLowerInvariant(zeichen);
            if (klein is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_')
                builder.Append(klein);
            else if (char.IsWhiteSpace(klein) || klein is '\'' or '’')
                builder.Append('.');
            else if (klein == 'ß')
                builder.Append("ss");
        }

        var ergebnis = Regex.Replace(builder.ToString(), "[.]{2,}", ".").Trim('.', '-', '_');
        return ergebnis.Length > MaxLaenge ? ergebnis[..MaxLaenge].TrimEnd('.', '-', '_') : ergebnis;
    }

    public static bool IstGueltig(string? identifiant)
        => identifiant is { Length: >= MinLaenge and <= MaxLaenge } && FormatRegex().IsMatch(identifiant);

    /// <summary>Grundvorschlag „vorname.nachname“ (ohne Eindeutigkeitsprüfung).</summary>
    public static string Vorschlag(string? vorname, string? nachname)
    {
        var basis = Normalisieren($"{vorname} {nachname}".Trim());
        if (basis.Length < MinLaenge)
            basis = Normalisieren($"compte.{basis}").Trim('.');
        return basis.Length > MaxLaenge - 3 ? basis[..(MaxLaenge - 3)].TrimEnd('.', '-', '_') : basis;
    }

    /// <summary>Eindeutiger Vorschlag: „ama.mensah“, sonst „ama.mensah2“, „ama.mensah3“ …</summary>
    public static async Task<string> EindeutigerVorschlagAsync(
        ISkoulouAppDbContext dbContext,
        string? vorname,
        string? nachname,
        ISet<string>? zusaetzlichBelegt = null,
        CancellationToken cancellationToken = default)
    {
        var basis = Vorschlag(vorname, nachname);
        var belegt = await dbContext.Users.AsNoTracking()
            .Where(user => user.NormalizedUserName != null && user.NormalizedUserName.StartsWith(basis.ToUpperInvariant()))
            .Select(user => user.NormalizedUserName!)
            .ToListAsync(cancellationToken);
        var vergeben = new HashSet<string>(belegt, StringComparer.OrdinalIgnoreCase);
        if (zusaetzlichBelegt is not null)
            vergeben.UnionWith(zusaetzlichBelegt);

        var kandidat = basis;
        for (var nummer = 2; vergeben.Contains(kandidat); nummer++)
            kandidat = $"{basis}{nummer}";
        zusaetzlichBelegt?.Add(kandidat);
        return kandidat;
    }

    /// <summary>Prüft Format und Eindeutigkeit; <c>null</c> = in Ordnung.</summary>
    public static async Task<string?> PruefenAsync(
        ISkoulouAppDbContext dbContext,
        string identifiant,
        Guid? kontoID,
        CancellationToken cancellationToken = default)
    {
        if (!IstGueltig(identifiant))
            return MeldungFormat;
        var normalisiert = identifiant.ToUpperInvariant();
        var vergeben = await dbContext.Users.AsNoTracking()
            .AnyAsync(user => user.NormalizedUserName == normalisiert && (!kontoID.HasValue || user.Id != kontoID.Value), cancellationToken);
        return vergeben ? MeldungVergeben : null;
    }
}
