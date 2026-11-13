using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Data.Konfiguration;

/// <summary>Kurzformen für Fremdschlüssel (immer Restrict) und Schulbindung.</summary>
internal static class KonfigurationsHilfen
{
    /// <summary>Fremdschlüssel ohne Navigation auf <typeparamref name="TZiel"/>, Löschen verboten.</summary>
    public static EntityTypeBuilder<T> Fk<T, TZiel>(this EntityTypeBuilder<T> b, Expression<Func<T, object?>> fk)
        where T : class where TZiel : class
    {
        b.HasOne<TZiel>().WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);
        return b;
    }

    /// <summary>Schulgebundene Entität: Fremdschlüssel auf Schule (Restrict).</summary>
    public static EntityTypeBuilder<T> Schulgebunden<T>(this EntityTypeBuilder<T> b) where T : class, ISchulgebunden
    {
        b.HasOne<Schule>().WithMany().HasForeignKey(x => x.SchuleId).OnDelete(DeleteBehavior.Restrict);
        return b;
    }

    /// <summary>Konto-Fremdschlüssel (Restrict).</summary>
    public static EntityTypeBuilder<T> KontoFk<T>(this EntityTypeBuilder<T> b, Expression<Func<T, object?>> fk) where T : class
        => b.Fk<T, Konto>(fk);
}
