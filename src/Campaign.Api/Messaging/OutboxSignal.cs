using System.Threading.Channels;

namespace Campaign.Api.Messaging;

/// <summary>Đánh thức OutboxDispatcher ngay khi có job mới, thay vì chờ tới lượt quét định kỳ.</summary>
public sealed class OutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Chờ tới khi có tín hiệu hoặc hết thời gian chờ.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.WaitToReadAsync(cts.Token);
            _channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Hết thời gian chờ: quét định kỳ
        }
    }
}
