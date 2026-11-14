using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Zeile der Kontenliste (nie mit Telefonnummer – private Nummern werden nicht angezeigt).</summary>
public sealed record KontoZeile(
    Guid Id, string Name, string Identifiant, string? Email, string Rolle, KontoStatus Status, Guid? SchuleId, string? Schule,
    bool SelbstRegistriert, DateTime? LetzteAnmeldungUtc, bool Verriegelt);

/// <summary>Zeile der Responsables mit ihren Schulen.</summary>
public sealed record ResponsableZeile(Guid Id, string Name, string Identifiant, string? Email, KontoStatus Status, IReadOnlyList<string> Schulen);

/// <summary>Eingabe « Nouveau responsable ».</summary>
public sealed record ResponsableEingabe(string? Vorname, string? Nachname, string? Identifiant, string? Email, string? Telefon);

/// <summary>Auswahl einer Schule (mit aktuellem Responsable).</summary>
public sealed record SchulOption(Guid Id, string Name, string Ort, SchulStatus Status, string? Responsable);

/// <summary>
/// Konten der Plattform (Bauplan §3, Fachkonzept 01 §3.10/3.12): suchen, sperren (nur SuperAdmin), entsperren
/// (SuperAdmin; Responsable nur Konten seiner Schulen), Startpasswort neu setzen (nur SuperAdmin), Responsables
/// anlegen und Schulen zuweisen. Ein Responsable darf nichts Kritisches sperren oder löschen.
/// </summary>
public sealed class KontenVerwaltungService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    Zugriff zugriff,
    AuditService audit,
    PlattformKontoFabrik konten,
    PlattformMails mails,
    SitzungsPruefung sitzungen,
    TimeProvider zeit)
{
    public const string FilterGesperrt = "bloques";
    public const string FilterWartend = "en-attente";

    /// <summary>Anzahl Konten je Rolle (Chips) und gesperrte.</summary>
    public async Task<IReadOnlyDictionary<string, int>> ZaehlerAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var je = await db.Users.Where(k => k.Status != KontoStatus.Archive).GroupBy(k => k.Rolle)
            .Select(g => new { g.Key, Anzahl = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Anzahl, ct);
        je[FilterGesperrt] = await db.Users.CountAsync(k => k.Status == KontoStatus.Bloque, ct);
        je[FilterWartend] = await db.Users.CountAsync(k => k.Status == KontoStatus.EnAttente, ct);
        return je;
    }

    public async Task<(IReadOnlyList<KontoZeile> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, string? suche, string? filter, Guid? schuleId = null,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Users.AsNoTracking();
        q = filter switch
        {
            FilterGesperrt => q.Where(k => k.Status == KontoStatus.Bloque),
            FilterWartend => q.Where(k => k.Status == KontoStatus.EnAttente),
            { } rolle when Rollen.Alle.Contains(rolle) => q.Where(k => k.Rolle == rolle && k.Status != KontoStatus.Archive),
            _ => q
        };
        if (schuleId is { } s) q = q.Where(k => k.SchuleId == s);
        if (PlattformListen.Suchtext(suche) is { } t)
        {
            var normal = t.ToUpperInvariant();
            q = q.Where(k => k.Vorname.Contains(t) || k.Nachname.Contains(t) || (k.Vorname + " " + k.Nachname).Contains(t)
                             || k.NormalizedUserName!.Contains(normal) || k.NormalizedEmail!.Contains(normal));
        }
        var jetzt = zeit.GetUtcNow();
        var abfrage = q.OrderBy(k => k.Nachname).ThenBy(k => k.Vorname)
            .Select(k => new KontoZeile(k.Id, k.Vorname + " " + k.Nachname, k.UserName!, k.Email, k.Rolle, k.Status, k.SchuleId,
                k.Schule != null ? k.Schule.Name : null, k.SelbstRegistriert, k.LetzteAnmeldungUtc, k.LockoutEnd != null && k.LockoutEnd > jetzt));
        return await PlattformListen.SeiteAsync(abfrage, ueberspringen, anzahl, ct);
    }

    /// <summary>Konto sperren (nur SuperAdmin; nie sich selbst, nie einen anderen Administrateur). Sitzungen enden sofort.</summary>
    public async Task SperrenAsync(Guid akteurId, Guid kontoId, string? grund, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        if (kontoId == akteur.Id) throw new FachFehler("Vous ne pouvez pas bloquer votre propre compte.");
        var text = Eingabe.Text(grund, 300, "le motif", feld: "motif");
        await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct) ?? throw new FachFehler("Ce compte est introuvable.");
            if (konto.Rolle == Rollen.SuperAdmin) throw new FachFehler("Un compte administrateur ne peut pas être bloqué ici.");
            if (konto.Status == KontoStatus.Bloque) return;
            if (konto.Status == KontoStatus.Archive) throw new FachFehler("Ce compte est archivé.");
            konto.Status = KontoStatus.Bloque;
            konto.AktiveSitzungId = Guid.NewGuid();
            audit.Erfasse(db, akteur.Id, konto.SchuleId, AuditAktionen.KontoGesperrt, nameof(Konto), konto.Id,
                $"Compte {konto.VollerName} ({Rollen.Bezeichnung(konto.Rolle)}) bloqué{(text is null ? "" : $". Motif : {text}")}.");
            await db.SaveChangesAsync(ct);
        }, ct);
        sitzungen.Vergessen(kontoId);
    }

    /// <summary>Gesperrtes Konto freigeben: SuperAdmin alle (außer sich selbst), Responsable nur Schulkonten seiner Schulen.</summary>
    public async Task EntsperrenAsync(Guid akteurId, Guid kontoId, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        if (kontoId == akteur.Id) throw new FachFehler("Vous ne pouvez pas modifier votre propre compte ici.");
        await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct) ?? throw new FachFehler("Ce compte est introuvable.");
            if (!akteur.IstAdmin)
            {
                if (!Rollen.IstSchulRolle(konto.Rolle) || konto.SchuleId is not { } s) throw new KeinZugriffFehler("Ce compte n'appartient pas à vos écoles.");
                await PlattformRechte.PruefeBetreuteSchuleAsync(db, akteur, s, ct);
            }
            if (konto.Status == KontoStatus.EnAttente)
                throw new FachFehler(konto.Rolle == Rollen.Direktor
                    ? "Ce compte sera activé avec la validation de l'établissement."
                    : "Cette inscription doit être validée par la direction de l'établissement.");
            await PruefeAdminZielAsync(db, akteur, konto, ct);
            if (konto.Status != KontoStatus.Bloque) throw new FachFehler("Ce compte n'est pas bloqué.");
            konto.Status = KontoStatus.Actif;
            konto.AccessFailedCount = 0;
            konto.LockoutEnd = null;
            audit.Erfasse(db, akteur.Id, konto.SchuleId, PlattformAudit.KontoEntsperrt, nameof(Konto), konto.Id,
                $"Compte {konto.VollerName} ({Rollen.Bezeichnung(konto.Rolle)}) débloqué.");
            await db.SaveChangesAsync(ct);
        }, ct);
        sitzungen.Vergessen(kontoId);
    }

    /// <summary>Neues Startpasswort (nur SuperAdmin, nie das eigene): einmal anzeigen, Sitzungen enden, Audit.</summary>
    public async Task<Zugang> PasswortZuruecksetzenAsync(Guid akteurId, Guid kontoId, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        if (kontoId == akteur.Id) throw new FachFehler("Changez votre propre mot de passe depuis votre profil.");
        var zugang = await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct) ?? throw new FachFehler("Ce compte est introuvable.");
            if (konto.Status == KontoStatus.Archive) throw new FachFehler("Ce compte est archivé.");
            await PruefeAdminZielAsync(db, akteur, konto, ct);
            var passwort = konten.Basis.NeuesStartpasswort(konto);
            audit.Erfasse(db, akteur.Id, konto.SchuleId, AuditAktionen.PasswortZurueckgesetzt, nameof(Konto), konto.Id,
                $"Nouveau mot de passe de départ pour {konto.VollerName} ({Rollen.Bezeichnung(konto.Rolle)}).");
            await db.SaveChangesAsync(ct);
            return new Zugang(konto.Id, konto.VollerName, Rollen.Bezeichnung(konto.Rolle), konto.UserName!, passwort);
        }, ct);
        sitzungen.Vergessen(kontoId);
        return zugang;
    }

    /// <summary>
    /// Andere Administratoren verwaltet nur der Hauptadministrator (Seite « Administrateurs »); den Hauptadministrator
    /// selbst kann niemand entsperren oder zurücksetzen.
    /// </summary>
    private static async Task PruefeAdminZielAsync(SkoulouDbContext db, Akteur akteur, Konto konto, CancellationToken ct)
    {
        if (konto.Rolle != Rollen.SuperAdmin) return;
        if (konto.IstHauptAdmin) throw new FachFehler(AdministratorenService.MeldungZiel);
        await AdministratorenService.SichereHauptAdminAsync(db, ct);
        if (!await db.Users.AnyAsync(k => k.Id == akteur.Id && k.IstHauptAdmin, ct))
            throw new KeinZugriffFehler(AdministratorenService.MeldungNurHauptAdmin);
    }

    // ---------- Responsables ----------

    public async Task<(IReadOnlyList<ResponsableZeile> Eintraege, bool Mehr)> ResponsablesAsync(Guid akteurId, string? suche,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Users.AsNoTracking().Where(k => k.Rolle == Rollen.Responsable && k.Status != KontoStatus.Archive);
        if (PlattformListen.Suchtext(suche) is { } t)
            q = q.Where(k => k.Vorname.Contains(t) || k.Nachname.Contains(t) || k.UserName!.Contains(t) || k.Email!.Contains(t));
        var (seite, mehr) = await PlattformListen.SeiteAsync(q.OrderBy(k => k.Vorname).ThenBy(k => k.Nachname)
            .Select(k => new { k.Id, Name = k.Vorname + " " + k.Nachname, k.UserName, k.Email, k.Status }), ueberspringen, anzahl, ct);
        var ids = seite.Select(x => x.Id).ToList();
        var schulen = (await db.Schulen.AsNoTracking().Where(s => s.ResponsableId != null && ids.Contains(s.ResponsableId.Value))
            .OrderBy(s => s.Name).Select(s => new { s.ResponsableId, s.Name }).ToListAsync(ct)).ToLookup(x => x.ResponsableId!.Value, x => x.Name);
        return (seite.Select(x => new ResponsableZeile(x.Id, x.Name, x.UserName!, x.Email, x.Status, schulen[x.Id].ToList())).ToList(), mehr);
    }

    /// <summary>Responsable anlegen (E-Mail Pflicht): Startpasswort einmal anzeigen, Willkommensmail ohne Passwort.</summary>
    public async Task<Zugang> ResponsableAnlegenAsync(Guid akteurId, ResponsableEingabe e, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        var vorname = Eingabe.Pflicht(e.Vorname, 100, "le prénom", "prenom");
        var nachname = Eingabe.Pflicht(e.Nachname, 100, "le nom", "nom");
        var email = Eingabe.Email(e.Email) ?? throw new FachFehler("Une adresse e-mail est nécessaire pour ce rôle (factures, messages de la plateforme).", "email");
        var telefon = Eingabe.Telefon(e.Telefon, "telephone");
        var (konto, passwort) = await dbFactory.TransaktionAsync(async db =>
        {
            var identifiant = await konten.Basis.IdentifiantAsync(db, e.Identifiant, vorname, nachname, ct: ct);
            var passwort = StartPasswort.Erzeuge();
            var konto = await konten.AnlegenAsync(db, new KontoDaten(vorname, nachname, identifiant, email, telefon), identifiant,
                Rollen.Responsable, null, KontoStatus.Actif, passwort, mussPasswortAendern: true, erstelltVonId: akteur.Id, selbstRegistriert: false, ct);
            audit.Erfasse(db, akteur.Id, null, AuditAktionen.KontoAngelegt, nameof(Konto), konto.Id, $"Responsable {konto.VollerName} créé.");
            await db.SaveChangesAsync(ct);
            return (konto, passwort);
        }, ct, DbArbeit.KontoVersuche);
        await mails.ResponsableWillkommenAsync(konto.Email, konto.Vorname, konto.UserName!, ct);
        return new Zugang(konto.Id, konto.VollerName, Rollen.Bezeichnung(Rollen.Responsable), konto.UserName!, passwort);
    }

    /// <summary>Alle Schulen zur Auswahl (Name, Ort, aktueller Responsable).</summary>
    public async Task<IReadOnlyList<SchulOption>> SchulOptionenAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Schulen.AsNoTracking().Where(s => s.Status != SchulStatus.Refusee).OrderBy(s => s.Name)
            .Select(s => new SchulOption(s.Id, s.Name, s.Ort, s.Status,
                db.Users.Where(r => r.Id == s.ResponsableId).Select(r => r.Vorname + " " + r.Nachname).FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> SchulenDesResponsableAsync(Guid akteurId, Guid responsableId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Schulen.AsNoTracking().Where(s => s.ResponsableId == responsableId).Select(s => s.Id).ToListAsync(ct);
    }

    /// <summary>Setzt genau diese Schulen für den Responsable (andere verlieren ihn); jede Änderung im Audit.</summary>
    public async Task SchulenZuweisenAsync(Guid akteurId, Guid responsableId, IReadOnlyCollection<Guid> schulIds, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var r = await db.Users.Where(k => k.Id == responsableId && k.Rolle == Rollen.Responsable)
                .Select(k => new { k.Status, Name = k.Vorname + " " + k.Nachname }).FirstOrDefaultAsync(ct)
                ?? throw new FachFehler("Responsable introuvable.");
            if (r.Status != KontoStatus.Actif && schulIds.Count > 0) throw new FachFehler("Ce responsable n'est pas actif.");
            var betroffen = await db.Schulen.Where(s => s.ResponsableId == responsableId || schulIds.Contains(s.Id)).ToListAsync(ct);
            foreach (var s in betroffen)
            {
                var soll = schulIds.Contains(s.Id);
                if (soll == (s.ResponsableId == responsableId)) continue;
                s.ResponsableId = soll ? responsableId : null;
                audit.Erfasse(db, akteur.Id, s.Id, PlattformAudit.ResponsableZugewiesen, nameof(Schule), s.Id,
                    soll ? $"Responsable de « {s.Name} » : {r.Name}." : $"{r.Name} n'accompagne plus « {s.Name} ».");
            }
            await db.SaveChangesAsync(ct);
        }, ct);
    }
}
