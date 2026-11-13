using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data.Konfiguration;

// Bereich Organisation: Schuljahr, Perioden, Stufen, Klassen, Fächer, Lehraufträge, Schüler, Stundenpläne.

internal sealed class SchuljahrKonfiguration : IEntityTypeConfiguration<Schuljahr>
{
    public void Configure(EntityTypeBuilder<Schuljahr> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Bezeichnung).HasMaxLength(80);
        b.Property(x => x.WiedereroeffnetGrund).HasMaxLength(500);
        b.HasIndex(x => new { x.SchuleId, x.Bezeichnung }).IsUnique();
        // Höchstens ein laufendes Jahr je Schule.
        b.HasIndex(x => x.SchuleId).IsUnique().HasFilter($"[Status] = {(int)SchuljahrStatus.Laufend}")
            .HasDatabaseName("IX_Schuljahre_EinLaufendesJahr");
        b.HasMany(x => x.Perioden).WithOne().HasForeignKey(p => p.SchuljahrId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_Schuljahre_Zeitraum", "[Ende] > [Beginn]"));
    }
}

internal sealed class PeriodeKonfiguration : IEntityTypeConfiguration<Periode>
{
    public void Configure(EntityTypeBuilder<Periode> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Titel).HasMaxLength(80);
        b.Property(x => x.Jahresgewicht).HasPrecision(5, 2);
        b.HasIndex(x => new { x.SchuljahrId, x.Nr }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_Perioden_Zeitraum", "[Ende] >= [Beginn]"));
    }
}

internal sealed class StufeKonfiguration : IEntityTypeConfiguration<Stufe>
{
    public void Configure(EntityTypeBuilder<Stufe> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasIndex(x => new { x.SchuleId, x.Name }).IsUnique();
        b.Fk<Stufe, Stufe>(x => x.NachfolgerId);
    }
}

internal sealed class KlasseKonfiguration : IEntityTypeConfiguration<Klasse>
{
    public void Configure(EntityTypeBuilder<Klasse> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Name).HasMaxLength(60);
        b.Property(x => x.Raum).HasMaxLength(80);
        b.HasIndex(x => new { x.SchuljahrId, x.Name }).IsUnique();
        b.Fk<Klasse, Schuljahr>(x => x.SchuljahrId);
        b.Fk<Klasse, Stufe>(x => x.StufeId);
        b.Fk<Klasse, Periode>(x => x.PeriodeId);
        b.KontoFk(x => x.KlassenlehrerId);
    }
}

internal sealed class FachKonfiguration : IEntityTypeConfiguration<Fach>
{
    public void Configure(EntityTypeBuilder<Fach> b)
    {
        b.ToTable("Faecher");
        b.Schulgebunden();
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Kuerzel).HasMaxLength(12);
        b.HasIndex(x => new { x.SchuleId, x.Name }).IsUnique();
    }
}

internal sealed class KlassenfachKonfiguration : IEntityTypeConfiguration<Klassenfach>
{
    public void Configure(EntityTypeBuilder<Klassenfach> b)
    {
        b.ToTable("Klassenfaecher");
        b.Schulgebunden();
        b.Property(x => x.Koeffizient).HasPrecision(5, 2);
        b.HasIndex(x => new { x.KlasseId, x.FachId }).IsUnique();
        b.Fk<Klassenfach, Klasse>(x => x.KlasseId);
        b.Fk<Klassenfach, Fach>(x => x.FachId);
        b.ToTable(t => t.HasCheckConstraint("CK_Klassenfaecher_Koeffizient", "[Koeffizient] > 0"));
    }
}

internal sealed class LehrauftragKonfiguration : IEntityTypeConfiguration<Lehrauftrag>
{
    public void Configure(EntityTypeBuilder<Lehrauftrag> b)
    {
        b.ToTable("Lehrauftraege");
        b.Schulgebunden();
        b.Property(x => x.Wochenstunden).HasPrecision(4, 1);
        b.HasIndex(x => new { x.KlassenfachId, x.LehrerId }).IsUnique();
        b.HasIndex(x => x.LehrerId);
        b.Fk<Lehrauftrag, Klassenfach>(x => x.KlassenfachId);
        b.KontoFk(x => x.LehrerId);
    }
}

internal sealed class SchuelerProfilKonfiguration : IEntityTypeConfiguration<SchuelerProfil>
{
    public void Configure(EntityTypeBuilder<SchuelerProfil> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Matrikel).HasMaxLength(60);
        b.Property(x => x.Geburtsort).HasMaxLength(150);
        b.Property(x => x.Nationalitaet).HasMaxLength(80);
        b.Property(x => x.ElternTelefon).HasMaxLength(50);
        b.Property(x => x.ElternEmail).HasMaxLength(256);
        b.HasIndex(x => x.KontoId).IsUnique();
        b.HasIndex(x => new { x.SchuleId, x.Matrikel }).IsUnique().HasFilter("[Matrikel] IS NOT NULL");
        b.KontoFk(x => x.KontoId);
    }
}

internal sealed class EinschreibungKonfiguration : IEntityTypeConfiguration<Einschreibung>
{
    public void Configure(EntityTypeBuilder<Einschreibung> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Austrittsgrund).HasMaxLength(300);
        b.Property(x => x.StornoGrund).HasMaxLength(300);
        // Höchstens eine aktive Einschreibung je Schüler.
        b.HasIndex(x => x.SchuelerId).IsUnique().HasFilter($"[Status] = {(int)EinschreibungsStatus.Active}")
            .HasDatabaseName("IX_Einschreibungen_EineAktive");
        b.HasIndex(x => new { x.KlasseId, x.Status });
        b.HasIndex(x => new { x.SchuljahrId, x.SchuelerId });
        b.KontoFk(x => x.SchuelerId);
        b.Fk<Einschreibung, Schuljahr>(x => x.SchuljahrId);
        b.Fk<Einschreibung, Klasse>(x => x.KlasseId);
    }
}

internal sealed class ElternKindKonfiguration : IEntityTypeConfiguration<ElternKind>
{
    public void Configure(EntityTypeBuilder<ElternKind> b)
    {
        b.ToTable("ElternKinder");
        b.Schulgebunden();
        b.Property(x => x.Beziehung).HasMaxLength(40);
        b.HasIndex(x => new { x.ElternId, x.SchuelerId }).IsUnique();
        b.HasIndex(x => x.SchuelerId);
        b.KontoFk(x => x.ElternId);
        b.KontoFk(x => x.SchuelerId);
    }
}

internal sealed class StundenplanKonfiguration : IEntityTypeConfiguration<Stundenplan>
{
    public void Configure(EntityTypeBuilder<Stundenplan> b)
    {
        b.ToTable("Stundenplaene");
        b.Schulgebunden();
        b.HasIndex(x => new { x.KlasseId, x.GueltigAb });
        b.Fk<Stundenplan, Klasse>(x => x.KlasseId);
        b.HasMany(x => x.Eintraege).WithOne().HasForeignKey(e => e.StundenplanId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StundenplanEintragKonfiguration : IEntityTypeConfiguration<StundenplanEintrag>
{
    public void Configure(EntityTypeBuilder<StundenplanEintrag> b)
    {
        b.ToTable("StundenplanEintraege");
        b.Property(x => x.Raum).HasMaxLength(80);
        b.HasIndex(x => new { x.LehrauftragId, x.Wochentag });
        b.Fk<StundenplanEintrag, Lehrauftrag>(x => x.LehrauftragId);
        b.ToTable(t => t.HasCheckConstraint("CK_StundenplanEintraege_Zeit", "[Ende] > [Beginn]"));
    }
}

internal sealed class AnwesenheitKonfiguration : IEntityTypeConfiguration<Anwesenheit>
{
    public void Configure(EntityTypeBuilder<Anwesenheit> b)
    {
        b.Schulgebunden();
        b.Property(x => x.Bemerkung).HasMaxLength(300);
        b.HasIndex(x => new { x.EinschreibungId, x.Datum }).IsUnique();
        b.HasIndex(x => new { x.SchuleId, x.Datum });
        b.Fk<Anwesenheit, Einschreibung>(x => x.EinschreibungId);
        b.KontoFk(x => x.ErfasstVonId);
    }
}
