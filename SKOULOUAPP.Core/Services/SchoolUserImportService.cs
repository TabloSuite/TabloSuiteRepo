using System.Globalization;
using System.Text;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Models.Import;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

public sealed class SchoolUserImportService(
    ISkoulouAppDbContext dbContext,
    DashboardService dashboardService)
{
    private static readonly IReadOnlyDictionary<string, string> HeaderAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Vorname"] = "Vorname",
        ["Prénom"] = "Vorname",
        ["Prenom"] = "Vorname",
        ["PrÃ©nom"] = "Vorname",
        ["Nachname"] = "Nachname",
        ["Nom"] = "Nachname",
        ["Klasse"] = "Klasse",
        ["Classe"] = "Klasse",
        ["Email"] = "Email",
        ["E-mail"] = "Email",
        ["Telefon"] = "Telefon",
        ["Téléphone"] = "Telefon",
        ["Telephone"] = "Telefon",
        ["ElternEmail"] = "ElternEmail",
        ["E-mail parent"] = "ElternEmail",
        ["Email parent"] = "ElternEmail",
        ["ElternTelefon"] = "ElternTelefon",
        ["Téléphone parent"] = "ElternTelefon",
        ["Telephone parent"] = "ElternTelefon"
    };

    public async Task<SchoolUserImportPreviewResult> PreviewAsync(
        SchoolUserImportPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new SchoolUserImportPreviewResult();
        if (!await CanDirectorManageSchoolAsync(request.DirectorId, request.SchoolId, cancellationToken))
        {
            result.Errors.Add("Vous n'êtes pas autorisé à importer des utilisateurs pour cette école.");
            return result;
        }

        var parsedRows = ParseCsv(request.CsvContent);
        if (parsedRows.Count == 0)
        {
            result.Errors.Add("Le fichier est vide ou ne contient aucune ligne de données.");
            return result;
        }

        var classes = await dbContext.Klassen
            .AsNoTracking()
            .Where(klasse => klasse.SchoolId == request.SchoolId || (klasse.SchoolId == null && klasse.IstSystemNiveau))
            .Select(klasse => new { klasse.Id, klasse.Code, klasse.Title })
            .ToListAsync(cancellationToken);

        var existingEmails = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Email != null)
            .Select(user => user.Email!)
            .ToListAsync(cancellationToken);
        var usedEmails = new HashSet<string>(existingEmails.Select(NormalizeEmailKey), StringComparer.OrdinalIgnoreCase);

        foreach (var parsedRow in parsedRows)
        {
            var preview = new SchoolUserImportRowPreview
            {
                LineNumber = parsedRow.LineNumber,
                Vorname = Get(parsedRow, "Vorname"),
                Nachname = Get(parsedRow, "Nachname"),
                Klasse = Get(parsedRow, "Klasse"),
                Telefon = NormalizeOptional(Get(parsedRow, "Telefon")),
                ElternEmail = request.Kind == SchoolUserImportKind.Student ? NormalizeOptional(Get(parsedRow, "ElternEmail")) : null,
                ElternTelefon = request.Kind == SchoolUserImportKind.Student ? NormalizeOptional(Get(parsedRow, "ElternTelefon")) : null
            };

            if (string.IsNullOrWhiteSpace(preview.Vorname))
            {
                preview.Errors.Add("Le prénom est manquant.");
            }

            if (string.IsNullOrWhiteSpace(preview.Nachname))
            {
                preview.Errors.Add("Le nom est manquant.");
            }

            if (string.IsNullOrWhiteSpace(preview.Klasse))
            {
                preview.Errors.Add("La classe est manquante.");
            }

            var classNames = SplitClasses(preview.Klasse).ToList();
            if (request.Kind == SchoolUserImportKind.Student && classNames.Count > 1)
            {
                preview.Errors.Add("Les élèves ne peuvent être affectés qu'à une seule classe lors de l'importation.");
            }

            foreach (var className in classNames)
            {
                var matchingClass = classes.FirstOrDefault(klasse => MatchesClass(klasse.Code, klasse.Title, className));
                if (matchingClass is null)
                {
                    preview.Errors.Add($"La classe « {className} » est introuvable dans cette école.");
                    continue;
                }

                preview.KlasseId ??= matchingClass.Id;
                preview.KlasseIds.Add(matchingClass.Id);
            }

            preview.KlasseIds = preview.KlasseIds.Distinct().ToList();

            var providedEmail = NormalizeOptional(Get(parsedRow, "Email"));
            if (string.IsNullOrWhiteSpace(providedEmail))
            {
                preview.Email = GenerateUniqueSkoulouAppEmail(preview.Vorname, preview.Nachname, usedEmails);
                preview.EmailGenerated = true;
            }
            else
            {
                preview.Email = providedEmail.Trim().ToLowerInvariant();
                var key = NormalizeEmailKey(preview.Email);
                if (!usedEmails.Add(key))
                {
                    preview.Errors.Add($"L'adresse e-mail « {preview.Email} » existe déjà ou apparaît plusieurs fois dans l'importation.");
                }
            }

            result.Rows.Add(preview);
        }

        return result;
    }

    public async Task<SchoolUserImportCommitResult> CommitAsync(
        SchoolUserImportCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(new SchoolUserImportPreviewRequest
        {
            DirectorId = request.DirectorId,
            SchoolId = request.SchoolId,
            Kind = request.Kind,
            CsvContent = request.CsvContent
        }, cancellationToken);
        if (!preview.Succeeded)
        {
            return new SchoolUserImportCommitResult
            {
                Succeeded = false,
                Message = "L'import contient encore des erreurs.",
                Errors = preview.Errors.Concat(preview.Rows.SelectMany(row => row.Errors.Select(error => $"Ligne {row.LineNumber}: {error}"))).ToList()
            };
        }

        var created = 0;
        var errors = new List<string>();
        foreach (var row in preview.Rows)
        {
            var userSchoolIdsBeforeSave = await dbContext.UserSchools
                .AsNoTracking()
                .Where(link => link.SchoolId == request.SchoolId)
                .Select(link => link.AppUserId)
                .ToListAsync(cancellationToken);

            var model = new UserFormModel
            {
                Vorname = row.Vorname.Trim(),
                Nachname = row.Nachname.Trim(),
                Email = row.Email.Trim(),
                TelefonNummer = row.Telefon,
                RoleName = request.Kind == SchoolUserImportKind.Teacher ? Rollen.Lehrer : Rollen.Schueler,
                SchoolIds = [request.SchoolId],
                Password = SchoolUserImportDefaults.InitialPassword,
                PasswordConfirmation = SchoolUserImportDefaults.InitialPassword,
                IsBlocked = false,
                IsSelfRegistered = false
            };

            if (request.Kind == SchoolUserImportKind.Student)
            {
                model.CurrentKlasseId = row.KlasseId;
                model.ElternEmail = row.ElternEmail;
                model.ElternTelefonNummer = row.ElternTelefon;
            }
            else
            {
                model.TeacherClassAssignments = row.KlasseIds
                    .Distinct()
                    .Select(klasseId => new TeacherClassAssignmentFormModel
                    {
                        SchoolId = request.SchoolId,
                        KlasseId = klasseId,
                        Wochenstunden = 1m
                    })
                    .DistinctBy(assignment => assignment.KlasseId)
                    .ToList();
            }

            var (succeeded, message) = await dashboardService.SaveUserAsync(model, cancellationToken);
            if (succeeded)
            {
                var normalizedEmail = model.Email.Trim().ToUpperInvariant();
                var createdUserId = await dbContext.Users
                    .Where(user => user.Email != null && user.Email.ToUpper() == normalizedEmail)
                    .Select(user => user.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (createdUserId == Guid.Empty)
                {
                    createdUserId = await dbContext.UserSchools
                        .Where(link => link.SchoolId == request.SchoolId)
                        .Where(link => !userSchoolIdsBeforeSave.Contains(link.AppUserId))
                        .Select(link => link.AppUserId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (createdUserId == Guid.Empty)
                {
                    errors.Add($"Ligne {row.LineNumber}: L'utilisateur créé est introuvable dans la base de données.");
                    continue;
                }

                if (request.Kind == SchoolUserImportKind.Teacher && row.KlasseIds.Count > 0)
                {
                    var existingAssignments = await dbContext.LehrerNiveauZuordnungen
                        .Where(assignment => assignment.LehrerID == createdUserId && assignment.SchulID == request.SchoolId)
                        .ToListAsync(cancellationToken);
                    dbContext.LehrerNiveauZuordnungen.RemoveRange(existingAssignments);

                    foreach (var klasseId in row.KlasseIds.Distinct())
                    {
                        dbContext.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung
                        {
                            LehrerID = createdUserId,
                            SchulID = request.SchoolId,
                            KlasseId = klasseId,
                            Wochenstunden = 1m
                        });
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                created++;
            }
            else
            {
                errors.Add($"Ligne {row.LineNumber}: {message}");
            }
        }

        return new SchoolUserImportCommitResult
        {
            Succeeded = errors.Count == 0,
            CreatedCount = created,
            Errors = errors,
            Message = errors.Count == 0
                ? $"Import terminé: {created} utilisateur(s) créé(s)."
                : $"Import partiel: {created} utilisateur(s) créé(s), {errors.Count} erreur(s)."
        };
    }

    private async Task<bool> CanDirectorManageSchoolAsync(Guid directorId, Guid schoolId, CancellationToken cancellationToken)
        => await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(link => link.AppUserId == directorId && link.SchoolId == schoolId && link.CanManageStudents, cancellationToken);

    private static List<ParsedCsvRow> ParseCsv(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var headerLine = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (headerLine is null)
        {
            return [];
        }

        var headers = SplitCsvLine(headerLine)
            .Select(header => NormalizeHeader(header))
            .ToList();
        var rows = new List<ParsedCsvRow>();
        var headerSeen = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!headerSeen)
            {
                headerSeen = true;
                continue;
            }

            var values = SplitCsvLine(line);
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                data[headers[i]] = i < values.Count ? values[i].Trim() : string.Empty;
            }

            rows.Add(new ParsedCsvRow(index + 1, data));
        }

        return rows;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var separator = line.Contains(';') ? ';' : ',';
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == separator && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static string Get(ParsedCsvRow row, string key)
        => row.Values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string NormalizeHeader(string header)
    {
        var trimmed = header.Trim().TrimStart('\uFEFF');
        if (HeaderAliases.TryGetValue(trimmed, out var normalized))
        {
            return normalized;
        }

        var accentFree = string.Concat(trimmed.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
        return HeaderAliases.TryGetValue(accentFree, out normalized) ? normalized : trimmed;
    }

    private static IEnumerable<string> SplitClasses(string value)
        => value.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchesClass(string code, string title, string input)
        => string.Equals(code.Trim(), input.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(title.Trim(), input.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string GenerateUniqueSkoulouAppEmail(string vorname, string nachname, HashSet<string> usedEmails)
    {
        var baseLocalPart = BuildEmailLocalPart(vorname, nachname);
        if (string.IsNullOrWhiteSpace(baseLocalPart))
        {
            baseLocalPart = $"user{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var candidate = $"{baseLocalPart}@skoulou.com";
        var suffix = 2;
        while (!usedEmails.Add(NormalizeEmailKey(candidate)))
        {
            candidate = $"{baseLocalPart}{suffix}@skoulou.com";
            suffix++;
        }

        return candidate;
    }

    private static string BuildEmailLocalPart(string vorname, string nachname)
    {
        var source = $"{nachname}.{vorname}".Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join('.', builder.ToString().Split(['.', ' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.All(char.IsLetterOrDigit)));
    }

    private static string NormalizeEmailKey(string email) => email.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParsedCsvRow(int LineNumber, Dictionary<string, string> Values);
}
