namespace HalloApp.Core.Models.Import;

public enum SchoolUserImportKind
{
    Teacher,
    Student
}

public class SchoolUserImportPreviewRequest
{
    public Guid DirectorId { get; set; }
    public Guid SchoolId { get; set; }
    public SchoolUserImportKind Kind { get; set; }
    public string CsvContent { get; set; } = string.Empty;
}

public sealed class SchoolUserImportCommitRequest
{
    public Guid DirectorId { get; set; }
    public Guid SchoolId { get; set; }
    public SchoolUserImportKind Kind { get; set; }
    public string CsvContent { get; set; } = string.Empty;
}

public sealed class SchoolUserImportPreviewResult
{
    public bool Succeeded => Errors.Count == 0 && Rows.All(row => row.IsValid);
    public List<SchoolUserImportRowPreview> Rows { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public int ValidRowCount => Rows.Count(row => row.IsValid);
    public int ErrorRowCount => Rows.Count(row => !row.IsValid);
}

public sealed class SchoolUserImportCommitResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CreatedCount { get; set; }
    public List<string> Errors { get; set; } = [];
}

public sealed class SchoolUserImportRowPreview
{
    public int LineNumber { get; set; }
    public string Vorname { get; set; } = string.Empty;
    public string Nachname { get; set; } = string.Empty;
    public string Klasse { get; set; } = string.Empty;
    public Guid? KlasseId { get; set; }
    public List<Guid> KlasseIds { get; set; } = [];
    public string Email { get; set; } = string.Empty;
    public bool EmailGenerated { get; set; }
    public string? Telefon { get; set; }
    public string? ElternEmail { get; set; }
    public string? ElternTelefon { get; set; }
    public string InitialPassword { get; set; } = SchoolUserImportDefaults.InitialPassword;
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; set; } = [];
}

public static class SchoolUserImportDefaults
{
    public const string InitialPassword = "Passwort123!";

    public const string TeacherTemplate = "Prénom;Nom;Classe;E-mail;Téléphone\nJean;Koffi;CM1A;jean.koffi@example.com;90000000\nAmah;Mensah;CM1A,CM2B;;";

    public const string StudentTemplate = "Prénom;Nom;Classe;E-mail;Téléphone;E-mail parent;Téléphone parent\nAfi;Mensah;CM1A;afi.mensah@example.com;91111111;parent@example.com;92222222\nKomi;Adjovi;CM1A;;;;";
}
