using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record FachZeile(Guid Id, string Name, string? Kuerzel, FachGruppe Gruppe, int Reihenfolge, bool Aktiv, int AnzahlKlassen);

public sealed record FachEingabe(string? Name, string? Kuerzel, FachGruppe Gruppe);

/// <summary>Fächer der Schule mit Bulletin-Gruppe und Reihenfolge (Bauplan §4.2).</summary>
public sealed class FaecherService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff)
{
    public static string GruppenText(FachGruppe g) => g switch
    {
        FachGruppe.Lettres => "Lettres",
        FachGruppe.Sciences => "Sciences",
        FachGruppe.Autres => "Autres",
        _ => "Sans groupe"
    };

    public async Task<IReadOnlyList<FachZeile>> FaecherAsync(Guid akteurId, bool mitInaktiven = true, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Faecher.AsNoTracking().Where(f => f.SchuleId == a.SchuleId && (mitInaktiven || f.Aktiv))
            .OrderBy(f => f.Reihenfolge).ThenBy(f => f.Name)
            .Select(f => new FachZeile(f.Id, f.Name, f.Kuerzel, f.Gruppe, f.Reihenfolge, f.Aktiv,
                db.Klassenfaecher.Count(kf => kf.FachId == f.Id)))
            .ToListAsync(ct);
    }

    public async Task<Guid> FachSpeichernAsync(Guid akteurId, Guid? fachId, FachEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var name = Eingabe.Pflicht(e.Name, 120, "le nom de la matière", "name");
        var kuerzel = Eingabe.Text(e.Kuerzel, 12, "l'abréviation", feld: "kuerzel")?.ToUpperInvariant();
        if (!Enum.IsDefined(e.Gruppe)) throw new FachFehler("Choisissez le groupe Lettres, Sciences, Autres ou « sans groupe ».", "gruppe");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alle = await db.Faecher.Where(f => f.SchuleId == a.SchuleId).ToListAsync(ct);
        if (alle.Any(f => f.Id != fachId && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new FachFehler($"La matière « {name} » existe déjà.", "name");
        Fach fach;
        if (fachId is { } id)
        {
            fach = alle.FirstOrDefault(f => f.Id == id) ?? throw new FachFehler("Cette matière est introuvable.");
        }
        else
        {
            fach = new Fach { SchuleId = a.SchuleId, Reihenfolge = alle.Count == 0 ? 1 : alle.Max(f => f.Reihenfolge) + 1 };
            db.Faecher.Add(fach);
        }
        fach.Name = name;
        fach.Kuerzel = kuerzel;
        fach.Gruppe = e.Gruppe;
        await db.SaveChangesAsync(ct);
        return fach.Id;
    }

    /// <summary>Löschen, solange in keiner Klasse und keinem Inhalt benutzt; sonst deaktivieren.</summary>
    public async Task<LoeschErgebnis> FachLoeschenAsync(Guid akteurId, Guid fachId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fach = await db.Faecher.FirstOrDefaultAsync(f => f.Id == fachId, ct) ?? throw new FachFehler("Cette matière est introuvable.");
        a.PruefeSchule(fach.SchuleId);
        if (await db.Klassenfaecher.AnyAsync(kf => kf.FachId == fach.Id, ct) || await db.Lecons.AnyAsync(l => l.FachId == fach.Id, ct)
            || await db.Pruefungen.AnyAsync(p => p.FachId == fach.Id, ct))
        {
            fach.Aktiv = false;
            await db.SaveChangesAsync(ct);
            return LoeschErgebnis.Deaktiviert;
        }
        db.Faecher.Remove(fach);
        await db.SaveChangesAsync(ct);
        return LoeschErgebnis.Geloescht;
    }

    public async Task FachAktivierenAsync(Guid akteurId, Guid fachId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fach = await db.Faecher.FirstOrDefaultAsync(f => f.Id == fachId, ct) ?? throw new FachFehler("Cette matière est introuvable.");
        a.PruefeSchule(fach.SchuleId);
        fach.Aktiv = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Fach in der Bulletin-Reihenfolge um eine Stelle verschieben (-1 = nach oben).</summary>
    public async Task VerschiebenAsync(Guid akteurId, Guid fachId, int richtung, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alle = await db.Faecher.Where(f => f.SchuleId == a.SchuleId).OrderBy(f => f.Reihenfolge).ThenBy(f => f.Name).ToListAsync(ct);
        var i = alle.FindIndex(f => f.Id == fachId);
        if (i < 0) throw new FachFehler("Cette matière est introuvable.");
        var j = i + Math.Sign(richtung);
        if (j < 0 || j >= alle.Count) return;
        (alle[i], alle[j]) = (alle[j], alle[i]);
        for (var n = 0; n < alle.Count; n++) alle[n].Reihenfolge = n + 1;
        await db.SaveChangesAsync(ct);
    }
}
