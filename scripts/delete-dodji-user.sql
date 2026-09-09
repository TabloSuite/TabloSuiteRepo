SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @UserId uniqueidentifier = (
	SELECT TOP 1 Id
	FROM dbo.AspNetUsers
	WHERE NormalizedEmail = N'DODJI_ETSE@WEB.DE'
	   OR Email = N'dodji_etse@web.de'
);

IF @UserId IS NULL
BEGIN
	SELECT CAST(0 AS int) AS DeletedUsers;
	RETURN;
END;

BEGIN TRANSACTION;

DELETE FROM dbo.StudentProfiles WHERE AppUserId = @UserId;
DELETE FROM dbo.UserSchools WHERE AppUserId = @UserId;
DELETE FROM dbo.LehrerNiveauZuordnungen WHERE LehrerId = @UserId;

DELETE FROM dbo.ExerciseAttempts WHERE StudentId = @UserId;
DELETE FROM dbo.LessonProgresses WHERE StudentId = @UserId;
DELETE FROM dbo.ExamAttempts WHERE StudentId = @UserId;
DELETE FROM dbo.QuizAssignments WHERE StudentId = @UserId;
DELETE FROM dbo.QuizAttempts WHERE StudentId = @UserId;
DELETE FROM dbo.KlassenAbschlussEntscheidungen WHERE StudentId = @UserId;
UPDATE dbo.KlassenAbschlussEntscheidungen SET EntscheidetVonUserId = NULL WHERE EntscheidetVonUserId = @UserId;
DELETE FROM dbo.RatenZahlungen WHERE StudentId = @UserId;
UPDATE dbo.RatenZahlungen SET ErfasstVonUserID = NULL WHERE ErfasstVonUserID = @UserId;
UPDATE dbo.Abrechnungsperioden SET AutomatischAusgeloestVonUserId = NULL WHERE AutomatischAusgeloestVonUserId = @UserId;

DELETE FROM dbo.AspNetUserClaims WHERE UserId = @UserId;
DELETE FROM dbo.AspNetUserLogins WHERE UserId = @UserId;
DELETE FROM dbo.AspNetUserTokens WHERE UserId = @UserId;
DELETE FROM dbo.AspNetUserRoles WHERE UserId = @UserId;

DELETE FROM dbo.AspNetUsers WHERE Id = @UserId;

SELECT @@ROWCOUNT AS DeletedUsers;

COMMIT TRANSACTION;
