using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Leçons und Examens: Listen, Editor (Laden/Speichern), Status, Duplizieren, Löschen, Übertragen.
/// Lehrkräfte arbeiten nur mit eigenen Inhalten; die Direktion sieht alle Inhalte der Schule
/// (transparent, ohne « partage ») und erfasst für Lehrkräfte (« Saisi par la direction »).
/// </summary>
public sealed class InhaltService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AufgabenAbgleich abgleich,
    InhaltBereiniger bereiniger, AuditService audit)
{
    public const int MaxSeiten = 30;
    public const int MaxSeitenLaenge = 400_000;
    public const string AuditUebertragen = "inhalt.uebertragen";

    // ---------- Auswahllisten ----------

    /// <summary>Stufen/Fächer der Lehrkraft, Lehrkräfte (Direktion), Fächer und Klassen des Jahres (Filter).</summary>
    public async Task<InhaltOptionen> OptionenAsync(Guid akteurId, Guid? autorId = null, Guid? schuljahrId = null, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        var schuleId = akteur.SchuleId!.Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var autor = akteur.IstDirektion ? autorId ?? akteur.Id : akteur.Id;

        var lehrkraefte = akteur.IstDirektion
            ? await db.Users.AsNoTracking()
                .Where(k => k.SchuleId == schuleId && k.Status == KontoStatus.Actif && Rollen.Lehrkraefte.Contains(k.Rolle))
                .OrderBy(k => k.Nachname).ThenBy(k => k.Vorname)
                .Select(k => new LehrkraftOption(k.Id, k.Vorname + " " + k.Nachname, k.Rolle)).ToListAsync(ct)
            : [new LehrkraftOption(akteur.Id, akteur.Name, akteur.Rolle)];

        var faecher = await db.Faecher.AsNoTracking().Where(f => f.SchuleId == schuleId).OrderBy(f => f.Reihenfolge).ThenBy(f => f.Name)
            .Select(f => new AuswahlEintrag(f.Id, f.Name)).ToListAsync(ct);

        var jahr = schuljahrId ?? (await SchulAbfragen.LaufendesSchuljahrAsync(db, schuleId, ct))?.Id;
        var klassenQuelle = db.Klassen.AsNoTracking().Where(k => k.SchuleId == schuleId && k.SchuljahrId == jahr);
        if (!akteur.IstDirektion)
        {
            var eigene = SchulAbfragen.KlassenDesLehrers(db, akteur.Id, jahr);
            klassenQuelle = klassenQuelle.Where(k => eigene.Contains(k.Id));
        }
        var klassen = await klassenQuelle.OrderBy(k => k.Name).Select(k => new AuswahlEintrag(k.Id, k.Name)).ToListAsync(ct);

        return new InhaltOptionen(await KombinationenAsync(db, autor, ct), lehrkraefte, faecher, klassen);
    }

    /// <summary>Stufe + Fach aus den Lehraufträgen der Lehrkraft (alle Schuljahre).</summary>
    private static async Task<List<StufeFach>> KombinationenAsync(SkoulouDbContext db, Guid lehrkraftId, CancellationToken ct)
    {
        var klassenfaecher = SchulAbfragen.KlassenfaecherDesLehrers(db, lehrkraftId);
        var roh = await (from kf in db.Klassenfaecher
                         where klassenfaecher.Contains(kf.Id)
                         join k in db.Klassen on kf.KlasseId equals k.Id
                         join s in db.Stufen on k.StufeId equals s.Id
                         join f in db.Faecher on kf.FachId equals f.Id
                         select new { StufeId = s.Id, Stufe = s.Name, SR = s.Reihenfolge, FachId = f.Id, Fach = f.Name, FR = f.Reihenfolge })
            .Distinct().ToListAsync(ct);
        return roh.OrderBy(x => x.SR).ThenBy(x => x.Stufe).ThenBy(x => x.FR).ThenBy(x => x.Fach)
            .Select(x => new StufeFach(x.StufeId, x.Stufe, x.FachId, x.Fach)).ToList();
    }

    // ---------- Listen ----------

    public async Task<(IReadOnlyList<InhaltZeile> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, InhaltFilter filter,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        var schuleId = akteur.SchuleId!.Value;
        var autorId = akteur.IstDirektion ? filter.AutorId : akteur.Id;
        var suche = filter.Suche?.Trim();
        anzahl = Math.Clamp(anzahl, 1, 200);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        IQueryable<InhaltZeile> abfrage;
        if (filter.Art == InhaltsArt.Lecon)
        {
            var q = db.Lecons.AsNoTracking().Where(l => l.SchuleId == schuleId);
            if (autorId is { } a) q = q.Where(l => l.AutorId == a);
            q = filter.Status is { } s ? q.Where(l => l.Status == s) : q.Where(l => l.Status != InhaltsStatus.Archive);
            if (filter.FachId is { } fach) q = q.Where(l => l.FachId == fach);
            if (filter.StufeId is { } stufe) q = q.Where(l => l.StufeId == stufe);
            if (filter.KlasseId is { } klasse) q = q.Where(l => db.Zuweisungen.Any(z => z.LeconId == l.Id && z.KlasseId == klasse));
            if (!string.IsNullOrEmpty(suche)) q = q.Where(l => l.Titel.Contains(suche) || (l.Thema != null && l.Thema.Contains(suche)));
            abfrage = q.OrderByDescending(l => l.GeaendertAmUtc).Select(l => new InhaltZeile(l.Id, InhaltsArt.Lecon, l.Titel,
                db.Stufen.Where(x => x.Id == l.StufeId).Select(x => x.Name).FirstOrDefault() ?? "",
                db.Faecher.Where(x => x.Id == l.FachId).Select(x => x.Name).FirstOrDefault() ?? "",
                l.Status, l.AutorId,
                db.Users.Where(k => k.Id == l.AutorId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault() ?? "",
                l.ErfasstVonId != null,
                db.Fragen.Count(f => !f.Archiviert && db.AufgabenBloecke.Any(b => b.Id == f.BlockId && b.LeconId == l.Id && !b.Archiviert)),
                db.Zuweisungen.Count(z => z.LeconId == l.Id && z.Status == ZuweisungsStatus.Active),
                l.GeaendertAmUtc));
        }
        else
        {
            var q = db.Pruefungen.AsNoTracking().Where(p => p.SchuleId == schuleId);
            if (autorId is { } a) q = q.Where(p => p.AutorId == a);
            q = filter.Status is { } s ? q.Where(p => p.Status == s) : q.Where(p => p.Status != InhaltsStatus.Archive);
            if (filter.FachId is { } fach) q = q.Where(p => p.FachId == fach);
            if (filter.StufeId is { } stufe) q = q.Where(p => p.StufeId == stufe);
            if (filter.KlasseId is { } klasse) q = q.Where(p => db.Zuweisungen.Any(z => z.PruefungId == p.Id && z.KlasseId == klasse));
            if (!string.IsNullOrEmpty(suche)) q = q.Where(p => p.Titel.Contains(suche));
            abfrage = q.OrderByDescending(p => p.GeaendertAmUtc).Select(p => new InhaltZeile(p.Id, InhaltsArt.Pruefung, p.Titel,
                db.Stufen.Where(x => x.Id == p.StufeId).Select(x => x.Name).FirstOrDefault() ?? "",
                db.Faecher.Where(x => x.Id == p.FachId).Select(x => x.Name).FirstOrDefault() ?? "",
                p.Status, p.AutorId,
                db.Users.Where(k => k.Id == p.AutorId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault() ?? "",
                p.ErfasstVonId != null,
                db.Fragen.Count(f => !f.Archiviert && db.AufgabenBloecke.Any(b => b.Id == f.BlockId && b.PruefungId == p.Id && !b.Archiviert)),
                db.Zuweisungen.Count(z => z.PruefungId == p.Id && z.Status == ZuweisungsStatus.Active),
                p.GeaendertAmUtc));
        }

        var seite = await abfrage.Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1).ToListAsync(ct);
        return (seite.Take(anzahl).ToList(), seite.Count > anzahl);
    }

    // ---------- Editor ----------

    /// <summary>Leeres Formular; die Direktion kann eine andere Lehrkraft als Autor wählen.</summary>
    public async Task<InhaltFormular> NeuAsync(Guid akteurId, InhaltsArt art, Guid? fuerLehrkraftId = null, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        var autorId = akteur.IstDirektion && fuerLehrkraftId is { } l ? l : akteur.Id;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (autorId != akteur.Id) await InhaltsRechte.PruefeLehrkraftAsync(db, akteur.SchuleId!.Value, autorId, ct);
        var kombis = await KombinationenAsync(db, autorId, ct);
        var formular = new InhaltFormular
        {
            Art = art, AutorId = autorId, StufeId = kombis.FirstOrDefault()?.StufeId, FachId = kombis.FirstOrDefault()?.FachId,
            AutorName = await db.Users.Where(k => k.Id == autorId).Select(k => k.Vorname + " " + k.Nachname).FirstAsync(ct),
            ErfasstVonName = autorId != akteur.Id ? akteur.Name : null
        };
        if (art == InhaltsArt.Lecon) formular.Seiten.Add(new SeiteFormular());
        else formular.Bloecke.Add(new BlockFormular { Titel = "Exercice 1", Fragen = [FrageFormular.Neu()] });
        return formular;
    }

    public async Task<InhaltFormular> LadeAsync(Guid akteurId, InhaltsArt art, Guid id, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var formular = new InhaltFormular { Id = id, Art = art };
        Guid schuleId;
        Guid? erfasstVon;
        if (art == InhaltsArt.Lecon)
        {
            var l = await db.Lecons.AsNoTracking().Include(x => x.Seiten).FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
            InhaltsRechte.PruefeInhalt(akteur, l.SchuleId, l.AutorId);
            (schuleId, erfasstVon) = (l.SchuleId, l.ErfasstVonId);
            formular.AutorId = l.AutorId;
            formular.Titel = l.Titel;
            formular.StufeId = l.StufeId;
            formular.FachId = l.FachId;
            formular.Status = l.Status;
            formular.Zusammenfassung = l.Zusammenfassung;
            formular.Thema = l.Thema;
            formular.VideoMediumId = l.VideoMediumId;
            formular.VideoName = l.VideoMediumId is { } v ? await db.Medien.Where(m => m.Id == v).Select(m => m.Dateiname).FirstOrDefaultAsync(ct) : null;
            formular.Seiten = l.Seiten.OrderBy(s => s.Nr).Select(s => new SeiteFormular { Html = s.Html }).ToList();
            if (formular.Seiten.Count == 0) formular.Seiten.Add(new SeiteFormular());
            formular.AnzahlZuweisungen = await db.Zuweisungen.CountAsync(z => z.LeconId == id, ct);
        }
        else
        {
            var p = await db.Pruefungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
            InhaltsRechte.PruefeInhalt(akteur, p.SchuleId, p.AutorId);
            (schuleId, erfasstVon) = (p.SchuleId, p.ErfasstVonId);
            formular.AutorId = p.AutorId;
            formular.Titel = p.Titel;
            formular.StufeId = p.StufeId;
            formular.FachId = p.FachId;
            formular.Status = p.Status;
            formular.Beschreibung = p.Beschreibung;
            formular.DauerMinuten = p.DauerMinuten;
            formular.Bestehensgrenze = p.Bestehensgrenze;
            formular.AnzahlZuweisungen = await db.Zuweisungen.CountAsync(z => z.PruefungId == id, ct);
        }
        formular.AutorName = await db.Users.Where(k => k.Id == formular.AutorId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct);
        formular.ErfasstVonName = erfasstVon is { } e ? await db.Users.Where(k => k.Id == e).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct) : null;

        var aktuelle = await FragenAbbildung.AktuelleAsync(db, art == InhaltsArt.Lecon ? id : null, art == InhaltsArt.Pruefung ? id : null, ct);
        var fragenIds = aktuelle.SelectMany(a => a.Fragen).Select(f => f.Id).ToList();
        var beantwortet = (await db.Antworten.Where(a => fragenIds.Contains(a.FrageId)).Select(a => a.FrageId).Distinct().ToListAsync(ct)).ToHashSet();
        formular.Bloecke = aktuelle.Select(a => new BlockFormular
        {
            Id = a.Block.Id, Titel = a.Block.Titel, Consigne = a.Block.Consigne,
            Fragen = a.Fragen.Select(f => new FrageFormular
            {
                Id = f.Id, Typ = f.Typ, Punkte = f.Punkte, AudioText = f.AudioText, Sprache = f.Sprache,
                TextHtml = f.Typ == FragenTyp.TexteATrous ? AufgabenAbgleich.LueckenTextZumBearbeiten(f.TextHtml) : f.TextHtml,
                Varianten = f.AkzeptierteAntworten.ToList(), Wortbank = f.Wortbank.ToList(), AkzenteBeachten = f.AkzenteBeachten,
                Luecken = FragenAbbildung.Luecken(f.LueckenJson).Select(l => l.ToList()).ToList(),
                Optionen = f.Optionen.OrderBy(o => o.Nr).Select(o => new OptionFormular { Text = o.Text, Richtig = o.Richtig }).ToList(),
                Revision = f.Revision, HatAntworten = beantwortet.Contains(f.Id)
            }).ToList()
        }).ToList();
        _ = schuleId;
        return formular;
    }

    /// <summary>Speichert Kopf, Seiten, Blöcke und Fragen in einer Transaktion; liefert die Id.</summary>
    public async Task<Guid> SpeichernAsync(Guid akteurId, InhaltFormular f, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        var schuleId = akteur.SchuleId!.Value;
        PruefeKopf(f);
        abgleich.Pruefe(f.Bloecke);
        var seiten = f.Art == InhaltsArt.Lecon ? BereinigteSeiten(f) : [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await DbTransaktion.AusfuehrenAsync(db, async () =>
        {
            Guid? leconId = null, pruefungId = null;
            if (f.Art == InhaltsArt.Lecon)
            {
                var lecon = f.Id is { } id
                    ? await db.Lecons.Include(l => l.Seiten).FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden)
                    : null;
                if (lecon is null)
                {
                    var autor = akteur.IstDirektion ? f.AutorId : akteur.Id;
                    await PruefeNeuenAutorAsync(db, akteur, autor, f, ct);
                    lecon = new Lecon { SchuleId = schuleId, AutorId = autor, ErfasstVonId = autor != akteur.Id ? akteur.Id : null };
                    db.Lecons.Add(lecon);
                }
                else
                {
                    InhaltsRechte.PruefeInhalt(akteur, lecon.SchuleId, lecon.AutorId);
                    await PruefeStufeFachAenderungAsync(db, lecon.AutorId, lecon.StufeId, lecon.FachId, f, z => z.LeconId == lecon.Id, ct);
                    db.LeconSeiten.RemoveRange(lecon.Seiten);
                    lecon.Seiten = [];
                    await db.SaveChangesAsync(ct);
                }
                lecon.Titel = f.Titel.Trim();
                lecon.StufeId = f.StufeId!.Value;
                lecon.FachId = f.FachId!.Value;
                lecon.Zusammenfassung = Leer(f.Zusammenfassung);
                lecon.Thema = Leer(f.Thema);
                lecon.VideoMediumId = await PruefeVideoAsync(db, schuleId, f.VideoMediumId, ct);
                lecon.Status = f.Status == InhaltsStatus.Archive ? InhaltsStatus.Archive : f.Status;
                // Neue Seiten ausdrücklich hinzufügen (vorgegebene IDs gälten über die Navigation sonst als bestehend).
                lecon.Seiten = seiten.Select((html, i) => new LeconSeite { LeconId = lecon.Id, Nr = i + 1, Html = html }).ToList();
                db.LeconSeiten.AddRange(lecon.Seiten);
                if (lecon.Status == InhaltsStatus.Prete && seiten.Count == 0 && lecon.VideoMediumId is null && f.Bloecke.All(b => b.Fragen.Count == 0))
                    throw new FachFehler("Ajoutez au moins une page, une vidéo ou un exercice avant de marquer la leçon comme prête.");
                leconId = lecon.Id;
                f.Id = lecon.Id;
            }
            else
            {
                var pruefung = f.Id is { } id
                    ? await db.Pruefungen.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden)
                    : null;
                if (pruefung is null)
                {
                    var autor = akteur.IstDirektion ? f.AutorId : akteur.Id;
                    await PruefeNeuenAutorAsync(db, akteur, autor, f, ct);
                    pruefung = new Pruefung { SchuleId = schuleId, AutorId = autor, ErfasstVonId = autor != akteur.Id ? akteur.Id : null };
                    db.Pruefungen.Add(pruefung);
                }
                else
                {
                    InhaltsRechte.PruefeInhalt(akteur, pruefung.SchuleId, pruefung.AutorId);
                    await PruefeStufeFachAenderungAsync(db, pruefung.AutorId, pruefung.StufeId, pruefung.FachId, f, z => z.PruefungId == pruefung.Id, ct);
                }
                pruefung.Titel = f.Titel.Trim();
                pruefung.StufeId = f.StufeId!.Value;
                pruefung.FachId = f.FachId!.Value;
                pruefung.Beschreibung = Leer(f.Beschreibung);
                pruefung.DauerMinuten = f.DauerMinuten;
                pruefung.Bestehensgrenze = Math.Round(f.Bestehensgrenze, 2);
                pruefung.Status = f.Status;
                if (pruefung.Status == InhaltsStatus.Prete && f.Bloecke.All(b => b.Fragen.Count == 0))
                    throw new FachFehler("Ajoutez au moins une question avant de marquer l'examen comme prêt.");
                pruefungId = pruefung.Id;
                f.Id = pruefung.Id;
            }
            await db.SaveChangesAsync(ct);
            await abgleich.AbgleichenAsync(db, schuleId, leconId, pruefungId, f.Bloecke, ct);
            await MedienNutzungAsync(db, schuleId, f.Art, f.Id!.Value, ct);
            return f.Id!.Value;
        }, ct);
    }

    private static void PruefeKopf(InhaltFormular f)
    {
        if (string.IsNullOrWhiteSpace(f.Titel)) throw new FachFehler("Veuillez saisir un titre.", "Titel");
        if (f.Titel.Trim().Length > 200) throw new FachFehler("Le titre est trop long (200 caractères au plus).", "Titel");
        if (f.StufeId is null || f.FachId is null) throw new FachFehler("Choisissez le niveau et la matière.", "StufeFach");
        if ((f.Zusammenfassung?.Trim().Length ?? 0) > 1000) throw new FachFehler("Le résumé est trop long (1000 caractères au plus).", "Zusammenfassung");
        if ((f.Thema?.Trim().Length ?? 0) > 200) throw new FachFehler("Le thème est trop long (200 caractères au plus).", "Thema");
        if (f.Art == InhaltsArt.Pruefung)
        {
            if (f.DauerMinuten is < Core.Paedagogik.PruefungsZeit.MinDauer or > Core.Paedagogik.PruefungsZeit.MaxDauer)
                throw new FachFehler("La durée doit être comprise entre 1 et 600 minutes.", "Dauer");
            if (f.Bestehensgrenze is < 0 or > 100) throw new FachFehler("Le seuil de réussite doit être compris entre 0 et 100 %.", "Bestehensgrenze");
            if ((f.Beschreibung?.Trim().Length ?? 0) > 2000) throw new FachFehler("Les consignes générales sont trop longues (2000 caractères au plus).", "Beschreibung");
        }
    }

    private List<string> BereinigteSeiten(InhaltFormular f)
    {
        if (f.Seiten.Count > MaxSeiten) throw new FachFehler($"Une leçon peut contenir au plus {MaxSeiten} pages.");
        var seiten = f.Seiten.Select(s => bereiniger.Bereinige(s.Html)).Where(h => h.Length > 0).ToList();
        if (seiten.Any(s => s.Length > MaxSeitenLaenge)) throw new FachFehler("Une page est trop longue. Répartissez le contenu sur plusieurs pages.");
        return seiten;
    }

    private static async Task PruefeNeuenAutorAsync(SkoulouDbContext db, Akteur akteur, Guid autorId, InhaltFormular f, CancellationToken ct)
    {
        if (autorId != akteur.Id) await InhaltsRechte.PruefeLehrkraftAsync(db, akteur.SchuleId!.Value, autorId, ct);
        if (!(await KombinationenAsync(db, autorId, ct)).Any(k => k.StufeId == f.StufeId && k.FachId == f.FachId))
            throw new FachFehler("Ce niveau et cette matière ne font pas partie des affectations de l'enseignant.", "StufeFach");
    }

    private static async Task PruefeStufeFachAenderungAsync(SkoulouDbContext db, Guid autorId, Guid stufeId, Guid fachId, InhaltFormular f,
        System.Linq.Expressions.Expression<Func<Zuweisung, bool>> zuweisungen, CancellationToken ct)
    {
        if (f.StufeId == stufeId && f.FachId == fachId) return;
        if (await db.Zuweisungen.AnyAsync(zuweisungen, ct))
            throw new FachFehler("Le niveau et la matière ne peuvent plus changer : ce contenu est déjà attribué à une classe.", "StufeFach");
        if (!(await KombinationenAsync(db, autorId, ct)).Any(k => k.StufeId == f.StufeId && k.FachId == f.FachId))
            throw new FachFehler("Ce niveau et cette matière ne font pas partie des affectations de l'enseignant.", "StufeFach");
    }

    private static async Task<Guid?> PruefeVideoAsync(SkoulouDbContext db, Guid schuleId, Guid? videoId, CancellationToken ct)
    {
        if (videoId is not { } id) return null;
        if (!await db.Medien.AnyAsync(m => m.Id == id && m.SchuleId == schuleId && m.Art == MedienArt.Video, ct))
            throw new FachFehler("La vidéo choisie n'est pas disponible dans votre établissement.", "Video");
        return id;
    }

    /// <summary>Medien-Nutzungen des Inhalts aus Seiten, Fragen (auch archivierten Revisionen) und Video ableiten.</summary>
    private static async Task MedienNutzungAsync(SkoulouDbContext db, Guid schuleId, InhaltsArt art, Guid id, CancellationToken ct)
    {
        var texte = new List<string>();
        Guid? video = null;
        if (art == InhaltsArt.Lecon)
        {
            texte.AddRange(await db.LeconSeiten.Where(s => s.LeconId == id).Select(s => s.Html).ToListAsync(ct));
            video = await db.Lecons.Where(l => l.Id == id).Select(l => l.VideoMediumId).FirstOrDefaultAsync(ct);
        }
        texte.AddRange(await db.Fragen.Where(f => db.AufgabenBloecke.Any(b => b.Id == f.BlockId && (art == InhaltsArt.Lecon ? b.LeconId == id : b.PruefungId == id)))
            .Select(f => f.TextHtml).ToListAsync(ct));
        var bilder = texte.SelectMany(InhaltBereiniger.MedienIds).Distinct().ToList();
        await MedienService.NutzungAbgleichenAsync(db, schuleId, art == InhaltsArt.Lecon ? MedienService.NutzungLecon : MedienService.NutzungPruefung,
            id, bilder, video, ct);
    }

    // ---------- Status, Duplizieren, Löschen, Übertragen ----------

    public async Task StatusSetzenAsync(Guid akteurId, InhaltsArt art, Guid id, InhaltsStatus status, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (art == InhaltsArt.Lecon)
        {
            var l = await db.Lecons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
            InhaltsRechte.PruefeInhalt(akteur, l.SchuleId, l.AutorId);
            l.Status = status;
        }
        else
        {
            var p = await db.Pruefungen.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
            InhaltsRechte.PruefeInhalt(akteur, p.SchuleId, p.AutorId);
            if (status == InhaltsStatus.Prete && !await db.Fragen.AnyAsync(f => !f.Archiviert && db.AufgabenBloecke.Any(b => b.Id == f.BlockId && b.PruefungId == id && !b.Archiviert), ct))
                throw new FachFehler("Ajoutez au moins une question avant de marquer l'examen comme prêt.");
            p.Status = status;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Kopie als Entwurf (« Réutiliser »): Seiten, Blöcke und aktuelle Fragen; optional andere Stufe/anderes Fach.</summary>
    public async Task<Guid> DuplizierenAsync(Guid akteurId, InhaltsArt art, Guid id, CancellationToken ct = default)
    {
        var quelle = await LadeAsync(akteurId, art, id, ct);
        var kopie = new InhaltFormular
        {
            Art = art, AutorId = quelle.AutorId, Titel = Kuerze(quelle.Titel + " (copie)", 200), StufeId = quelle.StufeId, FachId = quelle.FachId,
            Zusammenfassung = quelle.Zusammenfassung, Thema = quelle.Thema, VideoMediumId = quelle.VideoMediumId,
            Seiten = quelle.Seiten.Select(s => new SeiteFormular { Html = s.Html }).ToList(),
            Beschreibung = quelle.Beschreibung, DauerMinuten = quelle.DauerMinuten, Bestehensgrenze = quelle.Bestehensgrenze,
            Bloecke = quelle.Bloecke.Select(b => new BlockFormular
            {
                Titel = b.Titel, Consigne = b.Consigne,
                Fragen = b.Fragen.Select(q => new FrageFormular
                {
                    Typ = q.Typ, TextHtml = q.TextHtml, Punkte = q.Punkte, AudioText = q.AudioText, Sprache = q.Sprache,
                    Varianten = q.Varianten.ToList(), Luecken = q.Luecken.Select(l => l.ToList()).ToList(), Wortbank = q.Wortbank.ToList(),
                    AkzenteBeachten = q.AkzenteBeachten,
                    Optionen = q.Optionen.Select(o => new OptionFormular { Text = o.Text, Richtig = o.Richtig }).ToList()
                }).ToList()
            }).ToList()
        };
        return await SpeichernAsync(akteurId, kopie, ct);
    }

    /// <summary>Löschen nur ohne Zuweisungen (sonst archivieren).</summary>
    public async Task LoeschenAsync(Guid akteurId, InhaltsArt art, Guid id, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await DbTransaktion.AusfuehrenAsync(db, async () =>
        {
            Guid schuleId, autorId;
            if (art == InhaltsArt.Lecon)
            {
                var l = await db.Lecons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
                (schuleId, autorId) = (l.SchuleId, l.AutorId);
            }
            else
            {
                var p = await db.Pruefungen.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
                (schuleId, autorId) = (p.SchuleId, p.AutorId);
            }
            InhaltsRechte.PruefeInhalt(akteur, schuleId, autorId);
            if (await db.Zuweisungen.AnyAsync(z => art == InhaltsArt.Lecon ? z.LeconId == id : z.PruefungId == id, ct))
                throw new FachFehler("Ce contenu a déjà été attribué : archivez-le au lieu de le supprimer.");

            var bloecke = await db.AufgabenBloecke.Where(b => art == InhaltsArt.Lecon ? b.LeconId == id : b.PruefungId == id).ToListAsync(ct);
            var blockIds = bloecke.Select(b => b.Id).ToList();
            db.Fragen.RemoveRange(await db.Fragen.Where(f => blockIds.Contains(f.BlockId)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.AufgabenBloecke.RemoveRange(bloecke);
            db.MedienNutzungen.RemoveRange(await db.MedienNutzungen.Where(n => n.ZielId == id).ToListAsync(ct));
            if (art == InhaltsArt.Lecon) db.Lecons.Remove(await db.Lecons.Include(l => l.Seiten).FirstAsync(l => l.Id == id, ct));
            else db.Pruefungen.Remove(await db.Pruefungen.FirstAsync(p => p.Id == id, ct));
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Direktion: Inhalt an eine andere Lehrkraft übertragen (aktive Zuweisungen gehen mit, Audit).</summary>
    public async Task UebertragenAsync(Guid akteurId, InhaltsArt art, Guid id, Guid neueLehrkraftId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.InhalteFuerLehrerErfassen, schreiben: true);
        var schuleId = akteur.SchuleId!.Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await DbTransaktion.AusfuehrenAsync(db, async () =>
        {
            string titel;
            Guid alterAutor;
            if (art == InhaltsArt.Lecon)
            {
                var l = await db.Lecons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
                if (l.SchuleId != schuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
                (titel, alterAutor) = (l.Titel, l.AutorId);
                l.AutorId = neueLehrkraftId;
            }
            else
            {
                var p = await db.Pruefungen.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
                if (p.SchuleId != schuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
                (titel, alterAutor) = (p.Titel, p.AutorId);
                p.AutorId = neueLehrkraftId;
            }
            await InhaltsRechte.PruefeLehrkraftAsync(db, schuleId, neueLehrkraftId, ct);
            if (alterAutor == neueLehrkraftId) throw new FachFehler("Ce contenu appartient déjà à cet enseignant.");
            foreach (var z in await db.Zuweisungen.Where(z => (art == InhaltsArt.Lecon ? z.LeconId == id : z.PruefungId == id)
                                                              && z.Status == ZuweisungsStatus.Active).ToListAsync(ct))
                z.LehrerId = neueLehrkraftId;
            var namen = await db.Users.Where(k => k.Id == alterAutor || k.Id == neueLehrkraftId)
                .ToDictionaryAsync(k => k.Id, k => k.Vorname + " " + k.Nachname, ct);
            audit.Erfasse(db, akteur.Id, schuleId, AuditUebertragen, art == InhaltsArt.Lecon ? "Lecon" : "Pruefung", id,
                $"« {titel} » transféré de {namen.GetValueOrDefault(alterAutor)} à {namen.GetValueOrDefault(neueLehrkraftId)}.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string Kuerze(string text, int max) => text.Length <= max ? text : text[..max];
}
