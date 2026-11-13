using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data.Konfiguration;

// Bereich Plattform: Konten, Schulen, Einstellungen, Nummernkreise, Kommunikation, Protokolle, Medien.

internal sealed class KontoKonfiguration : IEntityTypeConfiguration<Konto>
{
    public void Configure(EntityTypeBuilder<Konto> b)
    {
        b.Property(x => x.Vorname).HasMaxLength(100);
        b.Property(x => x.Nachname).HasMaxLength(100);
        b.Property(x => x.Telefon).HasMaxLength(50);
        b.Property(x => x.Rolle).HasMaxLength(30);
        b.Property(x => x.AgbVersion).HasMaxLength(32);
        b.Property(x => x.PhoneNumber).HasMaxLength(50);
        b.HasOne(x => x.Schule).WithMany().HasForeignKey(x => x.SchuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchuleId, x.Rolle, x.Status });
        b.HasIndex(x => x.Rolle);
        b.Fk<Konto, Medium>(x => x.FotoMediumId);
        b.Ignore(x => x.VollerName);
        // E-Mail bewusst NICHT eindeutig (Identity legt nur den nicht eindeutigen « EmailIndex » an).
    }
}

internal sealed class RolleKonfiguration : IEntityTypeConfiguration<Rolle>
{
    public void Configure(EntityTypeBuilder<Rolle> b) => b.Property(x => x.Beschreibung).HasMaxLength(300);
}

internal sealed class SchuleKonfiguration : IEntityTypeConfiguration<Schule>
{
    public void Configure(EntityTypeBuilder<Schule> b)
    {
        b.ToTable("Schulen");
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.NameNormalisiert).HasMaxLength(200);
        b.HasIndex(x => x.NameNormalisiert).IsUnique();
        b.Property(x => x.Ort).HasMaxLength(120);
        b.Property(x => x.Land).HasMaxLength(120);
        b.Property(x => x.StatusGrund).HasMaxLength(500);
        b.Property(x => x.Zeitzone).HasMaxLength(64);
        b.Property(x => x.OffizielleBezeichnung).HasMaxLength(200);
        b.Property(x => x.Slogan).HasMaxLength(200);
        b.Property(x => x.Adresse).HasMaxLength(300);
        b.Property(x => x.Telefon).HasMaxLength(60);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Zulassungsnummer).HasMaxLength(100);
        b.Property(x => x.Ministerium).HasMaxLength(300);
        b.Property(x => x.RepublikZeile).HasMaxLength(300);
        b.Property(x => x.StaatsMotto).HasMaxLength(300);
        b.Property(x => x.DirectionRegionale).HasMaxLength(200);
        b.Property(x => x.Statut).HasMaxLength(60);
        b.Property(x => x.MatrikelPraefix).HasMaxLength(6);
        b.Property(x => x.ResponsableProzent).HasPrecision(5, 2);
        b.HasIndex(x => x.Status);
        b.KontoFk(x => x.ResponsableId);
        b.Fk<Schule, Medium>(x => x.LogoMediumId);
    }
}

internal sealed class PlattformEinstellungKonfiguration : IEntityTypeConfiguration<PlattformEinstellung>
{
    public void Configure(EntityTypeBuilder<PlattformEinstellung> b)
    {
        b.Property(x => x.AgbVersion).HasMaxLength(32);
        b.Property(x => x.ResponsableProzentStandard).HasPrecision(5, 2);
    }
}

internal sealed class NummernkreisKonfiguration : IEntityTypeConfiguration<Nummernkreis>
{
    public void Configure(EntityTypeBuilder<Nummernkreis> b)
    {
        // SQL Server behandelt NULL im Unique-Index als gleich: ein globaler Kreis je Art und Jahr.
        // HasFilter(null): sonst setzt EF « SchuleId IS NOT NULL » und globale Kreise wären nicht eindeutig.
        b.HasIndex(x => new { x.SchuleId, x.Art, x.Jahr }).IsUnique().HasFilter(null);
        b.Fk<Nummernkreis, Schule>(x => x.SchuleId);
    }
}

internal sealed class AnkuendigungKonfiguration : IEntityTypeConfiguration<Ankuendigung>
{
    public void Configure(EntityTypeBuilder<Ankuendigung> b)
    {
        b.Property(x => x.Titel).HasMaxLength(200);
        b.HasIndex(x => new { x.StartUtc, x.EndeUtc });
        b.Fk<Ankuendigung, Schule>(x => x.SchuleId);
        b.KontoFk(x => x.ErstelltVonId);
    }
}

internal sealed class NachrichtKonfiguration : IEntityTypeConfiguration<Nachricht>
{
    public void Configure(EntityTypeBuilder<Nachricht> b)
    {
        b.Property(x => x.Von).HasMaxLength(256);
        b.Property(x => x.An).HasMaxLength(1000);
        b.Property(x => x.Betreff).HasMaxLength(300);
        b.Property(x => x.PostmarkId).HasMaxLength(100);
        b.HasIndex(x => x.PostmarkId).IsUnique().HasFilter("[PostmarkId] IS NOT NULL");
        b.HasIndex(x => new { x.SchuleId, x.ErstelltAmUtc });
        b.HasIndex(x => new { x.Richtung, x.ErstelltAmUtc });
        b.Fk<Nachricht, Schule>(x => x.SchuleId);
        b.Fk<Nachricht, Nachricht>(x => x.AntwortAufId);
        b.KontoFk(x => x.GesendetVonId);
    }
}

internal sealed class MailAusgangKonfiguration : IEntityTypeConfiguration<MailAusgang>
{
    public void Configure(EntityTypeBuilder<MailAusgang> b)
    {
        b.ToTable("MailAusgaenge");
        b.Property(x => x.An).HasMaxLength(256);
        b.Property(x => x.AntwortAn).HasMaxLength(256);
        b.Property(x => x.Betreff).HasMaxLength(300);
        b.Property(x => x.Fehler).HasMaxLength(1000);
        b.Property(x => x.Bezug).HasMaxLength(100);
        b.Property(x => x.PostmarkId).HasMaxLength(100);
        b.HasIndex(x => new { x.Status, x.NaechsterVersuchUtc });
        b.HasIndex(x => x.ErstelltAmUtc);
    }
}

internal sealed class FehlerProtokollKonfiguration : IEntityTypeConfiguration<FehlerProtokoll>
{
    public void Configure(EntityTypeBuilder<FehlerProtokoll> b)
    {
        b.Property(x => x.Stufe).HasMaxLength(20);
        b.Property(x => x.Nachricht).HasMaxLength(4000);
        b.Property(x => x.Typ).HasMaxLength(500);
        b.Property(x => x.Pfad).HasMaxLength(2000);
        b.HasIndex(x => x.ZeitUtc);
        b.HasIndex(x => new { x.Bearbeitet, x.ZeitUtc });
        // Kein FK auf Konto/Schule: Protokolle dürfen nie eine Löschung blockieren oder scheitern.
    }
}

internal sealed class AuditEintragKonfiguration : IEntityTypeConfiguration<AuditEintrag>
{
    public void Configure(EntityTypeBuilder<AuditEintrag> b)
    {
        b.Property(x => x.Aktion).HasMaxLength(100);
        b.Property(x => x.ZielTyp).HasMaxLength(100);
        b.Property(x => x.Text).HasMaxLength(2000);
        b.HasIndex(x => new { x.SchuleId, x.ZeitUtc });
        b.HasIndex(x => x.ZeitUtc);
        b.HasIndex(x => new { x.ZielTyp, x.ZielId });
    }
}

internal sealed class MediumKonfiguration : IEntityTypeConfiguration<Medium>
{
    public void Configure(EntityTypeBuilder<Medium> b)
    {
        b.ToTable("Medien");
        b.Property(x => x.BlobName).HasMaxLength(600);
        b.HasIndex(x => x.BlobName).IsUnique();
        b.Property(x => x.Dateiname).HasMaxLength(260);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.HasIndex(x => new { x.SchuleId, x.ErstelltAmUtc });
        b.Fk<Medium, Schule>(x => x.SchuleId);
        b.HasOne<Konto>().WithMany().HasForeignKey(x => x.BesitzerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MedienNutzungKonfiguration : IEntityTypeConfiguration<MedienNutzung>
{
    public void Configure(EntityTypeBuilder<MedienNutzung> b)
    {
        b.Property(x => x.Typ).HasMaxLength(60);
        b.HasIndex(x => new { x.MediumId, x.Typ, x.ZielId }).IsUnique();
        b.HasIndex(x => new { x.Typ, x.ZielId });
        b.Fk<MedienNutzung, Medium>(x => x.MediumId);
    }
}

internal sealed class ResponsableAnteilKonfiguration : IEntityTypeConfiguration<ResponsableAnteil>
{
    public void Configure(EntityTypeBuilder<ResponsableAnteil> b)
    {
        b.Property(x => x.Prozent).HasPrecision(5, 2);
        b.Property(x => x.AuszahlungsReferenz).HasMaxLength(100);
        b.HasIndex(x => new { x.ResponsableId, x.AusgezahltAmUtc });
        b.KontoFk(x => x.ResponsableId);
        b.KontoFk(x => x.AusgezahltVonId);
        b.Fk<ResponsableAnteil, Plattformrechnung>(x => x.RechnungId);
        b.Fk<ResponsableAnteil, Schule>(x => x.SchuleId);
        b.Fk<ResponsableAnteil, ResponsableAnteil>(x => x.AusgleichFuerId);
    }
}
