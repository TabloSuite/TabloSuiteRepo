namespace SkoulouApp.Core.Models.TeacherLessons;

/// <summary>
/// Lehrkraft, für die die Direktion Lektionen, Übungen und Prüfungen erfassen darf
/// (Auswahl « Saisir pour l'enseignant : … »).
/// </summary>
public sealed record LehrerAuswahlItem(Guid Id, string Name);
