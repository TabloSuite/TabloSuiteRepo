using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data.Konfiguration;

// Bereich Noten, Conseil, Bulletins: nie kaskadierend löschen.

internal sealed class PeriodenKomponenteKonfiguration : IEntityTypeConfiguration<PeriodenKomponente>
{
    public void Configure(EntityTypeBuilder<PeriodenKomponente> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Titel).HasMaxLength(80);
        b.Property(x => x.Bareme).HasPrecision(6, 2);
        b.Property(x => x.Gewicht).HasPrecision(5, 2);
        b.HasIndex(x => new { x.PeriodeId, x.Titel }).IsUnique();
        b.Fk<PeriodenKomponente, Periode>(x => x.PeriodeId);
    }
}

internal sealed class BewertungKonfiguration : IEntityTypeConfiguration<Bewertung>
{
    public void Configure(EntityTypeBuilder<Bewertung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Titel).HasMaxLength(120);
        b.Property(x => x.Bareme).HasPrecision(6, 2);
        b.Property(x => x.Gewicht).HasPrecision(5, 2);
        b.HasIndex(x => new { x.KlassenfachId, x.PeriodeId });
        b.Fk<Bewertung, Klassenfach>(x => x.KlassenfachId);
        b.Fk<Bewertung, Periode>(x => x.PeriodeId);
        b.Fk<Bewertung, PeriodenKomponente>(x => x.KomponenteId);
        b.KontoFk(x => x.ErstelltVonId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Bewertungen_Bareme", "[Bareme] > 0");
            t.HasCheckConstraint("CK_Bewertungen_Gewicht", "[Gewicht] > 0");
        });
    }
}

internal sealed class NoteKonfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> b)
    {
        b.ToTable("Noten");
        b.Schulgebunden();
        b.Property(x => x.Punkte).HasPrecision(6, 2);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => new { x.BewertungId, x.EinschreibungId }).IsUnique();
        b.HasIndex(x => x.EinschreibungId);
        b.Fk<Note, Bewertung>(x => x.BewertungId);
        b.Fk<Note, Einschreibung>(x => x.EinschreibungId);
        b.KontoFk(x => x.ErfasstVonId);
    }
}

internal sealed class NotenAenderungKonfiguration : IEntityTypeConfiguration<NotenAenderung>
{
    public void Configure(EntityTypeBuilder<NotenAenderung> b)
    {
        b.ToTable("NotenAenderungen");
        b.Schulgebunden();
        b.Property(x => x.AltePunkte).HasPrecision(6, 2);
        b.Property(x => x.NeuePunkte).HasPrecision(6, 2);
        b.Property(x => x.Grund).HasMaxLength(300);
        b.HasIndex(x => new { x.NoteId, x.ZeitUtc });
        b.Fk<NotenAenderung, Note>(x => x.NoteId);
        b.KontoFk(x => x.GeaendertVonId);
    }
}

internal sealed class FachBemerkungKonfiguration : IEntityTypeConfiguration<FachBemerkung>
{
    public void Configure(EntityTypeBuilder<FachBemerkung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Text).HasMaxLength(300);
        b.HasIndex(x => new { x.KlassenfachId, x.PeriodeId, x.EinschreibungId }).IsUnique();
        b.Fk<FachBemerkung, Klassenfach>(x => x.KlassenfachId);
        b.Fk<FachBemerkung, Periode>(x => x.PeriodeId);
        b.Fk<FachBemerkung, Einschreibung>(x => x.EinschreibungId);
        b.KontoFk(x => x.ErfasstVonId);
    }
}

internal sealed class KlassenratEintragKonfiguration : IEntityTypeConfiguration<KlassenratEintrag>
{
    public void Configure(EntityTypeBuilder<KlassenratEintrag> b)
    {
        b.ToTable("KlassenratEintraege");
        b.Schulgebunden();
        b.Property(x => x.Bemerkung).HasMaxLength(300);
        b.HasIndex(x => new { x.PeriodeId, x.EinschreibungId }).IsUnique();
        b.Fk<KlassenratEintrag, Periode>(x => x.PeriodeId);
        b.Fk<KlassenratEintrag, Einschreibung>(x => x.EinschreibungId);
        b.KontoFk(x => x.ErfasstVonId);
    }
}

internal sealed class JahresentscheidungKonfiguration : IEntityTypeConfiguration<Jahresentscheidung>
{
    public void Configure(EntityTypeBuilder<Jahresentscheidung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Bemerkung).HasMaxLength(300);
        b.HasIndex(x => x.EinschreibungId).IsUnique();
        b.Fk<Jahresentscheidung, Einschreibung>(x => x.EinschreibungId);
        b.Fk<Jahresentscheidung, Stufe>(x => x.ZielStufeId);
    }
}

internal sealed class BulletinVorlageKonfiguration : IEntityTypeConfiguration<BulletinVorlage>
{
    public void Configure(EntityTypeBuilder<BulletinVorlage> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Farbe).HasMaxLength(7);
        b.Property(x => x.Fusstext).HasMaxLength(300);
        // Eine Vorlage je Stufe, genau eine Standardvorlage (StufeId null) je Schule.
        b.HasIndex(x => new { x.SchuleId, x.StufeId }).IsUnique().HasFilter(null);
        b.Fk<BulletinVorlage, Stufe>(x => x.StufeId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BulletinVorlagen_Schrift", "[SchriftGroesse] BETWEEN 8 AND 12");
            t.HasCheckConstraint("CK_BulletinVorlagen_Rand", "[SeitenRand] BETWEEN 15 AND 50");
        });
    }
}

internal sealed class KlassenVeroeffentlichungKonfiguration : IEntityTypeConfiguration<KlassenVeroeffentlichung>
{
    public void Configure(EntityTypeBuilder<KlassenVeroeffentlichung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.RectificationGrund).HasMaxLength(300);
        b.HasIndex(x => new { x.KlasseId, x.PeriodeId }).IsUnique();
        b.Fk<KlassenVeroeffentlichung, Klasse>(x => x.KlasseId);
        b.Fk<KlassenVeroeffentlichung, Periode>(x => x.PeriodeId);
        b.KontoFk(x => x.VeroeffentlichtVonId);
    }
}

internal sealed class BulletinKonfiguration : IEntityTypeConfiguration<Bulletin>
{
    public void Configure(EntityTypeBuilder<Bulletin> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Nummer).HasMaxLength(30);
        b.Property(x => x.PruefToken).HasMaxLength(64).IsFixedLength();
        b.Property(x => x.Hash).HasMaxLength(64);
        b.Property(x => x.Grund).HasMaxLength(300);
        b.HasIndex(x => new { x.SchuleId, x.Nummer }).IsUnique();
        b.HasIndex(x => x.PruefToken).IsUnique();
        // Höchstens ein gültiges Bulletin je Periode und Einschreibung.
        b.HasIndex(x => new { x.PeriodeId, x.EinschreibungId }).IsUnique()
            .HasFilter($"[Status] = {(int)BulletinStatus.Publie}").HasDatabaseName("IX_Bulletins_EinGueltiges");
        b.Fk<Bulletin, Periode>(x => x.PeriodeId);
        b.Fk<Bulletin, Einschreibung>(x => x.EinschreibungId);
        b.Fk<Bulletin, Bulletin>(x => x.ErsetztId);
        b.Fk<Bulletin, Medium>(x => x.LogoMediumId);
        b.Fk<Bulletin, Medium>(x => x.FotoMediumId);
    }
}

internal sealed class BulletinUebergabeKonfiguration : IEntityTypeConfiguration<BulletinUebergabe>
{
    public void Configure(EntityTypeBuilder<BulletinUebergabe> b)
    {
        b.ToTable("BulletinUebergaben");
        b.Schulgebunden();
        b.Property(x => x.Vermerk).HasMaxLength(300);
        b.HasIndex(x => x.BulletinId);
        b.Fk<BulletinUebergabe, Bulletin>(x => x.BulletinId);
        b.KontoFk(x => x.VonId);
    }
}
