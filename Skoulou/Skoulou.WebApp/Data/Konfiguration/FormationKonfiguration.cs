using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data.Konfiguration;

// Formation der Responsables: Stand je Konto (Konten werden nie gelöscht → Restrict); Versuche gehören zur Runde (Cascade).

internal sealed class FormationTeilnehmerKonfiguration : IEntityTypeConfiguration<FormationTeilnehmer>
{
    public void Configure(EntityTypeBuilder<FormationTeilnehmer> b)
    {
        b.ToTable("FormationTeilnehmer");
        b.HasIndex(x => x.KontoId).IsUnique();
        b.KontoFk(x => x.KontoId);
    }
}

internal sealed class FormationLektionGelesenKonfiguration : IEntityTypeConfiguration<FormationLektionGelesen>
{
    public void Configure(EntityTypeBuilder<FormationLektionGelesen> b)
    {
        b.ToTable("FormationLektionenGelesen");
        b.Property(x => x.LektionId).HasMaxLength(60);
        b.HasIndex(x => new { x.KontoId, x.LektionId }).IsUnique();
        b.KontoFk(x => x.KontoId);
    }
}

internal sealed class FormationRundeKonfiguration : IEntityTypeConfiguration<FormationRunde>
{
    public void Configure(EntityTypeBuilder<FormationRunde> b)
    {
        b.ToTable("FormationRunden");
        b.Property(x => x.ErlassenGrund).HasMaxLength(500);
        b.HasIndex(x => new { x.KontoId, x.Status });
        b.KontoFk(x => x.KontoId);
    }
}

internal sealed class FormationVersuchKonfiguration : IEntityTypeConfiguration<FormationVersuch>
{
    public void Configure(EntityTypeBuilder<FormationVersuch> b)
    {
        b.ToTable("FormationVersuche");
        b.HasIndex(x => new { x.RundeId, x.Nummer }).IsUnique();
        b.HasOne(x => x.Runde).WithMany(r => r.Versuche).HasForeignKey(x => x.RundeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FormationRegelKonfiguration : IEntityTypeConfiguration<FormationRegel>
{
    public void Configure(EntityTypeBuilder<FormationRegel> b) => b.ToTable("FormationRegeln");
}
