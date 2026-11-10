using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using ENote.WebApp.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<AppUser, Role, Guid, IdentityUserClaim<Guid>, AppUserRole, IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>(options)
{
    public DbSet<Salon> Salons => Set<Salon>();
    public DbSet<UserSalon> UserSalons => Set<UserSalon>();
    public DbSet<GeraetSalonFavorit> GeraetSalonFavoriten => Set<GeraetSalonFavorit>();
    public DbSet<ProduktOwnerSalon> ProduktOwnerSalons => Set<ProduktOwnerSalon>();
    public DbSet<Leistung> Leistungen => Set<Leistung>();
    public DbSet<LeistungKategorie> LeistungKategorien => Set<LeistungKategorie>();
    public DbSet<LeistungFavorit> LeistungFavoriten => Set<LeistungFavorit>();
    public DbSet<Rabatt> Rabatte => Set<Rabatt>();
    public DbSet<KassenSitzung> KassenSitzungen => Set<KassenSitzung>();
    public DbSet<Verkauf> Verkaeufe => Set<Verkauf>();
    public DbSet<VerkaufsPosition> VerkaufsPositionen => Set<VerkaufsPosition>();
    public DbSet<VerkaufZahlung> VerkaufZahlungen => Set<VerkaufZahlung>();
    public DbSet<LagerAbgang> LagerAbgaenge => Set<LagerAbgang>();
    public DbSet<GeldBewegung> GeldBewegungen => Set<GeldBewegung>();
    public DbSet<MobileMoneyAnbieter> MobileMoneyAnbieter => Set<MobileMoneyAnbieter>();
    public DbSet<SalonMobileMoneyAnbieter> SalonMobileMoneyAnbieter => Set<SalonMobileMoneyAnbieter>();
    public DbSet<BesitzerHinweis> BesitzerHinweise => Set<BesitzerHinweis>();
    public DbSet<PlattformPreis> PlattformPreise => Set<PlattformPreis>();
    public DbSet<NutzungsAbo> NutzungsAbos => Set<NutzungsAbo>();
    public DbSet<ProduktOwnerAuszahlung> ProduktOwnerAuszahlungen => Set<ProduktOwnerAuszahlung>();
    public DbSet<AusgabenKategorie> AusgabenKategorien => Set<AusgabenKategorie>();
    public DbSet<Ausgabe> Ausgaben => Set<Ausgabe>();
    public DbSet<BlobMediaAsset> BlobMediaAssets => Set<BlobMediaAsset>();
    public DbSet<ExceptionLog> ExceptionLogs => Set<ExceptionLog>();
    public DbSet<BesitzerBetreuung> BesitzerBetreuungen => Set<BesitzerBetreuung>();
    public DbSet<MonatsPreisStand> MonatsPreisStaende => Set<MonatsPreisStand>();
    public DbSet<VertragsHistorie> VertragsHistorien => Set<VertragsHistorie>();
    public DbSet<PoZahlungseingang> PoZahlungseingaenge => Set<PoZahlungseingang>();
    public DbSet<PoZahlungsanforderung> PoZahlungsanforderungen => Set<PoZahlungsanforderung>();
    public DbSet<PoGeldbewegung> PoGeldbewegungen => Set<PoGeldbewegung>();
    public DbSet<PoAbrechnungsEreignis> PoAbrechnungsEreignisse => Set<PoAbrechnungsEreignis>();
    public DbSet<MailNachricht> MailNachrichten => Set<MailNachricht>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(u => u.NormalizedEmail);
            entity.HasMany(u => u.UserRoles)
                .WithOne(ur => ur.User)
                .HasForeignKey(ur => ur.UserId)
                .IsRequired();
        });

        builder.Entity<Role>(entity =>
        {
            entity.HasMany(r => r.UserRoles)
                .WithOne(ur => ur.Role)
                .HasForeignKey(ur => ur.RoleId)
                .IsRequired();
        });

        builder.Entity<Salon>(entity =>
        {
            entity.HasIndex(s => s.Name).IsUnique();
            entity.Property(s => s.IndividuellerMonatsPreis).HasPrecision(18, 2);
            entity.HasOne(s => s.LogoAsset)
                .WithMany()
                .HasForeignKey(s => s.LogoAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<UserSalon>(entity =>
        {
            entity.HasIndex(us => new { us.AppUserId, us.SalonId }).IsUnique();
            entity.HasIndex(us => us.AppUserId).IsUnique();
            entity.HasOne(us => us.Salon)
                .WithMany(s => s.UserSalons)
                .HasForeignKey(us => us.SalonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Leistung>(entity =>
        {
            entity.Property(l => l.Preis).HasPrecision(18, 2);
            entity.Property(l => l.Kaufpreis).HasPrecision(18, 2);
            entity.Property(l => l.Stueckzahl).HasPrecision(18, 2);
            entity.Property(l => l.MindestbestandWarnung).HasPrecision(18, 2);
            entity.Property(l => l.BasisAbzugMenge).HasPrecision(18, 2);
            entity.HasOne(l => l.Salon)
                .WithMany()
                .HasForeignKey(l => l.SalonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(l => l.BasisLeistung)
                .WithMany()
                .HasForeignKey(l => l.BasisLeistungId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(l => l.Kategorie)
                .WithMany()
                .HasForeignKey(l => l.KategorieId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<LeistungKategorie>(entity =>
        {
            entity.HasIndex(k => new { k.SalonId, k.Name }).IsUnique();
            entity.HasOne(k => k.Salon)
                .WithMany()
                .HasForeignKey(k => k.SalonId)
                .OnDelete(DeleteBehavior.Cascade);
            // Selbstreferenz für Unterkategorien: kein Cascade (vermeidet Zyklen), Parent optional.
            entity.HasOne(k => k.Parent)
                .WithMany()
                .HasForeignKey(k => k.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LeistungFavorit>(entity =>
        {
            entity.HasIndex(f => new { f.SalonId, f.LeistungId }).IsUnique();
            entity.HasOne(f => f.Leistung)
                .WithMany()
                .HasForeignKey(f => f.LeistungId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Rabatt>(entity =>
        {
            entity.Property(r => r.Wert).HasPrecision(18, 2);
            entity.HasOne(r => r.Salon)
                .WithMany()
                .HasForeignKey(r => r.SalonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.Leistung)
                .WithMany()
                .HasForeignKey(r => r.LeistungId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<KassenSitzung>(entity =>
        {
            entity.Property(k => k.Startbestand).HasPrecision(18, 2);
            entity.Property(k => k.EndbestandGezaehlt).HasPrecision(18, 2);
            entity.Property(k => k.BareinnahmenSoll).HasPrecision(18, 2);
            entity.Property(k => k.Differenz).HasPrecision(18, 2);
            entity.HasOne(k => k.Salon)
                .WithMany()
                .HasForeignKey(k => k.SalonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(k => k.Kassierer)
                .WithMany()
                .HasForeignKey(k => k.KassiererId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Verkauf>(entity =>
        {
            entity.Property(v => v.Zwischensumme).HasPrecision(18, 2);
            entity.Property(v => v.RabattSumme).HasPrecision(18, 2);
            entity.Property(v => v.GesamtSumme).HasPrecision(18, 2);
            entity.Property(v => v.Gegeben).HasPrecision(18, 2);
            entity.Property(v => v.Rueckgeld).HasPrecision(18, 2);
            entity.HasOne(v => v.KassenSitzung)
                .WithMany(k => k.Verkaeufe)
                .HasForeignKey(v => v.KassenSitzungId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(v => v.StornoGrund).HasMaxLength(300);
            entity.Property(v => v.StornoErfasstVonName).HasMaxLength(200);
            entity.HasOne(v => v.StornoVonVerkauf)
                .WithMany()
                .HasForeignKey(v => v.StornoVonVerkaufId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Ignore(v => v.IstStorno);
        });

        builder.Entity<MailNachricht>(entity =>
        {
            entity.Property(m => m.Empfaenger).HasMaxLength(320);
            entity.Property(m => m.Betreff).HasMaxLength(300);
            entity.Property(m => m.Kategorie).HasMaxLength(50);
            entity.Property(m => m.LetzterFehler).HasMaxLength(1000);
            entity.Property(m => m.AnbieterId).HasMaxLength(100);
            entity.HasIndex(m => new { m.IstGesendet, m.CreatedAtUtc });
        });

        builder.Entity<LagerAbgang>(entity =>
        {
            entity.Property(a => a.Menge).HasPrecision(18, 2);
            entity.Property(a => a.EinkaufspreisSnapshot).HasPrecision(18, 2);
            entity.Property(a => a.VerkaufspreisSnapshot).HasPrecision(18, 2);
            entity.Property(a => a.LeistungName).HasMaxLength(200);
            entity.Property(a => a.ErfasstVonName).HasMaxLength(200);
            entity.Property(a => a.Notiz).HasMaxLength(500);
            entity.Ignore(a => a.Einkaufswert);
            entity.HasIndex(a => new { a.SalonId, a.ZeitUtc });
            entity.HasOne(a => a.Leistung).WithMany().HasForeignKey(a => a.LeistungId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Salon>().WithMany().HasForeignKey(a => a.SalonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<KassenSitzung>().WithMany().HasForeignKey(a => a.KassenSitzungId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GeldBewegung>(entity =>
        {
            entity.Property(g => g.Betrag).HasPrecision(18, 2);
            entity.Property(g => g.Herkunft).HasMaxLength(120);
            entity.Property(g => g.Notiz).HasMaxLength(500);
            entity.Property(g => g.ErfasstVonName).HasMaxLength(200);
            entity.Ignore(g => g.Vorzeichenbetrag);
            entity.HasIndex(g => new { g.SalonId, g.ZeitUtc });
            entity.HasIndex(g => g.KassenSitzungId);
            entity.HasOne<Salon>().WithMany().HasForeignKey(g => g.SalonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<KassenSitzung>().WithMany().HasForeignKey(g => g.KassenSitzungId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VerkaufZahlung>(entity =>
        {
            entity.Property(z => z.Betrag).HasPrecision(18, 2);
            entity.Property(z => z.AnbieterName).HasMaxLength(80);
            entity.Property(z => z.Referenz).HasMaxLength(80);
            entity.HasIndex(z => new { z.VerkaufId, z.Reihenfolge });
            entity.HasOne(z => z.Verkauf)
                .WithMany(v => v.Zahlungen)
                .HasForeignKey(z => z.VerkaufId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(z => z.MobileMoneyAnbieter)
                .WithMany()
                .HasForeignKey(z => z.MobileMoneyAnbieterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MobileMoneyAnbieter>(entity =>
        {
            entity.Property(a => a.Name).HasMaxLength(80).IsRequired();
            entity.Property(a => a.Land).HasMaxLength(80).IsRequired();
            entity.Property(a => a.Farbe).HasMaxLength(7).IsRequired();
            entity.HasIndex(a => new { a.Land, a.Name }).IsUnique();
            entity.HasData(MobileMoneyAnbieterStammdaten.Alle);
        });

        builder.Entity<SalonMobileMoneyAnbieter>(entity =>
        {
            entity.HasIndex(x => new { x.SalonId, x.AnbieterId }).IsUnique();
            entity.HasOne(x => x.Salon).WithMany().HasForeignKey(x => x.SalonId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Anbieter).WithMany().HasForeignKey(x => x.AnbieterId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VerkaufsPosition>(entity =>
        {
            entity.Property(p => p.EinzelPreis).HasPrecision(18, 2);
            entity.Property(p => p.RabattBetrag).HasPrecision(18, 2);
            entity.Property(p => p.ZeilenSumme).HasPrecision(18, 2);
            entity.HasIndex(p => p.StornoVonPositionId);
            entity.HasOne(p => p.Verkauf)
                .WithMany(v => v.Positionen)
                .HasForeignKey(p => p.VerkaufId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Leistung)
                .WithMany()
                .HasForeignKey(p => p.LeistungId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(p => p.Rabatt)
                .WithMany()
                .HasForeignKey(p => p.RabattId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BesitzerHinweis>(entity =>
        {
            entity.HasIndex(h => new { h.SalonId, h.IstUngelesen });
            entity.HasOne(h => h.Salon)
                .WithMany()
                .HasForeignKey(h => h.SalonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PlattformPreis>(entity =>
        {
            entity.Property(p => p.Preis).HasPrecision(18, 2);
        });

        builder.Entity<NutzungsAbo>(entity =>
        {
            entity.Property(a => a.PreisSnapshot).HasPrecision(18, 2);
            entity.HasOne(a => a.Salon)
                .WithMany()
                .HasForeignKey(a => a.SalonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GeraetSalonFavorit>(entity =>
        {
            entity.HasIndex(g => g.GeraeteSchluessel).IsUnique();
            entity.HasOne(g => g.Salon)
                .WithMany()
                .HasForeignKey(g => g.SalonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProduktOwnerSalon>(entity =>
        {
            entity.HasIndex(p => new { p.ProduktOwnerId, p.SalonId }).IsUnique();
            // Ein Laden hat genau einen aktiven verantwortlichen PO (eindeutige Provision/Abrechnung).
            // Archivierte Zuordnungen (GueltigBisUtc gesetzt) bleiben für die Historie erhalten.
            entity.HasIndex(p => p.SalonId).IsUnique().HasFilter("[GueltigBisUtc] IS NULL");
            entity.Property(p => p.ProzentProvision).HasPrecision(5, 2);
            entity.Property(p => p.FestbetragProvision).HasPrecision(18, 2);
            entity.HasOne(p => p.Salon)
                .WithMany()
                .HasForeignKey(p => p.SalonId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.ProduktOwner)
                .WithMany()
                .HasForeignKey(p => p.ProduktOwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AusgabenKategorie>(entity =>
        {
            entity.HasOne(k => k.Salon)
                .WithMany()
                .HasForeignKey(k => k.SalonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Ausgabe>(entity =>
        {
            entity.Property(a => a.Betrag).HasPrecision(18, 2);
            entity.HasOne(a => a.Salon)
                .WithMany()
                .HasForeignKey(a => a.SalonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.Kategorie)
                .WithMany()
                .HasForeignKey(a => a.KategorieId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.BelegAsset)
                .WithMany()
                .HasForeignKey(a => a.BelegAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ProduktOwnerAuszahlung>(entity =>
        {
            entity.Property(a => a.Betrag).HasPrecision(18, 2);
            entity.HasIndex(a => new { a.ProduktOwnerId, a.Jahr, a.Monat }).IsUnique();
            entity.HasOne(a => a.ProduktOwner)
                .WithMany()
                .HasForeignKey(a => a.ProduktOwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
