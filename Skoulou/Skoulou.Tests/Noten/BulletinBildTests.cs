using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>Werkzeug (nur mit SKOULOU_E5_BILD=Ordner und SKOULOU_E5_DB=Verbindung): neuestes Bulletin einer lokalen DB als PNG für die Sichtprüfung.</summary>
public class BulletinBildTests
{
    private sealed class BildFact : FactAttribute
    {
        public BildFact()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKOULOU_E5_BILD"))) Skip = "Nur mit SKOULOU_E5_BILD.";
        }
    }

    [BildFact]
    public async Task Neuestes_Bulletin_als_Bild()
    {
        var ordner = Environment.GetEnvironmentVariable("SKOULOU_E5_BILD")!;
        await using var db = new SkoulouDbContext(new DbContextOptionsBuilder<SkoulouDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("SKOULOU_E5_DB")).Options);
        var b = await db.Bulletins.Where(x => x.IstRectificatif).OrderByDescending(x => x.VeroeffentlichtAmUtc).FirstAsync();
        var snapshot = BulletinSnapshot.AusJson(b.SnapshotJson);
        snapshot = snapshot with { Inhalt = snapshot.Inhalt with { Schule = snapshot.Inhalt.Schule with { Kontakt = "+228 00 00 00 00" } } }; // Adresse im Bild ausblenden
        var seiten = new List<BulletinSeite> { new(snapshot, null, null, b.Status, "https://skoulou.tablosuite.com/verification/" + b.PruefToken) };
        var vorlage = new BulletinVorlage { ZeigeTeilnoten = true, ZeigeFachRang = true, ZeigeFachGruppen = true, ZeigeNoteInWorten = true };
        var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == b.SchuleId);
        schule.Email = null;
        var beispiel = VorlagenService.Beispiel(schule, vorlage);
        seiten.Add(new BulletinSeite(new BulletinSnapshot(1, beispiel, new SnapshotDokument("APERÇU — EXEMPLE", "14/11/2026", null, null, false, null, true)), null, null, BulletinStatus.Publie, null));
        BulletinPdf.Erzeuge(seiten);
        BulletinPdf.Dokument(seiten).GenerateImages(i => Path.Combine(ordner, $"17-bulletin-pdf-{i}.png"), new ImageGenerationSettings { RasterDpi = 110 });
    }
}
