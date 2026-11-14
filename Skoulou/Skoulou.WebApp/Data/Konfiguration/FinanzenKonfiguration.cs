using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data.Konfiguration;

// Bereich Finanzen: Geld als long (ganze FCFA), nie kaskadierend löschen, eindeutige Nummern.

internal sealed class GebuehrenPostenKonfiguration : IEntityTypeConfiguration<GebuehrenPosten>
{
    public void Configure(EntityTypeBuilder<GebuehrenPosten> b)
    {
        b.ToTable("GebuehrenPosten");
        b.Schulgebunden();
        b.Property(x => x.Bezeichnung).HasMaxLength(120);
        b.HasIndex(x => new { x.SchuljahrId, x.Reihenfolge });
        b.Fk<GebuehrenPosten, Schuljahr>(x => x.SchuljahrId);
        b.Fk<GebuehrenPosten, Stufe>(x => x.StufeId);
        b.Fk<GebuehrenPosten, Klasse>(x => x.KlasseId);
        b.HasMany(x => x.Raten).WithOne().HasForeignKey(r => r.PostenId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_GebuehrenPosten_Betrag", "[Betrag] >= 0"));
    }
}

internal sealed class GebuehrenRateKonfiguration : IEntityTypeConfiguration<GebuehrenRate>
{
    public void Configure(EntityTypeBuilder<GebuehrenRate> b)
    {
        b.ToTable("GebuehrenRaten");
        b.HasIndex(x => new { x.PostenId, x.Nr }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_GebuehrenRaten_Betrag", "[Betrag] >= 0"));
    }
}

internal sealed class ErmaessigungKonfiguration : IEntityTypeConfiguration<Ermaessigung>
{
    public void Configure(EntityTypeBuilder<Ermaessigung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Wert).HasPrecision(12, 2);
        b.Property(x => x.Grund).HasMaxLength(200);
        b.HasIndex(x => new { x.SchuljahrId, x.SchuelerId });
        b.KontoFk(x => x.SchuelerId);
        b.KontoFk(x => x.ErfasstVonId);
        b.Fk<Ermaessigung, Schuljahr>(x => x.SchuljahrId);
        b.Fk<Ermaessigung, GebuehrenPosten>(x => x.PostenId);
    }
}

internal sealed class ZahlungKonfiguration : IEntityTypeConfiguration<Zahlung>
{
    public void Configure(EntityTypeBuilder<Zahlung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Referenz).HasMaxLength(100);
        b.Property(x => x.Quittungsnummer).HasMaxLength(30);
        b.Property(x => x.Bemerkung).HasMaxLength(300);
        b.Property(x => x.StornoGrund).HasMaxLength(300);
        b.HasIndex(x => new { x.SchuleId, x.Quittungsnummer }).IsUnique();
        b.HasIndex(x => new { x.SchuljahrId, x.SchuelerId });
        b.HasIndex(x => new { x.SchuleId, x.Datum });
        b.KontoFk(x => x.SchuelerId);
        b.KontoFk(x => x.ErfasstVonId);
        b.KontoFk(x => x.StorniertVonId);
        b.Fk<Zahlung, Schuljahr>(x => x.SchuljahrId);
        b.ToTable(t => t.HasCheckConstraint("CK_Zahlungen_Betrag", "[Betrag] > 0"));
    }
}

internal sealed class KategorieKonfiguration : IEntityTypeConfiguration<Kategorie>
{
    public void Configure(EntityTypeBuilder<Kategorie> b)
    {
        b.ToTable("Kategorien");
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.SystemCode).HasMaxLength(40);
        b.HasIndex(x => new { x.SchuleId, x.Richtung, x.Name }).IsUnique().HasFilter(null);
        b.HasIndex(x => x.SystemCode);
        b.Fk<Kategorie, Schule>(x => x.SchuleId);
    }
}

internal sealed class BuchungKonfiguration : IEntityTypeConfiguration<Buchung>
{
    public void Configure(EntityTypeBuilder<Buchung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Text).HasMaxLength(300);
        b.Property(x => x.Nummer).HasMaxLength(30);
        b.HasIndex(x => new { x.SchuleId, x.Nummer }).IsUnique();
        b.HasIndex(x => new { x.SchuleId, x.Datum });
        b.HasIndex(x => new { x.Herkunft, x.HerkunftId });
        b.Fk<Buchung, Kategorie>(x => x.KategorieId);
        b.Fk<Buchung, Medium>(x => x.BelegMediumId);
        b.Fk<Buchung, Buchung>(x => x.StornoVonId);
        // Jede Buchung höchstens einmal aufgehoben (auch bei parallelem Storno).
        b.HasIndex(x => x.StornoVonId).IsUnique().HasFilter("[StornoVonId] IS NOT NULL").HasDatabaseName("IX_Buchungen_EinStorno");
        b.KontoFk(x => x.ErfasstVonId);
        b.ToTable(t => t.HasCheckConstraint("CK_Buchungen_Betrag", "[Betrag] > 0"));
    }
}

internal sealed class PlattformrechnungKonfiguration : IEntityTypeConfiguration<Plattformrechnung>
{
    public void Configure(EntityTypeBuilder<Plattformrechnung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Nummer).HasMaxLength(30);
        b.Property(x => x.Grund).HasMaxLength(500);
        b.HasIndex(x => x.Nummer).IsUnique();
        b.HasIndex(x => new { x.SchuleId, x.PeriodeId });
        b.HasIndex(x => new { x.Status, x.Faellig });
        // Statuswechsel (Melden, Bestätigen, Stornieren) parallel abgesichert.
        b.Property<byte[]>("Version").IsRowVersion();
        b.Fk<Plattformrechnung, Schuljahr>(x => x.SchuljahrId);
        b.Fk<Plattformrechnung, Periode>(x => x.PeriodeId);
        b.Fk<Plattformrechnung, Plattformrechnung>(x => x.ErgaenztId);
        b.KontoFk(x => x.BestaetigtVonId);
        b.HasMany(x => x.Positionen).WithOne().HasForeignKey(p => p.RechnungId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RechnungspositionKonfiguration : IEntityTypeConfiguration<Rechnungsposition>
{
    public void Configure(EntityTypeBuilder<Rechnungsposition> b)
    {
        b.ToTable("Rechnungspositionen");
        b.Property(x => x.StorniertGrund).HasMaxLength(300);
        // Jeder Schüler höchstens einmal je Periode (über nicht stornierte Positionen).
        b.HasIndex(x => new { x.PeriodeId, x.SchuelerId }).IsUnique()
            .HasFilter("[StorniertGrund] IS NULL").HasDatabaseName("IX_Rechnungspositionen_EinmalJePeriode");
        b.Fk<Rechnungsposition, Periode>(x => x.PeriodeId);
        b.KontoFk(x => x.SchuelerId);
        b.Fk<Rechnungsposition, Einschreibung>(x => x.EinschreibungId);
    }
}

internal sealed class ZahlungsmeldungKonfiguration : IEntityTypeConfiguration<Zahlungsmeldung>
{
    public void Configure(EntityTypeBuilder<Zahlungsmeldung> b)
    {
        b.Property(x => x.Referenz).HasMaxLength(100);
        b.HasIndex(x => x.RechnungId);
        b.Fk<Zahlungsmeldung, Plattformrechnung>(x => x.RechnungId);
        b.Fk<Zahlungsmeldung, Medium>(x => x.BelegMediumId);
        b.Fk<Zahlungsmeldung, Buchung>(x => x.BuchungId);
        b.KontoFk(x => x.GemeldetVonId);
    }
}
