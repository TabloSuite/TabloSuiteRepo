namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Serialisiert Datenbankaktionen innerhalb eines Blazor-Circuits und verhindert so schnelle Mehrfachauslösungen.
/// </summary>
public sealed class DatabaseActionGuard
{
    private int isBusy;

    public bool IsBusy => Volatile.Read(ref isBusy) == 1;

    public async Task<bool> RunAsync(Func<Task> action)
    {
        if (Interlocked.CompareExchange(ref isBusy, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            await action();
            return true;
        }
        finally
        {
            Volatile.Write(ref isBusy, 0);
        }
    }

    public async Task<T?> RunAsync<T>(Func<Task<T>> action)
    {
        if (Interlocked.CompareExchange(ref isBusy, 1, 0) != 0)
        {
            return default;
        }

        try
        {
            return await action();
        }
        finally
        {
            Volatile.Write(ref isBusy, 0);
        }
    }
}
