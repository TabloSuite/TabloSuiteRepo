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
    DashboardService dashboardService,
    IBenutzerArbeitsvorgang? benutzerArbeitsvorgang = null)
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
        ["Identifiant"] = "Identifiant",
        ["Login"] = "Identifiant",
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

        // Identifiants müssen über alle Konten und innerhalb der Datei eindeutig sein; E-Mails nicht.
        var belegteIdentifiants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            preview.Email = NormalizeOptional(Get(parsedRow, "Email"))?.ToLowerInvariant() ?? string.Empty;
            if (preview.Email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(preview.Email, out _))
            {
                preview.Errors.Add($"L'adresse e-mail « {preview.Email} » n'est pas valide.");
            }

            var gewuenscht = IdentifiantRegel.Normalisieren(Get(parsedRow, "Identifiant"));
            if (gewuenscht.Length == 0)
            {
                preview.Identifiant = await IdentifiantRegel.EindeutigerVorschlagAsync(dbContext, preview.Vorname, preview.Nachname, belegteIdentifiants, cancellationToken);
                preview.IdentifiantGenerated = true;
            }
            else
            {
                preview.Identifiant = gewuenscht;
                var fehler = await IdentifiantRegel.PruefenAsync(dbContext, gewuenscht, null, cancellationToken);
                if (fehler is null && !belegteIdentifiants.Add(gewuenscht))
                {
                    fehler = "Cet identifiant apparaît plusieurs fois dans l'importation.";
                }

                if (fehler is not null)
                {
                    preview.Errors.Add($"Identifiant « {gewuenscht} » : {fehler}");
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
        if (benutzerArbeitsvorgang is not null)
        {
            var result = await benutzerArbeitsvorgang.AusfuehrenAsync((_, import) => import.CommitAsync(request, cancellationToken),
                value => value.Succeeded, cancellationToken);
            if (!result.Succeeded)
            {
                result.CreatedCount = 0;
                result.Zugangsdaten.Clear();
                result.Message = "L'import a été annulé intégralement. Corrigez les erreurs puis réessayez.";
            }
            return result;
        }
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
        var zugangsdaten = new List<SchoolUserImportZugang>();
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
                Identifiant = row.Identifiant,
                Email = row.Email.Trim(),
                TelefonNummer = row.Telefon,
                RoleName = request.Kind == SchoolUserImportKind.Teacher ? Rollen.Lehrer : Rollen.Schueler,
                SchoolIds = [request.SchoolId],
                Password = ErstzugangService.PasswortErzeugen(),
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

            model.PasswordConfirmation = model.Password;
            var (succeeded, message) = await dashboardService.SaveUserAsync(model, cancellationToken);
            if (succeeded)
            {
                var normalizedIdentifiant = model.Identifiant.ToUpperInvariant();
                var createdUserId = await dbContext.Users
                    .Where(user => user.NormalizedUserName == normalizedIdentifiant)
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

                zugangsdaten.Add(new SchoolUserImportZugang(model.Identifiant, model.Password!));
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
            Zugangsdaten = zugangsdaten,
            Errors = errors,
            Message = errors.Count == 0
                ? $"Import terminé: {created} utilisateur(s) créé(s)."
                : $"Import partiel: {created} utilisateur(s) créé(s), {errors.Count} erreur(s)."
        };
    }

    private async Task<bool> CanDirectorManageSchoolAsync(Guid directorId, Guid schoolId, CancellationToken cancellationToken)
        => await new SchulZugangService(dbContext).IstSchuleFreigegebenAsync(schoolId, cancellationToken)
            && await dbContext.UserRoles.AnyAsync(ur => ur.UserId == directorId
                && dbContext.Roles.Any(r => r.Id == ur.RoleId && (r.Name == Rollen.SchulLeiter || r.Name == Rollen.Koordinator)), cancellationToken)
            && await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(link => link.AppUserId == directorId && !link.AppUser.IsBlocked && link.SchoolId == schoolId && link.CanManageStudents, cancellationToken);

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

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParsedCsvRow(int LineNumber, Dictionary<string, string> Values);
}
