using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Organisation;

/// <summary>E7b-Zusatz 5: Seite /direction/journal – Journal der eigenen Schule, nur für den Directeur.</summary>
public class SchulJournalTests
{
    [Fact]
    public async Task Direktor_sieht_nur_das_Journal_seiner_Schule_mit_Bezeichnung()
    {
        var w = await OrgWelt.ErstelleAsync();
        var journal = new ProtokollService(w.D.Db, w.D.Zugriff, w.D.Zeit);
        await w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, w.Klasse6B, new(2026, 11, 2));

        var (eintraege, _) = await journal.SchulJournalAsync(w.Direktor, new ProtokollFilter(), null);
        var wechsel = Assert.Single(eintraege, e => e.Aktion == OrgAudit.KlasseGewechselt);
        Assert.Equal("Changement de classe", wechsel.Bezeichnung);
        Assert.Contains("6e B", wechsel.Text, StringComparison.Ordinal);
        Assert.Equal("Directeur", Skoulou.Core.Rechte.Rollen.Bezeichnung(wechsel.AkteurRolle));

        // Filter nach Bereich und Suchtext.
        Assert.All((await journal.SchulJournalAsync(w.Direktor, new ProtokollFilter(), "einschreibung")).Eintraege, e => Assert.StartsWith("einschreibung.", e.Aktion));
        Assert.Empty((await journal.SchulJournalAsync(w.Direktor, new ProtokollFilter("introuvable-xyz"), null)).Eintraege);

        // Andere Schule sieht nichts davon; Koordinator und Lehrkraft haben keinen Zugriff.
        Assert.DoesNotContain((await journal.SchulJournalAsync(w.DirektorB, new ProtokollFilter(), null)).Eintraege, e => e.Id == wechsel.Id);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => journal.SchulJournalAsync(w.Koordinator, new ProtokollFilter(), null));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => journal.SchulJournalAsync(w.Lehrer, new ProtokollFilter(), null));
    }
}
