using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Organisation;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Nur mit SKOULOU_SQL_TESTS=1: Unique-Indizes, auf die sich der Bereich Plattform verlässt (Schulname normalisiert,
/// PostmarkId), und Übersetzbarkeit aller Listen-/Kennzahlabfragen des Bereichs auf SQL Server.
/// </summary>
public class PlattformSqlTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    [SqlFact]
    public async Task Schulname_und_PostmarkId_sind_eindeutig()
    {
        var name = "sql-" + Guid.NewGuid().ToString("N")[..8];
        await using (var db = datenbank.Kontext())
        {
            db.Schulen.Add(new Schule { Name = name, NameNormalisiert = name, Ort = "Lomé" });
            db.Nachrichten.AddRange(new Nachricht { Von = "a@exemple.tg", Betreff = "1", PostmarkId = name },
                new Nachricht { Von = "a@exemple.tg", Betreff = "ohne 1" }, new Nachricht { Von = "a@exemple.tg", Betreff = "ohne 2" });
            await db.SaveChangesAsync();
        }
        await using (var db = datenbank.Kontext())
        {
            db.Schulen.Add(new Schule { Name = name.ToUpperInvariant(), NameNormalisiert = name, Ort = "Kara" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = datenbank.Kontext())
        {
            db.Nachrichten.Add(new Nachricht { Von = "b@exemple.tg", Betreff = "2", PostmarkId = name });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [SqlFact]
    public async Task Abfragen_des_Bereichs_sind_uebersetzbar()
    {
        var fabrik = new SqlFabrik(datenbank);
        var zeit = new TestZeit();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var zugriff = new Zugriff(fabrik);
        var audit = new AuditService(zeit);
        var mails = new PlattformMails(new MailAusgangService(fabrik, new FakeVersender(), new MailSignal(), zeit, NullLogger<MailAusgangService>.Instance),
            Options.Create(new AppOptionen { BaseUrl = "https://skoulou.test" }), NullLogger<PlattformMails>.Instance);
        var hasher = new PasswordHasher<Konto>();
        var normal = new UpperInvariantLookupNormalizer();
        var konten = new PlattformKontoFabrik(new KontoFabrik(hasher, normal, zeit), hasher, normal, zeit);

        var kennung = Guid.NewGuid().ToString("N")[..8];
        Konto admin, resp;
        Guid schuleId;
        await using (var db = datenbank.Kontext())
        {
            admin = new Konto { Id = Guid.NewGuid(), UserName = "adm" + kennung, NormalizedUserName = "ADM" + kennung.ToUpperInvariant(), Rolle = Rollen.SuperAdmin, SecurityStamp = "x" };
            resp = new Konto { Id = Guid.NewGuid(), UserName = "resp" + kennung, NormalizedUserName = "RESP" + kennung.ToUpperInvariant(), Rolle = Rollen.Responsable, SecurityStamp = "x",
                Email = "resp@exemple.tg", NormalizedEmail = "RESP@EXEMPLE.TG" };
            var schule = new Schule { Name = "Ecole " + kennung, NameNormalisiert = "ecole " + kennung, Ort = "Lomé", Status = SchulStatus.Active, ResponsableId = resp.Id };
            schuleId = schule.Id;
            db.Users.AddRange(admin, resp);
            await db.SaveChangesAsync(); // erst die Konten (Schule.ResponsableId verweist darauf)
            db.Schulen.Add(schule);
            db.Users.Add(new Konto { UserName = "dir" + kennung, NormalizedUserName = "DIR" + kennung.ToUpperInvariant(), Rolle = Rollen.Direktor, SchuleId = schule.Id,
                SecurityStamp = "x", Email = "dir@exemple.tg", NormalizedEmail = "DIR@EXEMPLE.TG", LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5) });
            db.Nachrichten.Add(new Nachricht { Von = "dir@exemple.tg", Betreff = "Question", TextHtml = "x", SchuleId = schule.Id });
            db.Medien.Add(new Medium { SchuleId = schule.Id, BesitzerId = admin.Id, BlobName = "schulen/" + kennung, Dateiname = "a.png", Bytes = 10 });
            db.FehlerProtokolle.Add(new FehlerProtokoll { ZeitUtc = zeit.Jetzt.UtcDateTime, Nachricht = "Erreur " + kennung, SchuleId = schule.Id });
            db.AuditEintraege.Add(new AuditEintrag { ZeitUtc = zeit.Jetzt.UtcDateTime, AkteurId = admin.Id, SchuleId = schule.Id, Aktion = "schule.validiert", ZielTyp = "Schule", Text = "ok " + kennung });
            await db.SaveChangesAsync();
        }

        var schulen = new SchulVerwaltungService(fabrik, zugriff, audit, mails);
        // S3: das Direktionskonto der Zeile kommt über MIN(Id) im Index (vorher Tabellen-Scan je Schule).
        Assert.Equal("dir@exemple.tg", (await schulen.SucheAsync(admin.Id, kennung, SchulVerwaltungService.FilterActive)).Eintraege.Single().DirektorEmail);
        Assert.True((await schulen.ZaehlerAsync(admin.Id)).Active >= 1);
        Assert.NotEmpty(await schulen.ResponsableOptionenAsync(admin.Id));

        var verwaltung = new KontenVerwaltungService(fabrik, zugriff, audit, konten, mails, new SitzungsPruefung(fabrik, cache), zeit);
        var treffer = (await verwaltung.SucheAsync(admin.Id, "dir" + kennung, null)).Eintraege.Single();
        Assert.True(treffer.Verriegelt);
        Assert.Single((await verwaltung.SucheAsync(admin.Id, "dir@exemple", Rollen.Direktor, schuleId)).Eintraege);
        Assert.NotEmpty((await verwaltung.ResponsablesAsync(admin.Id, "resp" + kennung)).Eintraege.Single().Schulen);
        Assert.NotEmpty(await verwaltung.ZaehlerAsync(admin.Id));
        Assert.NotEmpty(await verwaltung.SchulOptionenAsync(admin.Id));

        var messagerie = new MessagerieService(fabrik, zugriff, audit, mails, new TestOptionen<PostmarkOptionen>(new PostmarkOptionen()), zeit);
        Assert.Single((await messagerie.SucheAsync(resp.Id, Postfach.Eingang, "Quest", Postfach.Ungelesen)).Eintraege);
        Assert.Equal(1, await messagerie.UngeleseneAsync(resp.Id));
        Assert.Single(await messagerie.EmpfaengerAsync(resp.Id, schuleId));

        var journal = new ProtokollService(fabrik, zugriff, zeit);
        Assert.Single((await journal.FehlerAsync(admin.Id, new ProtokollFilter(kennung, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), schuleId, true))).Eintraege);
        Assert.Single((await journal.AuditAsync(admin.Id, new ProtokollFilter(kennung, SchuleId: schuleId), "schule")).Eintraege);

        var medien = new MedienVerwaltungService(fabrik, zugriff, audit, new SpeicherAttrappe());
        Assert.True((await medien.SummenAsync(admin.Id)).Ungenutzt >= 1);
        Assert.Single((await medien.SucheAsync(admin.Id, "a.png", MedienVerwaltungService.FilterUngenutzt, schuleId)).Eintraege);

        var uebersicht = new PlattformUebersicht(fabrik, zugriff, zeit);
        Assert.True((await uebersicht.ZahlenAsync(admin.Id)).Schulen.Alle >= 1);
        Assert.Single((await uebersicht.MeineSchulenAsync(resp.Id)).Single().Direktion);

        var annonces = new AnkuendigungService(fabrik, zugriff, cache, zeit);
        await annonces.SpeichernAsync(admin.Id, null, new AnnonceEingabe("Titre " + kennung, "Texte assez long", AnkuendigungsStufe.Info, ZielRollen.Alle, null,
            zeit.Jetzt.UtcDateTime.AddMinutes(-1), zeit.Jetzt.UtcDateTime.AddDays(1)));
        Assert.Single((await annonces.SucheAsync(admin.Id, kennung, AnkuendigungService.FilterAktiv)).Eintraege);
        Assert.Contains(await annonces.AktiveAsync(null, null), a => a.Titel == "Titre " + kennung);

        var registrierung = new RegistrierungService(fabrik, konten, audit, mails, new RegistrierungsBremse(zeit), zeit);
        Assert.Single(await registrierung.SchulenSuchenAsync("ecole " + kennung));
    }
}
