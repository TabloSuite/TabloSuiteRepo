using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Unterricht;

namespace SkoulouApp.WebApp.Data;

internal static class UnterrichtModellKonfiguration
{
    public static void Konfigurieren(ModelBuilder modelBuilder)
    {
        var schule = modelBuilder.Entity<SkoulouApp.Core.DataModels.Schools.School>();
        foreach (var name in new[] { "Slogan", "OffizielleBezeichnung", "DokumentAdresse", "Ministerium", "RepublikZeile", "StaatsMotto" })
            schule.Property<string>(name).HasMaxLength(300);
        schule.Property(x => x.DokumentTelefon).HasMaxLength(60);
        schule.Property(x => x.DokumentEmail).HasMaxLength(256);
        schule.Property(x => x.Zulassungsnummer).HasMaxLength(100);
        var profil = modelBuilder.Entity<SkoulouApp.Core.DataModels.Schools.StudentProfile>();
        profil.Property(x => x.Matrikelnummer).HasMaxLength(60);
        profil.Property(x => x.Geburtsort).HasMaxLength(150);
        profil.HasIndex(x => x.SchoolId);
        // Ein Schülerkonto hat genau ein Profil (eine Schule). Schulwechsel = neues Konto der neuen Schule.
        profil.HasIndex(x => x.AppUserId).IsUnique().HasDatabaseName("IX_StudentProfiles_EinProfilProKonto");
        profil.HasIndex(x => new { x.SchoolId, x.Matrikelnummer }).IsUnique()
            .HasFilter("[Matrikelnummer] IS NOT NULL");

        var jahr = modelBuilder.Entity<Schuljahr>();
        jahr.HasIndex(x => new { x.SchulID, x.Bezeichnung }).IsUnique();
        jahr.HasAlternateKey(x => new { x.Id, x.SchulID });
        jahr.HasOne(x => x.Schule).WithMany().HasForeignKey(x => x.SchulID).OnDelete(DeleteBehavior.Restrict);

        var unterricht = modelBuilder.Entity<UnterrichtsZuordnung>();
        unterricht.HasIndex(x => new { x.SchuljahrID, x.KlasseID, x.FachID, x.LehrerID }).IsUnique();
        unterricht.HasOne(x => x.Schuljahr).WithMany().HasForeignKey(x => new { x.SchuljahrID, x.SchulID })
            .HasPrincipalKey(x => new { x.Id, x.SchulID }).OnDelete(DeleteBehavior.Restrict);
        unterricht.HasOne(x => x.Schule).WithMany().HasForeignKey(x => x.SchulID).OnDelete(DeleteBehavior.Restrict);
        unterricht.HasOne(x => x.Klasse).WithMany().HasForeignKey(x => x.KlasseID).OnDelete(DeleteBehavior.Restrict);
        unterricht.HasOne(x => x.Fach).WithMany().HasForeignKey(x => x.FachID).OnDelete(DeleteBehavior.Restrict);
        unterricht.HasOne(x => x.Lehrer).WithMany().HasForeignKey(x => x.LehrerID).OnDelete(DeleteBehavior.Restrict);

        var schueler = modelBuilder.Entity<SchuelerJahresZuordnung>();
        schueler.HasIndex(x => new { x.SchuljahrID, x.SchuelerID }).IsUnique();
        schueler.HasOne(x => x.Schuljahr).WithMany().HasForeignKey(x => new { x.SchuljahrID, x.SchulID })
            .HasPrincipalKey(x => new { x.Id, x.SchulID }).OnDelete(DeleteBehavior.Restrict);
        schueler.HasOne(x => x.Schule).WithMany().HasForeignKey(x => x.SchulID).OnDelete(DeleteBehavior.Restrict);
        schueler.HasOne(x => x.Klasse).WithMany().HasForeignKey(x => x.KlasseID).OnDelete(DeleteBehavior.Restrict);
        schueler.HasOne(x => x.Schueler).WithMany().HasForeignKey(x => x.SchuelerID).OnDelete(DeleteBehavior.Restrict);

        var plan = modelBuilder.Entity<Stundenplan>();
        plan.Property(x => x.ZeilenVersion).IsRowVersion();
        plan.HasIndex(x => new { x.SchulID, x.SchuljahrID, x.KlasseID });
        plan.HasOne(x => x.Schuljahr).WithMany().HasForeignKey(x => new { x.SchuljahrID, x.SchulID })
            .HasPrincipalKey(x => new { x.Id, x.SchulID }).OnDelete(DeleteBehavior.Restrict);
        plan.HasOne(x => x.Klasse).WithMany().HasForeignKey(x => x.KlasseID).OnDelete(DeleteBehavior.Restrict);
        var eintrag = modelBuilder.Entity<StundenplanEintrag>();
        eintrag.HasOne(x => x.Stundenplan).WithMany().HasForeignKey(x => x.StundenplanID).OnDelete(DeleteBehavior.Restrict);
        eintrag.HasOne(x => x.UnterrichtsZuordnung).WithMany().HasForeignKey(x => x.UnterrichtsZuordnungID).OnDelete(DeleteBehavior.Restrict);
        eintrag.HasIndex(x => new { x.StundenplanID, x.Wochentag, x.Beginn });

        var periode = modelBuilder.Entity<BewertungsPeriode>();
        periode.HasIndex(x => new { x.SchuljahrID, x.Titel }).IsUnique();
        periode.HasOne(x => x.Schuljahr).WithMany().HasForeignKey(x => new { x.SchuljahrID, x.SchulID })
            .HasPrincipalKey(x => new { x.Id, x.SchulID }).OnDelete(DeleteBehavior.Restrict);
        var bestandteil = modelBuilder.Entity<BewertungsBestandteil>();
        bestandteil.HasIndex(x => new { x.PeriodeID, x.Bezeichnung }).IsUnique();
        bestandteil.HasOne(x => x.Periode).WithMany().HasForeignKey(x => x.PeriodeID).OnDelete(DeleteBehavior.Restrict);
        var fach = modelBuilder.Entity<FachBewertung>();
        fach.HasIndex(x => new { x.SchuljahrID, x.KlasseID, x.FachID }).IsUnique();
        fach.HasOne(x => x.Schuljahr).WithMany().HasForeignKey(x => x.SchuljahrID).OnDelete(DeleteBehavior.Restrict);
        fach.HasOne(x => x.Klasse).WithMany().HasForeignKey(x => x.KlasseID).OnDelete(DeleteBehavior.Restrict);
        fach.HasOne(x => x.Fach).WithMany().HasForeignKey(x => x.FachID).OnDelete(DeleteBehavior.Restrict);
        var note = modelBuilder.Entity<FachNote>();
        var historie = modelBuilder.Entity<NotenAenderung>();
        historie.HasIndex(x => new { x.FachNoteID, x.ErstelltAmUtc });
        historie.HasOne(x => x.FachNote).WithMany().HasForeignKey(x => x.FachNoteID).OnDelete(DeleteBehavior.Restrict);
        note.HasIndex(x => new { x.BestandteilID, x.FachBewertungID, x.JahresZuordnungID }).IsUnique();
        note.HasOne(x => x.Bestandteil).WithMany().HasForeignKey(x => x.BestandteilID).OnDelete(DeleteBehavior.Restrict);
        note.HasOne(x => x.FachBewertung).WithMany().HasForeignKey(x => x.FachBewertungID).OnDelete(DeleteBehavior.Restrict);
        note.HasOne(x => x.JahresZuordnung).WithMany().HasForeignKey(x => x.JahresZuordnungID).OnDelete(DeleteBehavior.Restrict);

        var vorlage = modelBuilder.Entity<BulletinVorlage>();
        schule.Property(x => x.DokumentDirection).HasMaxLength(200);
        schule.Property(x => x.DokumentStatut).HasMaxLength(60);
        profil.Property(x => x.Nationalitaet).HasMaxLength(80);
        modelBuilder.Entity<SkoulouApp.Core.DataModels.Schools.Fach>().Property(x => x.Gruppe).HasMaxLength(60);

        // Eltern ↔ Kind (gleiche Schule); eine Verknüpfung je Paar.
        var elternKind = modelBuilder.Entity<SkoulouApp.Core.DataModels.Schools.ElternKind>();
        elternKind.HasIndex(x => new { x.ElternID, x.SchuelerID }).IsUnique();
        elternKind.HasIndex(x => x.SchuelerID);
        elternKind.HasIndex(x => x.SchulID);
        elternKind.HasOne(x => x.Eltern).WithMany().HasForeignKey(x => x.ElternID).OnDelete(DeleteBehavior.Restrict);
        elternKind.HasOne(x => x.Schueler).WithMany().HasForeignKey(x => x.SchuelerID).OnDelete(DeleteBehavior.Restrict);

        // Conseil de classe: eine Entscheidung je Schüler und Periode.
        var klassenrat = modelBuilder.Entity<KlassenratEntscheidung>();
        klassenrat.HasIndex(x => new { x.PeriodeID, x.JahresZuordnungID }).IsUnique();
        klassenrat.HasIndex(x => x.SchulID);
        klassenrat.HasOne(x => x.Periode).WithMany().HasForeignKey(x => x.PeriodeID).OnDelete(DeleteBehavior.Restrict);
        klassenrat.HasOne(x => x.JahresZuordnung).WithMany().HasForeignKey(x => x.JahresZuordnungID).OnDelete(DeleteBehavior.Restrict);

        var anwesenheit = modelBuilder.Entity<AnwesenheitsEintrag>();
        anwesenheit.HasIndex(x => new { x.PeriodeID, x.JahresZuordnungID, x.Datum }).IsUnique();
        anwesenheit.HasOne(x => x.Periode).WithMany().HasForeignKey(x => x.PeriodeID).OnDelete(DeleteBehavior.Restrict);
        anwesenheit.HasOne(x => x.JahresZuordnung).WithMany().HasForeignKey(x => x.JahresZuordnungID).OnDelete(DeleteBehavior.Restrict);
        vorlage.HasIndex(x => new { x.SchulID, x.KlasseID, x.Art }).IsUnique();
        vorlage.HasOne(x => x.Schule).WithMany().HasForeignKey(x => x.SchulID).OnDelete(DeleteBehavior.Restrict);
        vorlage.HasOne(x => x.Klasse).WithMany().HasForeignKey(x => x.KlasseID).OnDelete(DeleteBehavior.Restrict);
        var ausgabe = modelBuilder.Entity<BulletinAusgabe>();
        ausgabe.HasIndex(x => x.PruefToken).IsUnique().HasFilter("[PruefToken] IS NOT NULL");
        var abschluss = modelBuilder.Entity<BulletinAbschluss>();
        abschluss.HasIndex(x => new { x.PeriodeID, x.KlasseID }).IsUnique();
        abschluss.HasOne(x => x.Periode).WithMany().HasForeignKey(x => x.PeriodeID).OnDelete(DeleteBehavior.Restrict);
        var berichtigung = modelBuilder.Entity<BulletinBerichtigung>();
        berichtigung.Property(x => x.StammdatenEntwurfJson).HasDefaultValue("[]");
        berichtigung.HasIndex(x => new { x.SchulID, x.SchuljahrID, x.KlasseID });
        var uebergabe = modelBuilder.Entity<BulletinUebergabe>();
        uebergabe.HasOne(x => x.Ausgabe).WithMany().HasForeignKey(x => x.AusgabeID).OnDelete(DeleteBehavior.Restrict);
        ausgabe.HasIndex(x => new { x.PeriodeID, x.JahresZuordnungID, x.Version }).IsUnique();
        ausgabe.HasIndex(x => new { x.PeriodeID, x.JahresZuordnungID }).IsUnique().HasFilter("[IstVeroeffentlicht] = 1");
        ausgabe.HasOne(x => x.Schule).WithMany().HasForeignKey(x => x.SchulID).OnDelete(DeleteBehavior.Restrict);
        ausgabe.HasOne(x => x.Periode).WithMany().HasForeignKey(x => x.PeriodeID).OnDelete(DeleteBehavior.Restrict);
        ausgabe.HasOne(x => x.JahresZuordnung).WithMany().HasForeignKey(x => x.JahresZuordnungID).OnDelete(DeleteBehavior.Restrict);
    }
}
