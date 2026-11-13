using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data.Konfiguration;

// Bereich Pädagogik: Leçons, Examens, Fragen, Zuweisungen, Versuche (Versuchsdaten nie kaskadierend löschen).

internal sealed class LeconKonfiguration : IEntityTypeConfiguration<Lecon>
{
    public void Configure(EntityTypeBuilder<Lecon> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Titel).HasMaxLength(200);
        b.Property(x => x.Zusammenfassung).HasMaxLength(1000);
        b.Property(x => x.Thema).HasMaxLength(200);
        b.HasIndex(x => new { x.SchuleId, x.StufeId, x.FachId });
        b.HasIndex(x => x.AutorId);
        b.KontoFk(x => x.AutorId);
        b.KontoFk(x => x.ErfasstVonId);
        b.Fk<Lecon, Fach>(x => x.FachId);
        b.Fk<Lecon, Stufe>(x => x.StufeId);
        b.Fk<Lecon, Medium>(x => x.VideoMediumId);
        b.HasMany(x => x.Seiten).WithOne().HasForeignKey(s => s.LeconId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LeconSeiteKonfiguration : IEntityTypeConfiguration<LeconSeite>
{
    public void Configure(EntityTypeBuilder<LeconSeite> b)
    {
        b.ToTable("LeconSeiten");
        b.HasIndex(x => new { x.LeconId, x.Nr }).IsUnique();
    }
}

internal sealed class PruefungKonfiguration : IEntityTypeConfiguration<Pruefung>
{
    public void Configure(EntityTypeBuilder<Pruefung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Titel).HasMaxLength(200);
        b.Property(x => x.Beschreibung).HasMaxLength(2000);
        b.Property(x => x.Bestehensgrenze).HasPrecision(5, 2);
        b.HasIndex(x => new { x.SchuleId, x.StufeId, x.FachId });
        b.KontoFk(x => x.AutorId);
        b.KontoFk(x => x.ErfasstVonId);
        b.Fk<Pruefung, Fach>(x => x.FachId);
        b.Fk<Pruefung, Stufe>(x => x.StufeId);
        b.ToTable(t => t.HasCheckConstraint("CK_Pruefungen_Dauer", "[DauerMinuten] > 0"));
    }
}

internal sealed class AufgabenBlockKonfiguration : IEntityTypeConfiguration<AufgabenBlock>
{
    public void Configure(EntityTypeBuilder<AufgabenBlock> b)
    {
        b.ToTable("AufgabenBloecke");
        b.Schulgebunden();
        b.Property(x => x.Titel).HasMaxLength(200);
        b.Property(x => x.Consigne).HasMaxLength(2000);
        b.HasIndex(x => new { x.LeconId, x.Nr }).IsUnique().HasFilter("[LeconId] IS NOT NULL");
        b.HasIndex(x => new { x.PruefungId, x.Nr }).IsUnique().HasFilter("[PruefungId] IS NOT NULL");
        b.Fk<AufgabenBlock, Lecon>(x => x.LeconId);
        b.Fk<AufgabenBlock, Pruefung>(x => x.PruefungId);
        b.ToTable(t => t.HasCheckConstraint("CK_AufgabenBloecke_Inhalt",
            "([LeconId] IS NOT NULL AND [PruefungId] IS NULL) OR ([LeconId] IS NULL AND [PruefungId] IS NOT NULL)"));
    }
}

internal sealed class FrageKonfiguration : IEntityTypeConfiguration<Frage>
{
    public void Configure(EntityTypeBuilder<Frage> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Punkte).HasPrecision(6, 2);
        b.Property(x => x.AudioText).HasMaxLength(2000);
        b.Property(x => x.Sprache).HasMaxLength(10);
        // Nur die aktuelle Revision belegt eine Nummer im Block.
        b.HasIndex(x => new { x.BlockId, x.Nr }).IsUnique().HasFilter("[Archiviert] = 0");
        b.Fk<Frage, AufgabenBlock>(x => x.BlockId);
        b.Fk<Frage, Frage>(x => x.VorgaengerId);
        b.HasMany(x => x.Optionen).WithOne().HasForeignKey(o => o.FrageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FrageOptionKonfiguration : IEntityTypeConfiguration<FrageOption>
{
    public void Configure(EntityTypeBuilder<FrageOption> b)
    {
        b.ToTable("FrageOptionen");
        b.Property(x => x.Text).HasMaxLength(1000);
        b.HasIndex(x => new { x.FrageId, x.Nr }).IsUnique();
    }
}

internal sealed class ZuweisungKonfiguration : IEntityTypeConfiguration<Zuweisung>
{
    public void Configure(EntityTypeBuilder<Zuweisung> b)
    {
        b.Schulgebunden();
        b.HasIndex(x => new { x.KlasseId, x.Status });
        b.HasIndex(x => x.LehrerId);
        b.Fk<Zuweisung, Lecon>(x => x.LeconId);
        b.Fk<Zuweisung, Pruefung>(x => x.PruefungId);
        b.Fk<Zuweisung, Klasse>(x => x.KlasseId);
        b.KontoFk(x => x.LehrerId);
        b.ToTable(t => t.HasCheckConstraint("CK_Zuweisungen_Inhalt",
            "([LeconId] IS NOT NULL AND [PruefungId] IS NULL) OR ([LeconId] IS NULL AND [PruefungId] IS NOT NULL)"));
    }
}

internal sealed class VersuchKonfiguration : IEntityTypeConfiguration<Versuch>
{
    public void Configure(EntityTypeBuilder<Versuch> b)
    {
        b.ToTable("Versuche");
        b.Schulgebunden();
        b.Property(x => x.Punkte).HasPrecision(8, 2);
        b.Property(x => x.MaxPunkte).HasPrecision(8, 2);
        b.HasIndex(x => new { x.ZuweisungId, x.SchuelerId, x.Nr }).IsUnique();
        b.HasIndex(x => new { x.Status, x.DeadlineUtc });
        b.Fk<Versuch, Zuweisung>(x => x.ZuweisungId);
        b.KontoFk(x => x.SchuelerId);
    }
}

internal sealed class AntwortKonfiguration : IEntityTypeConfiguration<Antwort>
{
    public void Configure(EntityTypeBuilder<Antwort> b)
    {
        b.Property(x => x.Punkte).HasPrecision(6, 2);
        b.Property(x => x.Feedback).HasMaxLength(2000);
        b.HasIndex(x => new { x.VersuchId, x.FrageId }).IsUnique();
        b.Fk<Antwort, Versuch>(x => x.VersuchId);
        b.Fk<Antwort, Frage>(x => x.FrageId);
        b.KontoFk(x => x.KorrigiertVonId);
    }
}

internal sealed class LernStandKonfiguration : IEntityTypeConfiguration<LernStand>
{
    public void Configure(EntityTypeBuilder<LernStand> b)
    {
        b.ToTable("LernStaende");
        b.Schulgebunden();
        b.Property(x => x.BestePunkte).HasPrecision(8, 2);
        b.Property(x => x.Prozent).HasPrecision(5, 2);
        b.HasIndex(x => new { x.ZuweisungId, x.SchuelerId }).IsUnique();
        b.HasIndex(x => x.SchuelerId);
        b.Fk<LernStand, Zuweisung>(x => x.ZuweisungId);
        b.KontoFk(x => x.SchuelerId);
    }
}

internal sealed class FormelFavoritKonfiguration : IEntityTypeConfiguration<FormelFavorit>
{
    public void Configure(EntityTypeBuilder<FormelFavorit> b)
    {
        b.ToTable("FormelFavoriten");
        b.Property(x => x.LaTeX).HasMaxLength(2000);
        b.Property(x => x.Bezeichnung).HasMaxLength(120);
        b.HasIndex(x => x.KontoId);
        b.KontoFk(x => x.KontoId);
    }
}
