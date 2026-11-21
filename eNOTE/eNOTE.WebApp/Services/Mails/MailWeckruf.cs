using System.Threading.Channels;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Weckruf für den <see cref="MailVersandDienst"/>: <see cref="EmailService.SendeAsync(string,string,string,string?,CancellationToken)"/>
/// legt eine Mail nur in die Warteschlange (<c>MailNachrichten</c>) und weckt den Dienst, der sie sofort im Hintergrund
/// versendet. So wartet kein Klick mehr auf Postmark/SMTP. Mehrere Weckrufe hintereinander fallen zu einem zusammen.
/// </summary>
public sealed class MailWeckruf
{
    private readonly Channel<bool> kanal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Neue Mail liegt in der Warteschlange.</summary>
    public void Wecken() => kanal.Writer.TryWrite(true);

    /// <summary>Wartet auf einen Weckruf, höchstens <paramref name="hoechstens"/>. true = geweckt, false = Zeit abgelaufen.</summary>
    public async Task<bool> WarteAsync(TimeSpan hoechstens, CancellationToken ct)
    {
        if (kanal.Reader.TryRead(out _)) return true;
        using var zeit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        zeit.CancelAfter(hoechstens);
        try
        {
            await kanal.Reader.WaitToReadAsync(zeit.Token);
            return kanal.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
