SET QUOTED_IDENTIFIER ON;

DECLARE @UserId uniqueidentifier = (
	SELECT TOP 1 Id
	FROM dbo.AspNetUsers
	WHERE NormalizedEmail = N'DODJI_ETSE@WEB.DE'
	   OR Email = N'dodji_etse@web.de'
);

SELECT @UserId AS UserId;

SELECT r.Name AS RoleName
FROM dbo.AspNetUserRoles AS ur
INNER JOIN dbo.AspNetRoles AS r ON r.Id = ur.RoleId
WHERE ur.UserId = @UserId;

SELECT 'StudentProfiles' AS TableName, COUNT(*) AS CountValue FROM dbo.StudentProfiles WHERE AppUserId = @UserId
UNION ALL SELECT 'UserSchools', COUNT(*) FROM dbo.UserSchools WHERE AppUserId = @UserId
UNION ALL SELECT 'LehrerNiveauZuordnungen', COUNT(*) FROM dbo.LehrerNiveauZuordnungen WHERE LehrerId = @UserId
UNION ALL SELECT 'ExerciseAttempts(StudentId)', COUNT(*) FROM dbo.ExerciseAttempts WHERE StudentId = @UserId
UNION ALL SELECT 'LessonProgresses(StudentId)', COUNT(*) FROM dbo.LessonProgresses WHERE StudentId = @UserId
UNION ALL SELECT 'ExamAttempts(StudentId)', COUNT(*) FROM dbo.ExamAttempts WHERE StudentId = @UserId
UNION ALL SELECT 'QuizAssignments(StudentId)', COUNT(*) FROM dbo.QuizAssignments WHERE StudentId = @UserId
UNION ALL SELECT 'QuizAttempts(StudentId)', COUNT(*) FROM dbo.QuizAttempts WHERE StudentId = @UserId
UNION ALL SELECT 'KlassenAbschlussEntscheidungen(StudentId)', COUNT(*) FROM dbo.KlassenAbschlussEntscheidungen WHERE StudentId = @UserId
UNION ALL SELECT 'KlassenAbschlussEntscheidungen(EntscheidetVonUserId)', COUNT(*) FROM dbo.KlassenAbschlussEntscheidungen WHERE EntscheidetVonUserId = @UserId
UNION ALL SELECT 'RatenZahlungen(StudentId)', COUNT(*) FROM dbo.RatenZahlungen WHERE StudentId = @UserId
UNION ALL SELECT 'RatenZahlungen(ErfasstVonUserID)', COUNT(*) FROM dbo.RatenZahlungen WHERE ErfasstVonUserID = @UserId
UNION ALL SELECT 'Abrechnungsperioden(AutomatischAusgeloestVonUserId)', COUNT(*) FROM dbo.Abrechnungsperioden WHERE AutomatischAusgeloestVonUserId = @UserId;
