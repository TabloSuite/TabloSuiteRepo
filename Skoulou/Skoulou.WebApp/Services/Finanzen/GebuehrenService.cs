using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

public sealed record RateEingabe(long? Betrag, DateOnly? Faellig);
/// <summary>Eingabe eines Postens; <paramref name="PeriodeId"/> = nur für diese Periode/Session (null = ganzes Jahr).</summary>
public sealed record PostenEingabe(string? Bezeichnung, long? Betrag, Guid? StufeId, Guid? KlasseId, bool Pflicht, IReadOnlyList<RateEingabe> Raten,
    Guid? PeriodeId = null);
public sealed record RateZeile(int Nr, long Betrag, DateOnly Faellig);
public sealed record PostenZeile(Guid Id, string Bezeichnung, long Betrag, Guid? StufeId, Guid? KlasseId, string Geltung, bool Pflicht, int Reihenfolge, IReadOnlyList<RateZeile> Raten,
    Guid? PeriodeId = null);
public sealed record GeltungsAuswahl(IReadOnlyList<AuswahlEintrag> Stufen, IReadOnlyList<AuswahlEintrag> Klassen, IReadOnlyList<AuswahlEintrag> Perioden);
public sealed record ErmaessigungEingabe(Guid SchuelerId, Guid? PostenId, ErmaessigungsArt Art, decimal? Wert, string? Grund);
public sealed record ErmaessigungZeile(Guid Id, Guid? PostenId, string Posten, ErmaessigungsArt Art, decimal Wert, string Grund, string ErfasstVon, DateTime ErstelltAmUtc);
public sealed record ZahlungKurz(Guid Id, string Quittungsnummer, DateOnly Datum, long Betrag, ZahlungsKanal Kanal, string? Referenz, bool Storniert, string? StornoGrund);
public sealed record SchuelerKontoDetail(SchuelerKopf Kopf, Guid SchuljahrId, string Schuljahr, GebuehrenKonto Konto,
    IReadOnlyList<ErmaessigungZeile> Ermaessigungen, IReadOnlyList<ZahlungKurz> Zahlungen);
public sealed record KontoUebersichtZeile(Guid SchuelerId, string Name, string? Matrikel, string? Klasse, long Soll, long Bezahlt, long Offen, long EnRetard, long Guthaben);
public sealed record KontenZaehler(int Alle, int EnRetard, int Soldes, int Avoirs, long Soll, long Bezahlt, long Offen, long EnRetardBetrag);

/// <summary>
/// Gebühren eines Schuljahres: Posten mit Raten (Summe = Postenbetrag), Kopie aus dem Vorjahr, Ermäßigungen je Schüler,
/// Konto je Schüler und Klassenübersicht (« en retard »). Schreiben: Direktion mit <see cref="Faehigkeit.GebuehrenVerwalten"/>.
/// </summary>
public sealed class GebuehrenService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, TimeProvider zeit)
{
    public const string FilterEnRetard = "retard";
    public const string FilterSoldes = "soldes";
    public const string FilterAvoirs = "avoirs";
    public const string FilterReste = "reste";
    public const int MaxRaten = 12;

    private static readonly Faehigkeit[] Lesen = [Faehigkeit.GebuehrenVerwalten, Faehigkeit.ZahlungenErfassen, Faehigkeit.FinanzenEinsehen];

    private DateOnly Heute => new SchulZeit(zeit).Heute;

    public async Task<IReadOnlyList<PostenZeile>> PostenAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var posten = await db.GebuehrenPosten.AsNoTracking().Where(p => p.SchuljahrId == jahrId).Include(p => p.Raten)
            .OrderBy(p => p.Reihenfolge).ThenBy(p => p.Bezeichnung).ToListAsync(ct);
        var stufen = await db.Stufen.Where(s => s.SchuleId == a.SchuleId).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var klassen = await db.Klassen.Where(k => k.SchuljahrId == jahrId).ToDictionaryAsync(k => k.Id, k => k.Name, ct);
        var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahrId).ToDictionaryAsync(p => p.Id, p => p.Titel, ct);
        return posten.Select(p => new PostenZeile(p.Id, p.Bezeichnung, p.Betrag, p.StufeId, p.KlasseId,
            Geltung(p, stufen, klassen, perioden), p.Pflicht, p.Reihenfolge,
            p.Raten.OrderBy(r => r.Nr).Select(r => new RateZeile(r.Nr, r.Betrag, r.Faellig)).ToList(), p.PeriodeId)).ToList();
    }

    public static string Geltung(GebuehrenPosten p, IReadOnlyDictionary<Guid, string> stufen, IReadOnlyDictionary<Guid, string> klassen,
        IReadOnlyDictionary<Guid, string>? perioden = null)
    {
        var wer = p.KlasseId is { } k ? $"Classe {klassen.GetValueOrDefault(k, "?")}"
            : p.StufeId is { } s ? $"Niveau {stufen.GetValueOrDefault(s, "?")}" : "Tous les élèves";
        return p.PeriodeId is { } per ? $"{wer} · {perioden?.GetValueOrDefault(per) ?? "?"} seulement" : wer;
    }

    public async Task<GeltungsAuswahl> GeltungsAuswahlAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var stufen = await db.Stufen.Where(s => s.SchuleId == a.SchuleId && s.Aktiv).OrderBy(s => s.Reihenfolge)
            .Select(s => new AuswahlEintrag(s.Id, s.Name, null)).ToListAsync(ct);
        var klassen = await db.Klassen.Where(k => k.SchuljahrId == jahrId && k.Aktiv).OrderBy(k => k.Name)
            .Select(k => new AuswahlEintrag(k.Id, k.Name, null)).ToListAsync(ct);
        var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahrId).OrderBy(p => p.Beginn).ThenBy(p => p.Nr)
            .Select(p => new { p.Id, p.Titel, p.Beginn, p.Ende }).ToListAsync(ct);
        return new GeltungsAuswahl(stufen, klassen, perioden
            .Select(p => new AuswahlEintrag(p.Id, p.Titel, $"{SkoulouKultur.Datum(p.Beginn)} – {SkoulouKultur.Datum(p.Ende)}")).ToList());
    }

    public async Task<Guid> PostenSpeichernAsync(Guid akteurId, Guid jahrId, Guid? postenId, PostenEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.GebuehrenVerwalten);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            var bezeichnung = Eingabe.Pflicht(e.Bezeichnung, 120, "le libellé du frais", "bezeichnung");
            var betrag = FinanzEingabe.Betrag(e.Betrag, "betrag", "le montant du frais");
            Guid? stufeId = null, klasseId = null;
            if (e.KlasseId is { } k)
            {
                if (!await db.Klassen.AnyAsync(x => x.Id == k && x.SchuljahrId == jahrId, ct)) throw new FachFehler("Cette classe n'appartient pas à l'année choisie.", "geltung");
                klasseId = k;
            }
            else if (e.StufeId is { } s)
            {
                if (!await db.Stufen.AnyAsync(x => x.Id == s && x.SchuleId == a.SchuleId, ct)) throw new FachFehler("Ce niveau est introuvable.", "geltung");
                stufeId = s;
            }
            Guid? periodeId = null;
            if (e.PeriodeId is { } pid)
            {
                var periode = await db.Perioden.FirstOrDefaultAsync(x => x.Id == pid && x.SchuljahrId == jahrId, ct)
                              ?? throw new FachFehler("Cette période n'appartient pas à l'année choisie.", "periode");
                if (klasseId is { } kl && await db.Klassen.Where(x => x.Id == kl).Select(x => x.PeriodeId).FirstOrDefaultAsync(ct) is { } klassenSession
                    && klassenSession != pid)
                    throw new FachFehler($"Cette classe n'a pas lieu pendant « {periode.Titel} ».", "periode");
                periodeId = pid;
            }
            var raten = Raten(e.Raten, betrag, jahr);

            GebuehrenPosten posten;
            if (postenId is { } id)
            {
                posten = await db.GebuehrenPosten.Include(p => p.Raten).FirstOrDefaultAsync(p => p.Id == id && p.SchuljahrId == jahrId, ct)
                         ?? throw new FachFehler("Ce frais est introuvable.");
                db.GebuehrenRaten.RemoveRange(posten.Raten);
                await db.SaveChangesAsync(ct);
                posten.Raten.Clear();
            }
            else
            {
                var letzte = await db.GebuehrenPosten.Where(p => p.SchuljahrId == jahrId).MaxAsync(p => (int?)p.Reihenfolge, ct) ?? 0;
                posten = new GebuehrenPosten { SchuleId = a.SchuleId, SchuljahrId = jahrId, Reihenfolge = letzte + 1 };
                db.GebuehrenPosten.Add(posten);
            }
            (posten.Bezeichnung, posten.Betrag, posten.StufeId, posten.KlasseId, posten.PeriodeId, posten.Pflicht) = (bezeichnung, betrag, stufeId, klasseId, periodeId, e.Pflicht);
            foreach (var r in raten) db.GebuehrenRaten.Add(new GebuehrenRate { PostenId = posten.Id, Nr = r.Nr, Betrag = r.Betrag, Faellig = r.Faellig });
            await db.SaveChangesAsync(ct);
            return posten.Id;
        }, ct);
    }

    /// <summary>Raten prüfen: mindestens eine, Summe = Betrag, Fälligkeit im Rahmen des Jahres; Nummern nach Fälligkeit.</summary>
    public static IReadOnlyList<RateZeile> Raten(IReadOnlyList<RateEingabe> eingaben, long betrag, Schuljahr jahr)
    {
        var liste = eingaben.Where(r => r.Betrag is not null || r.Faellig is not null).ToList();
        if (liste.Count == 0) return [new RateZeile(1, betrag, jahr.Beginn)];
        if (liste.Count > MaxRaten) throw new FachFehler($"Un frais compte au plus {MaxRaten} tranches.", "raten");
        foreach (var r in liste)
        {
            if (r.Betrag is not > 0) throw new FachFehler("Chaque tranche doit avoir un montant supérieur à 0.", "raten");
            if (r.Faellig is not { } f) throw new FachFehler("Chaque tranche doit avoir une date d'échéance.", "raten");
            if (f < jahr.Beginn.AddMonths(-6) || f > jahr.Ende)
                throw new FachFehler($"Les échéances doivent se situer entre le {SkoulouKultur.Datum(jahr.Beginn.AddMonths(-6))} et la fin de l'année ({SkoulouKultur.Datum(jahr.Ende)}).", "raten");
        }
        var summe = liste.Sum(r => r.Betrag!.Value);
        if (summe != betrag)
            throw new FachFehler($"La somme des tranches ({SkoulouKultur.Fcfa(summe)}) doit être égale au montant du frais ({SkoulouKultur.Fcfa(betrag)}).", "raten");
        return liste.OrderBy(r => r.Faellig).Select((r, i) => new RateZeile(i + 1, r.Betrag!.Value, r.Faellig!.Value)).ToList();
    }

    public async Task PostenLoeschenAsync(Guid akteurId, Guid postenId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.GebuehrenVerwalten);
        await dbFactory.TransaktionAsync(async db =>
        {
            var posten = await db.GebuehrenPosten.Include(p => p.Raten).FirstOrDefaultAsync(p => p.Id == postenId, ct)
                         ?? throw new FachFehler("Ce frais est introuvable.");
            a.PruefeSchule(posten.SchuleId);
            OrgAbfragen.PruefeOffen(await OrgAbfragen.SchuljahrAsync(db, a, posten.SchuljahrId, ct));
            if (await db.Ermaessigungen.AnyAsync(e => e.PostenId == postenId, ct))
                throw new FachFehler("Des réductions portent sur ce frais : retirez-les d'abord.");
            db.GebuehrenRaten.RemoveRange(posten.Raten);
            db.GebuehrenPosten.Remove(posten);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Übernimmt die Posten des Vorjahres (Fälligkeiten um die Jahresdifferenz verschoben, Klassen über den Namen).</summary>
    public async Task<(int Kopiert, int Uebersprungen)> VorjahrKopierenAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.GebuehrenVerwalten);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            var vorjahr = await db.Schuljahre.Where(j => j.SchuleId == a.SchuleId && j.Beginn < jahr.Beginn).OrderByDescending(j => j.Beginn).FirstOrDefaultAsync(ct)
                          ?? throw new FachFehler("Aucune année précédente dont copier les frais.");
            var alte = await db.GebuehrenPosten.AsNoTracking().Where(p => p.SchuljahrId == vorjahr.Id).Include(p => p.Raten).OrderBy(p => p.Reihenfolge).ToListAsync(ct);
            if (alte.Count == 0) throw new FachFehler($"L'année {vorjahr.Bezeichnung} n'a aucun frais à copier.");
            var vorhandene = await db.GebuehrenPosten.Where(p => p.SchuljahrId == jahrId).Select(p => new { p.Bezeichnung, p.StufeId, p.KlasseId, p.PeriodeId, p.Reihenfolge }).ToListAsync(ct);
            var alteKlassen = await db.Klassen.Where(k => k.SchuljahrId == vorjahr.Id).ToDictionaryAsync(k => k.Id, k => k.Name, ct);
            var neueKlassen = await db.Klassen.Where(k => k.SchuljahrId == jahrId).ToListAsync(ct);
            // Posten einer Session: Periode mit derselben Nr im neuen Jahr (fehlt sie, wird der Posten übersprungen).
            var altePerioden = await db.Perioden.Where(p => p.SchuljahrId == vorjahr.Id).ToDictionaryAsync(p => p.Id, p => p.Nr, ct);
            var neuePerioden = await db.Perioden.Where(p => p.SchuljahrId == jahrId).ToDictionaryAsync(p => p.Nr, p => p.Id, ct);
            var verschiebung = jahr.Beginn.Year - vorjahr.Beginn.Year;
            var reihenfolge = vorhandene.Count == 0 ? 0 : vorhandene.Max(p => p.Reihenfolge);
            int kopiert = 0, uebersprungen = 0;
            foreach (var p in alte)
            {
                Guid? klasse = null;
                if (p.KlasseId is { } altK)
                {
                    klasse = neueKlassen.FirstOrDefault(k => k.Name == alteKlassen.GetValueOrDefault(altK))?.Id;
                    if (klasse is null) { uebersprungen++; continue; }
                }
                Guid? periode = null;
                if (p.PeriodeId is { } altP)
                {
                    if (!altePerioden.TryGetValue(altP, out var nr) || !neuePerioden.TryGetValue(nr, out var neuP)) { uebersprungen++; continue; }
                    periode = neuP;
                }
                // « Existiert schon » nur bei gleicher Session: gleichnamige Posten anderer Sessions (« Frais de session ») werden kopiert.
                if (vorhandene.Any(v => v.Bezeichnung == p.Bezeichnung && v.StufeId == p.StufeId && v.KlasseId == klasse && v.PeriodeId == periode)) { uebersprungen++; continue; }
                var neu = new GebuehrenPosten
                {
                    SchuleId = a.SchuleId, SchuljahrId = jahrId, Bezeichnung = p.Bezeichnung, Betrag = p.Betrag,
                    StufeId = klasse is null ? p.StufeId : null, KlasseId = klasse, PeriodeId = periode, Pflicht = p.Pflicht, Reihenfolge = ++reihenfolge
                };
                db.GebuehrenPosten.Add(neu);
                foreach (var r in p.Raten.OrderBy(r => r.Nr))
                {
                    var faellig = r.Faellig.AddYears(verschiebung);
                    if (faellig > jahr.Ende) faellig = jahr.Ende;
                    db.GebuehrenRaten.Add(new GebuehrenRate { PostenId = neu.Id, Nr = r.Nr, Betrag = r.Betrag, Faellig = faellig });
                }
                kopiert++;
            }
            await db.SaveChangesAsync(ct);
            return (kopiert, uebersprungen);
        }, ct);
    }

    public async Task<Guid> ErmaessigungAnlegenAsync(Guid akteurId, Guid jahrId, ErmaessigungEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.GebuehrenVerwalten);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            await OrgAbfragen.KontoAsync(db, a, e.SchuelerId, ct, Rollen.Schueler);
            if (e.PostenId is { } p && !await db.GebuehrenPosten.AnyAsync(x => x.Id == p && x.SchuljahrId == jahrId, ct))
                throw new FachFehler("Ce frais est introuvable.", "posten");
            var wert = e.Art == ErmaessigungsArt.Pourcentage
                ? e.Wert is > 0 and <= 100 ? Math.Round(e.Wert.Value, 2) : throw new FachFehler("Le pourcentage doit être compris entre 0 et 100.", "wert")
                : FinanzEingabe.Betrag(e.Wert is { } w && w == Math.Floor(w) ? (long)w : null, "wert", "le montant de la réduction (entier)");
            var ermaessigung = new Ermaessigung
            {
                SchuleId = a.SchuleId, SchuelerId = e.SchuelerId, SchuljahrId = jahrId, PostenId = e.PostenId, Art = e.Art, Wert = wert,
                Grund = Eingabe.Pflicht(e.Grund, 200, "le motif de la réduction", "grund"), ErfasstVonId = a.Id
            };
            db.Ermaessigungen.Add(ermaessigung);
            await db.SaveChangesAsync(ct);
            return ermaessigung.Id;
        }, ct);
    }

    public async Task ErmaessigungLoeschenAsync(Guid akteurId, Guid ermaessigungId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.GebuehrenVerwalten);
        await dbFactory.TransaktionAsync(async db =>
        {
            var e = await db.Ermaessigungen.FirstOrDefaultAsync(x => x.Id == ermaessigungId, ct) ?? throw new FachFehler("Cette réduction est introuvable.");
            a.PruefeSchule(e.SchuleId);
            OrgAbfragen.PruefeOffen(await OrgAbfragen.SchuljahrAsync(db, a, e.SchuljahrId, ct));
            var text = $"Réduction retirée ({(e.Art == ErmaessigungsArt.Pourcentage ? $"{e.Wert:0.##} %" : SkoulouKultur.Fcfa((long)e.Wert))}, {e.Grund}).";
            db.Ermaessigungen.Remove(e);
            // Doppeltipp/zweites Gerät: schon entfernt → kein Fehler und kein zweiter Journaleintrag.
            if (!await db.LoeschenSpeichernAsync(ct)) return;
            audit.Erfasse(db, a.Id, a.SchuleId, FinanzAudit.ErmaessigungGeloescht, nameof(Ermaessigung), e.Id, text);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<SchuelerKontoDetail> KontoAsync(Guid akteurId, Guid jahrId, Guid schuelerId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        await OrgAbfragen.KontoAsync(db, a, schuelerId, ct, Rollen.Schueler);
        return await KontoDetailAsync(db, a.SchuleId, jahr, schuelerId, Heute, ct);
    }

    /// <summary>Konto mit Ermäßigungen und Zahlungen (ohne Rechteprüfung; auch für Schüler- und Elternsicht).</summary>
    internal static async Task<SchuelerKontoDetail> KontoDetailAsync(SkoulouDbContext db, Guid schuleId, Schuljahr jahr, Guid schuelerId, DateOnly heute, CancellationToken ct)
    {
        var konto = await KontoLader.KontoAsync(db, schuleId, jahr.Id, schuelerId, heute, ct);
        var kopf = (await KontoLader.KoepfeAsync(db, jahr.Id, [schuelerId], ct))[schuelerId];
        var ermaessigungen = await (from e in db.Ermaessigungen
                                    where e.SchuljahrId == jahr.Id && e.SchuelerId == schuelerId
                                    join k in db.Users on e.ErfasstVonId equals k.Id
                                    join p in db.GebuehrenPosten on e.PostenId equals p.Id into pj
                                    from p in pj.DefaultIfEmpty()
                                    orderby e.ErstelltAmUtc
                                    select new ErmaessigungZeile(e.Id, e.PostenId, p == null ? "Tous les frais" : p.Bezeichnung, e.Art, e.Wert, e.Grund,
                                        k.Vorname + " " + k.Nachname, e.ErstelltAmUtc)).ToListAsync(ct);
        var zahlungen = await db.Zahlungen.Where(z => z.SchuljahrId == jahr.Id && z.SchuelerId == schuelerId)
            .OrderByDescending(z => z.Datum).ThenByDescending(z => z.ErstelltAmUtc)
            .Select(z => new ZahlungKurz(z.Id, z.Quittungsnummer, z.Datum, z.Betrag, z.Kanal, z.Referenz, z.StorniertAmUtc != null, z.StornoGrund))
            .ToListAsync(ct);
        return new SchuelerKontoDetail(kopf, jahr.Id, jahr.Bezeichnung, konto, ermaessigungen, zahlungen);
    }

    public async Task<Seite<KontoUebersichtZeile>> KontenAsync(Guid akteurId, Guid jahrId, Guid? klasseId, string? filter, string? suche,
        int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        var zeilen = await AlleKontenAsync(akteurId, jahrId, klasseId, ct);
        IEnumerable<KontoUebersichtZeile> auswahl = filter switch
        {
            FilterEnRetard => zeilen.Where(z => z.EnRetard > 0).OrderByDescending(z => z.EnRetard),
            FilterSoldes => zeilen.Where(z => z.Soll > 0 && z.Offen == 0),
            FilterAvoirs => zeilen.Where(z => z.Guthaben > 0),
            FilterReste => zeilen.Where(z => z.Offen > 0).OrderByDescending(z => z.Offen),
            _ => zeilen
        };
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = TextNormalisierung.Schluessel(suche);
            auswahl = auswahl.Where(z => TextNormalisierung.Schluessel(z.Name).Contains(s, StringComparison.Ordinal)
                                         || TextNormalisierung.Schluessel(z.Matrikel).Contains(s, StringComparison.Ordinal));
        }
        anzahl = Eingabe.Anzahl(anzahl);
        var seite = auswahl.Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1).ToList();
        var mehr = seite.Count > anzahl;
        if (mehr) seite.RemoveAt(anzahl);
        return new Seite<KontoUebersichtZeile>(seite, mehr);
    }

    public async Task<KontenZaehler> ZaehlerAsync(Guid akteurId, Guid jahrId, Guid? klasseId, CancellationToken ct = default)
    {
        var z = await AlleKontenAsync(akteurId, jahrId, klasseId, ct);
        return new KontenZaehler(z.Count, z.Count(x => x.EnRetard > 0), z.Count(x => x.Soll > 0 && x.Offen == 0), z.Count(x => x.Guthaben > 0),
            z.Sum(x => x.Soll), z.Sum(x => x.Bezahlt), z.Sum(x => x.Offen), z.Sum(x => x.EnRetard));
    }

    private async Task<List<KontoUebersichtZeile>> AlleKontenAsync(Guid akteurId, Guid jahrId, Guid? klasseId, CancellationToken ct)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        if (klasseId is { } k) await OrgAbfragen.KlasseAsync(db, a, k, ct);
        var ids = await KontoLader.SchuelerDesJahresAsync(db, jahrId, klasseId, ct);
        var konten = await KontoLader.KontenAsync(db, a.SchuleId, jahrId, ids, Heute, ct);
        var koepfe = await KontoLader.KoepfeAsync(db, jahrId, ids, ct);
        return koepfe.Values
            .Where(kopf => klasseId is null || kopf.KlasseId == klasseId)
            .Select(kopf =>
            {
                var konto = konten.GetValueOrDefault(kopf.Id) ?? GebuehrenKonto.Leer(Heute);
                return new KontoUebersichtZeile(kopf.Id, kopf.Name, kopf.Matrikel, kopf.Klasse, konto.Soll, konto.Bezahlt, konto.Offen, konto.EnRetardBetrag, konto.Guthaben);
            })
            .OrderBy(z => z.Name, StringComparer.Create(SkoulouKultur.Kultur, true)).ToList();
    }
}
