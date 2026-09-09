SET QUOTED_IDENTIFIER ON;

SELECT COUNT(*) AS MatchingUsers
FROM dbo.AspNetUsers
WHERE NormalizedEmail = N'DODJI_ETSE@WEB.DE'
   OR Email = N'dodji_etse@web.de';
