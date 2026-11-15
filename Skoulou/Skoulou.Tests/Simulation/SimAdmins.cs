using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Simulation;

/// <summary>S3: Hauptadministrator und Administratoren (/admin/administrateurs), gleichzeitige Kontoanlagen.</summary>
internal sealed partial class Simulation
{
    /// <summary>Datumsgebundene Schritte der Administratorenverwaltung (nur der Hauptadministrator).</summary>
    private async Task AdminEreignisseAsync(DateOnly tag)
    {
        var v = api.Administratoren;
        if (tag == new DateOnly(ErstesJahr, 11, 15))
        {
            await SollAsync("Doublon archivieren", () => v.LoeschenAsync(Admin, AdminDoppelt));
            await AbgelehntAsync("Archivierter Admin arbeitet", () => api.Schulen.SucheAsync(AdminDoppelt, null, null));
            Notiere("Administrateur en double supprimé (compte archivé).");
        }
        if (tag == new DateOnly(ErstesJahr + 1, 2, 1))
        {
            await SollAsync("Admin sperren", () => v.SperrenAsync(Admin, Admin4, true));
            await AbgelehntAsync("Gesperrter Admin arbeitet", () => api.Schulen.SucheAsync(Admin4, null, null));
            await AbgelehntAsync("Admin entsperrt Admin (Comptes)", () => api.AdminKonten.EntsperrenAsync(Admin2, Admin4));
            await AbgelehntAsync("Admin entsperrt Admin (Administrateurs)", () => v.SperrenAsync(Admin3, Admin4, false));
            Notiere("Administrateur Sélom Ahadji bloqué.");
        }
        if (tag == new DateOnly(ErstesJahr + 1, 3, 1))
        {
            await SollAsync("Admin entsperren", () => v.SperrenAsync(Admin, Admin4, false));
            await SollAsync("Entsperrter Admin arbeitet", () => api.Schulen.SucheAsync(Admin4, null, null));
            await SollAsync("Admin ändern", () => v.BearbeitenAsync(Admin, Admin4, new AdminEingabe("Sélom", "Ahadji-Kodjo", "selom.ahadji@skoulou-sim.tg")));
        }
        if (tag == new DateOnly(ErstesJahr + 1, 6, 15))
        {
            await SollAsync("Passwort eines Admins zurücksetzen", () => api.AdminKonten.PasswortZuruecksetzenAsync(Admin, Admin3));
            await AbgelehntAsync("Admin setzt Passwort eines Admins zurück", () => api.AdminKonten.PasswortZuruecksetzenAsync(Admin2, Admin3));
        }
        if (tag == new DateOnly(ErstesJahr + 2, 9, 1))
        {
            await SollAsync("Admin archivieren", () => v.LoeschenAsync(Admin, Admin4));
            await AbgelehntAsync("Archivierter Admin arbeitet", () => api.Schulen.SucheAsync(Admin4, null, null));
            await AbgelehntAsync("Archivierten Admin ändern", () => v.BearbeitenAsync(Admin, Admin4, new AdminEingabe("Sélom", "Ahadji", "selom@skoulou-sim.tg")), nurKeinZugriff: false);
            Notiere("Administrateur Sélom Ahadji supprimé (compte archivé).");
        }
        if (tag == new DateOnly(ErstesJahr + 3, 9, 1))
        {
            Admin5 = await AdminAnlegenAsync("Afi", "Gbadoé");
            await ErsteAnmeldungAsync(Admin5);
            await AdminVerwaltungAbgelehntAsync();
        }
    }

    /// <summary>Nur der Hauptadministrator verwaltet Administratoren; ihn selbst kann niemand sperren, archivieren oder ändern.</summary>
    private async Task AdminVerwaltungAbgelehntAsync()
    {
        var v = api.Administratoren;
        var andere = new[] { Admin2, Admin3 }.Concat(Admin5 == Guid.Empty ? Array.Empty<Guid>() : new[] { Admin5 }).ToList();
        foreach (var a in andere)
        {
            await AbgelehntAsync("Admin legt Admin an", () => v.AnlegenAsync(a, new AdminEingabe("Ama", "Kpeglo", "ama.kpeglo@skoulou-sim.tg")));
            await AbgelehntAsync("Admin liest « Administrateurs »", () => v.ListeAsync(a));
            await AbgelehntAsync("Admin sperrt Hauptadmin", () => v.SperrenAsync(a, Admin, true));
            await AbgelehntAsync("Admin archiviert Hauptadmin", () => v.LoeschenAsync(a, Admin));
            await AbgelehntAsync("Admin sperrt Hauptadmin (Comptes)", () => api.AdminKonten.SperrenAsync(a, Admin, "Test de droits."), nurKeinZugriff: false);
            await AbgelehntAsync("Admin setzt Passwort des Hauptadmins zurück", () => api.AdminKonten.PasswortZuruecksetzenAsync(a, Admin), nurKeinZugriff: false);
            await AbgelehntAsync("Admin entsperrt Hauptadmin", () => api.AdminKonten.EntsperrenAsync(a, Admin), nurKeinZugriff: false);
        }
        await AbgelehntAsync("Admin sperrt anderen Admin", () => v.SperrenAsync(Admin2, Admin3, true));
        await AbgelehntAsync("Hauptadmin sperrt sich selbst", () => v.SperrenAsync(Admin, Admin, true), nurKeinZugriff: false);
        await AbgelehntAsync("Hauptadmin archiviert sich selbst", () => v.LoeschenAsync(Admin, Admin), nurKeinZugriff: false);
        await AbgelehntAsync("Hauptadmin ändert sich in « Administrateurs »", () => v.BearbeitenAsync(Admin, Admin, new AdminEingabe("Admin", "SKOULOU", "admin@skoulou-sim.tg")), nurKeinZugriff: false);
        await AbgelehntAsync("Hauptadmin sperrt sich (Comptes)", () => api.AdminKonten.SperrenAsync(Admin, Admin, null), nurKeinZugriff: false);
        foreach (var r in Responsables.Take(1)) await AbgelehntAsync("Responsable legt Admin an", () => v.AnlegenAsync(r, new AdminEingabe("Ama", "Kpeglo", "ama.kpeglo@skoulou-sim.tg")));
        foreach (var s in Schulen.Take(1)) await AbgelehntAsync("Direction liest « Administrateurs »", () => v.ListeAsync(s.Direktor));
    }

    /// <summary>
    /// Vor jedem Schuljahr: drei Schulen legen im selben Moment einen Schüler und eine Lehrkraft mit gleichem Namen an,
    /// zwei Schüler registrieren sich gleichzeitig mit gleichem Namen (danach abgelehnt). Die Dienste wiederholen selbst;
    /// eine Meldung « enregistrement simultané » ist ein Befund (die Simulation wiederholt nichts).
    /// </summary>
    private async Task GleichzeitigeAnlagenAsync(int startJahr)
    {
        var aktiv = Schulen.Where(s => !s.Suspendiert && s.Jahre.Count > 0).ToList();
        var r = startJahr - ErstesJahr;
        var drei = Enumerable.Range(0, 3).Select(i => aktiv[(r * 5 + i * 11) % aktiv.Count]).Distinct().ToList();
        var schueler = new List<Task<Guid>>();
        var lehrer = new List<Task<Guid>>();
        var selbst = new List<Task<Guid>>();
        foreach (var s in drei)
        {
            var klasse = s.Jahr.Klassen.First(k => k.Session <= 1);
            var z1 = new Zufall(Zufall.Kombiniere(Startwert, s.Nr, startJahr, 71));
            var z2 = new Zufall(Zufall.Kombiniere(Startwert, s.Nr, startJahr, 72));
            schueler.Add(Task.Run(async () => (await SchuelerAnlegenAsync(s, z1, klasse, s.Direktor, name: ("Kossi", "Mensah", Geschlecht.Maennlich)))?.Id ?? Guid.Empty));
            lehrer.Add(Task.Run(async () =>
            {
                try { return await PersonAsync(s, z2, Rollen.Lehrer, false, ("Akossiwa", "Agbeko", Geschlecht.Weiblich)); }
                catch (InvalidOperationException) { return Guid.Empty; }
            }));
        }
        foreach (var (s, i) in drei.Take(2).Select((s, i) => (s, i)))
            selbst.Add(Task.Run(() => SelbstRegistrierenAsync(s, "Yawa", "Dossou", startJahr * 10 + i)));
        await Task.WhenAll(schueler.Concat(lehrer).Concat(selbst));
        await KontenVerschiedenAsync($"Schüler gleichzeitig ({startJahr})", schueler.Select(t => t.Result).ToList());
        await KontenVerschiedenAsync($"Lehrkräfte gleichzeitig ({startJahr})", lehrer.Select(t => t.Result).ToList());
        await KontenVerschiedenAsync($"Selbstregistrierungen gleichzeitig ({startJahr})", selbst.Select(t => t.Result).ToList());
        foreach (var (s, id) in drei.Zip(selbst.Select(t => t.Result)).Where(x => x.Second != Guid.Empty))
            await SollAsync("Doublon ablehnen", () => api.Schueler.AblehnenAsync(s.Direktor, id, "Doublon : l'élève est déjà inscrit."));
    }

    private async Task<Guid> SelbstRegistrierenAsync(SimSchule s, string vor, string nach, int nr)
    {
        var pw = "eleve" + nr + "x";
        var e = await SollAsync("Selbstregistrierung (gleichzeitig)", () => api.Registrierung.RegistrierenAsync(new RegistrierungsEingabe(RegistrierungsArt.Eleve,
            vor, nach, null, null, null, pw, pw, true, SchuleId: s.Id, Geburtsdatum: new DateOnly(2010, 3, 14),
            ElternTelefon: SimNamen.Telefon(new Zufall(nr))), $"10.{s.Nr}.200.{nr % 250}"));
        if (e is null) return Guid.Empty;
        return await d.MitDbAsync(db => db.Users.Where(k => k.UserName == e.Identifiant).Select(k => k.Id).SingleAsync());
    }

    // ---------- Invariante: Hauptadministrator

    /// <summary>
    /// Genau ein Hauptadministrator (das Seed-Konto), aktiv, nie gesperrt/archiviert; jede Verwaltung eines Admin-Kontos
    /// (angelegt, geändert, gesperrt, entsperrt, archiviert, Passwort) stammt vom Hauptadministrator; die Liste
    /// « Administrateurs » zeigt genau die nicht archivierten Admins mit dem erwarteten Status.
    /// </summary>
    private async Task HauptAdminAsync()
    {
        var daten = await d.MitDbAsync(async db => new
        {
            Admins = await db.Users.Where(k => k.Rolle == Rollen.SuperAdmin).Select(k => new { k.Id, k.Status, k.IstHauptAdmin }).ToListAsync(),
            Audit = await (from a in db.AuditEintraege
                           join k in db.Users on a.ZielId equals k.Id
                           where a.ZielTyp == nameof(Konto) && k.Rolle == Rollen.SuperAdmin
                           select new { a.AkteurId, a.Aktion, a.ZielId }).ToListAsync()
        });
        var haupt = daten.Admins.Where(a => a.IstHauptAdmin).ToList();
        Pruefe(haupt.Count == 1 && haupt[0].Id == Admin && haupt[0].Status == KontoStatus.Actif,
            $"Hauptadministrator: {haupt.Count} Konto/Konten markiert, erwartet genau das Seed-Konto (aktiv).");
        var verwaltung = new[]
        {
            AuditAktionen.KontoAngelegt, AuditAktionen.KontoGesperrt, AuditAktionen.KontoArchiviert, AuditAktionen.PasswortZurueckgesetzt,
            OrgAudit.KontoGeaendert, PlattformAudit.KontoEntsperrt
        };
        var eintraege = daten.Audit.Where(a => verwaltung.Contains(a.Aktion) && a.AkteurId != a.ZielId && a.AkteurId is not null).ToList();
        var fremd = eintraege.Where(a => a.AkteurId != Admin).ToList();
        Pruefe(fremd.Count == 0, $"Administratoren: {fremd.Count} Verwaltungsschritt(e) nicht vom Hauptadministrator ({string.Join(", ", fremd.Take(3).Select(f => f.Aktion))}).");
        var amHaupt = daten.Audit.Count(a => a.ZielId == Admin && (a.Aktion == AuditAktionen.KontoGesperrt || a.Aktion == AuditAktionen.KontoArchiviert));
        Pruefe(amHaupt == 0, $"Hauptadministrator: {amHaupt} Sperre/Archivierung im Journal.");

        var erwartet = new Dictionary<Guid, KontoStatus>
        {
            [Admin] = KontoStatus.Actif, [Admin2] = KontoStatus.Actif, [Admin3] = KontoStatus.Actif, [AdminZwilling] = KontoStatus.Actif,
            [Admin4] = KontoStatus.Archive, [AdminDoppelt] = KontoStatus.Archive
        };
        if (Admin5 != Guid.Empty) erwartet[Admin5] = KontoStatus.Actif;
        foreach (var (id, status) in erwartet)
            Pruefe(daten.Admins.FirstOrDefault(a => a.Id == id)?.Status == status, $"Administrateur {id}: Status {daten.Admins.FirstOrDefault(a => a.Id == id)?.Status} statt {status}.");
        var liste = await api.Administratoren.ListeAsync(Admin);
        Pruefe(liste.Select(a => a.Id).ToHashSet().SetEquals(erwartet.Where(e => e.Value != KontoStatus.Archive).Select(e => e.Key)) && liste.Count > 0 && liste[0].Id == Admin && liste[0].IstHauptAdmin,
            $"« Administrateurs »: {liste.Count} Einträge, erwartet {erwartet.Count(e => e.Value != KontoStatus.Archive)} mit dem Hauptadministrator zuerst.");
        var vorher = Fehler.Count;
        await AdminVerwaltungAbgelehntAsync();
        Pruefe(Fehler.Count == vorher, $"Administratoren: {Fehler.Count - vorher} unerlaubte Verwaltung(en) nicht abgelehnt (siehe Befunde).");
        Geprueft.Add($"Hauptadministrator: genau einer, aktiv, nie gesperrt/archiviert; {eintraege.Count} Verwaltungsschritte an Admin-Konten alle vom Hauptadministrator; Liste « Administrateurs » = {liste.Count} nicht archivierte; Verbote für Admins/Responsable/Direction abgelehnt");
    }
}
