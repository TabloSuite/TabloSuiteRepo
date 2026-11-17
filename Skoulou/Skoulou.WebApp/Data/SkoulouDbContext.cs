using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data;

/// <summary>
/// Datenbank von SKOULOU: Identity (Konten, Rollen) und alle Entitäten des Bauplans §4.
/// Konfiguration je Bereich unter <c>Data/Konfiguration</c>; Löschungen sind standardmäßig Restrict.
/// </summary>
public class SkoulouDbContext(DbContextOptions<SkoulouDbContext> options)
    : IdentityDbContext<Konto, Rolle, Guid>(options)
{
    // Plattform
    public DbSet<Schule> Schulen => Set<Schule>();
    public DbSet<PlattformEinstellung> PlattformEinstellungen => Set<PlattformEinstellung>();
    public DbSet<Nummernkreis> Nummernkreise => Set<Nummernkreis>();
    public DbSet<Ankuendigung> Ankuendigungen => Set<Ankuendigung>();
    public DbSet<Nachricht> Nachrichten => Set<Nachricht>();
    public DbSet<MailAusgang> MailAusgaenge => Set<MailAusgang>();
    public DbSet<FehlerProtokoll> FehlerProtokolle => Set<FehlerProtokoll>();
    public DbSet<AuditEintrag> AuditEintraege => Set<AuditEintrag>();
    public DbSet<Medium> Medien => Set<Medium>();
    public DbSet<MedienNutzung> MedienNutzungen => Set<MedienNutzung>();
    public DbSet<ResponsableAnteil> ResponsableAnteile => Set<ResponsableAnteil>();

    // Formation der Responsables (17.11.2026)
    public DbSet<FormationRegel> FormationRegeln => Set<FormationRegel>();
    public DbSet<FormationTeilnehmer> FormationTeilnehmer => Set<FormationTeilnehmer>();
    public DbSet<FormationLektionGelesen> FormationLektionenGelesen => Set<FormationLektionGelesen>();
    public DbSet<FormationRunde> FormationRunden => Set<FormationRunde>();
    public DbSet<FormationVersuch> FormationVersuche => Set<FormationVersuch>();

    // Organisation
    public DbSet<Schuljahr> Schuljahre => Set<Schuljahr>();
    public DbSet<Periode> Perioden => Set<Periode>();
    public DbSet<Stufe> Stufen => Set<Stufe>();
    public DbSet<Klasse> Klassen => Set<Klasse>();
    public DbSet<Fach> Faecher => Set<Fach>();
    public DbSet<Klassenfach> Klassenfaecher => Set<Klassenfach>();
    public DbSet<Lehrauftrag> Lehrauftraege => Set<Lehrauftrag>();
    public DbSet<SchuelerProfil> SchuelerProfile => Set<SchuelerProfil>();
    public DbSet<Einschreibung> Einschreibungen => Set<Einschreibung>();
    public DbSet<ElternKind> ElternKinder => Set<ElternKind>();
    public DbSet<Stundenplan> Stundenplaene => Set<Stundenplan>();
    public DbSet<StundenplanEintrag> StundenplanEintraege => Set<StundenplanEintrag>();
    public DbSet<Anwesenheit> Anwesenheiten => Set<Anwesenheit>();

    // Noten, Conseil, Bulletins
    public DbSet<PeriodenKomponente> PeriodenKomponenten => Set<PeriodenKomponente>();
    public DbSet<Bewertung> Bewertungen => Set<Bewertung>();
    public DbSet<Note> Noten => Set<Note>();
    public DbSet<NotenAenderung> NotenAenderungen => Set<NotenAenderung>();
    public DbSet<FachBemerkung> FachBemerkungen => Set<FachBemerkung>();
    public DbSet<KlassenratEintrag> KlassenratEintraege => Set<KlassenratEintrag>();
    public DbSet<Jahresentscheidung> Jahresentscheidungen => Set<Jahresentscheidung>();
    public DbSet<BulletinVorlage> BulletinVorlagen => Set<BulletinVorlage>();
    public DbSet<KlassenVeroeffentlichung> KlassenVeroeffentlichungen => Set<KlassenVeroeffentlichung>();
    public DbSet<Bulletin> Bulletins => Set<Bulletin>();
    public DbSet<BulletinUebergabe> BulletinUebergaben => Set<BulletinUebergabe>();

    // Pädagogik
    public DbSet<Lecon> Lecons => Set<Lecon>();
    public DbSet<LeconSeite> LeconSeiten => Set<LeconSeite>();
    public DbSet<Pruefung> Pruefungen => Set<Pruefung>();
    public DbSet<AufgabenBlock> AufgabenBloecke => Set<AufgabenBlock>();
    public DbSet<Frage> Fragen => Set<Frage>();
    public DbSet<FrageOption> FrageOptionen => Set<FrageOption>();
    public DbSet<Zuweisung> Zuweisungen => Set<Zuweisung>();
    public DbSet<Versuch> Versuche => Set<Versuch>();
    public DbSet<Antwort> Antworten => Set<Antwort>();
    public DbSet<LernStand> LernStaende => Set<LernStand>();
    public DbSet<FormelFavorit> FormelFavoriten => Set<FormelFavorit>();

    // Finanzen
    public DbSet<GebuehrenPosten> GebuehrenPosten => Set<GebuehrenPosten>();
    public DbSet<GebuehrenRate> GebuehrenRaten => Set<GebuehrenRate>();
    public DbSet<Ermaessigung> Ermaessigungen => Set<Ermaessigung>();
    public DbSet<Zahlung> Zahlungen => Set<Zahlung>();
    public DbSet<Kategorie> Kategorien => Set<Kategorie>();
    public DbSet<Buchung> Buchungen => Set<Buchung>();
    public DbSet<Plattformrechnung> Plattformrechnungen => Set<Plattformrechnung>();
    public DbSet<Rechnungsposition> Rechnungspositionen => Set<Rechnungsposition>();
    public DbSet<Zahlungsmeldung> Zahlungsmeldungen => Set<Zahlungsmeldung>();

    protected override void ConfigureConventions(ModelConfigurationBuilder konventionen)
    {
        // Geld ist long (ganze FCFA); Dezimalzahlen sind Noten, Koeffizienten, Prozente.
        konventionen.Properties<decimal>().HavePrecision(9, 2);
        konventionen.Properties<decimal?>().HavePrecision(9, 2);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Konto>().ToTable("Konten");
        builder.Entity<Rolle>().ToTable("Rollen");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("KontoRollen");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("KontoClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("KontoLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("KontoTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RollenClaims");

        builder.ApplyConfigurationsFromAssembly(typeof(SkoulouDbContext).Assembly);

        // Keine kaskadierenden Löschungen in Fachdaten (Archivieren statt Löschen); Ausnahmen
        // (reine Bestandteile wie Seiten, Optionen, Stundenplaneinträge) setzen Cascade ausdrücklich.
        foreach (var fk in builder.Model.GetEntityTypes()
                     .Where(t => t.ClrType.Namespace == typeof(Schule).Namespace)
                     .SelectMany(t => t.GetForeignKeys())
                     .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade && !IstBestandteil(fk.DeclaringEntityType.ClrType)))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    private static bool IstBestandteil(Type typ)
        => typ == typeof(LeconSeite) || typ == typeof(FrageOption) || typ == typeof(StundenplanEintrag) || typ == typeof(FormationVersuch);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetzeZeitstempel();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetzeZeitstempel();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetzeZeitstempel()
    {
        var jetzt = DateTime.UtcNow;
        foreach (var eintrag in ChangeTracker.Entries<IZeitgestempelt>())
        {
            if (eintrag.State == EntityState.Added)
            {
                if (eintrag.Entity.ErstelltAmUtc == default) eintrag.Entity.ErstelltAmUtc = jetzt;
                eintrag.Entity.GeaendertAmUtc = jetzt;
            }
            else if (eintrag.State == EntityState.Modified)
            {
                eintrag.Entity.GeaendertAmUtc = jetzt;
            }
        }
    }
}
