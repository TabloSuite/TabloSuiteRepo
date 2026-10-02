-- Löscht optional alle Schüler aus der SKOULOUAPP-Datenbank.
-- Standardmäßig wird NICHT gelöscht. Erst @ExecuteDelete = 1 führt die Löschung aus.
-- Vor Ausführung in Azure SQL: Backup/Export erstellen.

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @ExecuteDelete bit = 0;

DECLARE @StudentIds TABLE
(
	Id uniqueidentifier NOT NULL PRIMARY KEY
);

INSERT INTO @StudentIds (Id)
SELECT DISTINCT u.Id
FROM dbo.AspNetUsers AS u
INNER JOIN dbo.AspNetUserRoles AS ur ON ur.UserId = u.Id
INNER JOIN dbo.AspNetRoles AS r ON r.Id = ur.RoleId
WHERE r.Name = N'Schueler';

SELECT COUNT(*) AS StudentsToDelete
FROM @StudentIds;

SELECT u.Id, u.Email, u.FullName
FROM dbo.AspNetUsers AS u
INNER JOIN @StudentIds AS s ON s.Id = u.Id
ORDER BY u.Email;

IF @ExecuteDelete = 0
BEGIN
	PRINT 'Prüfmodus: Es wurde nichts gelöscht. Setze @ExecuteDelete = 1, um die Schüler endgültig zu löschen.';
	RETURN;
END;

BEGIN TRANSACTION;

DELETE ea
FROM dbo.ExerciseAttempts AS ea
INNER JOIN @StudentIds AS s ON s.Id = ea.StudentId;

DELETE lp
FROM dbo.LessonProgresses AS lp
INNER JOIN @StudentIds AS s ON s.Id = lp.StudentId;

DELETE ea
FROM dbo.ExamAttempts AS ea
INNER JOIN @StudentIds AS s ON s.Id = ea.StudentId;

DELETE qa
FROM dbo.QuizAssignments AS qa
INNER JOIN @StudentIds AS s ON s.Id = qa.StudentId;

DELETE qt
FROM dbo.QuizAttempts AS qt
INNER JOIN @StudentIds AS s ON s.Id = qt.StudentId;

DELETE kae
FROM dbo.KlassenAbschlussEntscheidungen AS kae
INNER JOIN @StudentIds AS s ON s.Id = kae.StudentId;

DELETE rz
FROM dbo.RatenZahlungen AS rz
INNER JOIN @StudentIds AS s ON s.Id = rz.StudentId;

DELETE le
FROM dbo.LevelEnrollments AS le
INNER JOIN @StudentIds AS s ON s.Id = le.StudentId;

DELETE sp
FROM dbo.StudentProfiles AS sp
INNER JOIN @StudentIds AS s ON s.Id = sp.AppUserId;

DELETE us
FROM dbo.UserSchools AS us
INNER JOIN @StudentIds AS s ON s.Id = us.AppUserId;

DELETE uc
FROM dbo.AspNetUserClaims AS uc
INNER JOIN @StudentIds AS s ON s.Id = uc.UserId;

DELETE ul
FROM dbo.AspNetUserLogins AS ul
INNER JOIN @StudentIds AS s ON s.Id = ul.UserId;

DELETE ut
FROM dbo.AspNetUserTokens AS ut
INNER JOIN @StudentIds AS s ON s.Id = ut.UserId;

DELETE ur
FROM dbo.AspNetUserRoles AS ur
INNER JOIN @StudentIds AS s ON s.Id = ur.UserId;

DELETE u
FROM dbo.AspNetUsers AS u
INNER JOIN @StudentIds AS s ON s.Id = u.Id;

COMMIT TRANSACTION;

SELECT COUNT(*) AS RemainingStudents
FROM dbo.AspNetUsers AS u
INNER JOIN dbo.AspNetUserRoles AS ur ON ur.UserId = u.Id
INNER JOIN dbo.AspNetRoles AS r ON r.Id = ur.RoleId
WHERE r.Name = N'Schueler';
