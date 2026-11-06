using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
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
        });

        builder.Entity<VerkaufsPosition>(entity =>
        {
            entity.Property(p => p.EinzelPreis).HasPrecision(18, 2);
            entity.Property(p => p.RabattBetrag).HasPrecision(18, 2);
            entity.Property(p => p.ZeilenSumme).HasPrecision(18, 2);
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
