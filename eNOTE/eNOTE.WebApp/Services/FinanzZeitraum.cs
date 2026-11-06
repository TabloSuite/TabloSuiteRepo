namespace ENote.WebApp.Services;

internal sealed record FinanzZeitraum(DateTime VonUtc, DateTime BisExklusivUtc)
{
    public DateTime Bis => BisExklusivUtc.AddDays(-1);

    public static bool TryErstellen(DateTime? von, DateTime? bis, out FinanzZeitraum? zeitraum, out string? fehler)
    {
        zeitraum = null;
        fehler = null;
        if (von is null || bis is null)
        {
            fehler = "Veuillez sélectionner les deux dates.";
            return false;
        }

        if (von.Value.Date > bis.Value.Date)
        {
            fehler = "La date de début doit être antérieure ou égale à la date de fin.";
            return false;
        }

        if (bis.Value.Date == DateTime.MaxValue.Date)
        {
            fehler = "La date de fin est trop éloignée.";
            return false;
        }

        // Les journées de l'application au Togo correspondent aux journées UTC.
        zeitraum = new FinanzZeitraum(
            DateTime.SpecifyKind(von.Value.Date, DateTimeKind.Utc),
            DateTime.SpecifyKind(bis.Value.Date.AddDays(1), DateTimeKind.Utc));
        return true;
    }
}
