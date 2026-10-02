using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Shared;

public partial class Toast : IDisposable
{
    [Parameter]
    public string? Message { get; set; }

    [Parameter]
    public EventCallback<string?> MessageChanged { get; set; }

    [Parameter]
    public ToastKind Kind { get; set; } = ToastKind.Success;

    [Parameter]
    public int DurationMs { get; set; } = 10500;

    private string? currentMessage;
    private bool visible;
    private bool leaving;
    private System.Timers.Timer? timer;
    private bool isDisposed;

    public enum ToastKind
    {
        Success,
        Error,
        Info
    }

    protected override void OnParametersSet()
    {
        if (isDisposed)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(Message) && Message != currentMessage)
        {
            Show(Message);
        }
    }

    private void Show(string message)
    {
        if (isDisposed)
        {
            return;
        }

        currentMessage = message;
        visible = true;
        leaving = false;

        timer?.Stop();
        if (timer is not null)
        {
            timer.Elapsed -= OnTimerElapsed;
        }
        timer?.Dispose();
        timer = new System.Timers.Timer(DurationMs) { AutoReset = false };
        timer.Elapsed += OnTimerElapsed;
        timer.Start();
    }

    private async void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(async () =>
            {
                if (isDisposed)
                {
                    return;
                }

                await BeginLeaveAsync();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task BeginLeaveAsync()
    {
        if (isDisposed)
        {
            return;
        }

        leaving = true;
        StateHasChanged();
        await Task.Delay(300);
        await DismissAsync();
    }

    private async Task DismissAsync()
    {
        if (isDisposed)
        {
            return;
        }

        timer?.Stop();
        visible = false;
        leaving = false;
        currentMessage = null;

        if (MessageChanged.HasDelegate)
        {
            await MessageChanged.InvokeAsync(null);
        }

        StateHasChanged();
    }

    private string AccentClass => Kind switch
    {
        ToastKind.Error => "bg-rose-600 text-white",
        ToastKind.Info => "bg-slate-900 text-white",
        _ => "bg-emerald-600 text-white"
    };

    private string IconPath => Kind switch
    {
        ToastKind.Error => "M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z",
        ToastKind.Info => "m11.25 11.25.041-.02a.75.75 0 0 1 1.063.852l-.708 2.836a.75.75 0 0 0 1.063.853l.041-.021M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9-3.75h.008v.008H12V8.25Z",
        _ => "m4.5 12.75 6 6 9-13.5"
    };

    public void Dispose()
    {
        isDisposed = true;
        timer?.Stop();
        if (timer is not null)
        {
            timer.Elapsed -= OnTimerElapsed;
        }
        timer?.Dispose();
        timer = null;
    }
}
