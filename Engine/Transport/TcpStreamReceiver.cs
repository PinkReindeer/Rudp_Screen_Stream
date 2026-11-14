using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Engine.Transport;

public sealed class TcpStreamReceiver : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly Channel<(byte[] Data, int Length, uint TimestampMs)> _frameChannel;
    private long _totalFramesReceived;
    private long _totalBytesReceived;

    public ChannelReader<(byte[] Data, int Length, uint TimestampMs)> CompletedFrames => _frameChannel.Reader;
    public long TotalFramesReceived => Interlocked.Read(ref _totalFramesReceived);
    public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);
    public bool IsConnected { get; private set; }

    public TcpStreamReceiver()
    {
        _frameChannel = Channel.CreateBounded<(byte[] Data, int Length, uint TimestampMs)>(
            new BoundedChannelOptions(4)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        Disconnect();
        var tcp = new TcpClient { NoDelay = true, ReceiveBufferSize = 4 * 1024 * 1024 };
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync(host, port, connectCts.Token);

        _tcp = tcp;
        _stream = tcp.GetStream();
        IsConnected = true;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoopAsync(_stream, _cts.Token));
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        var headerBuf = new byte[8];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactAsync(stream, headerBuf, 8, ct)) break;

                int frameLen = BinaryPrimitives.ReadInt32LittleEndian(headerBuf.AsSpan(0, 4));
                uint timestamp = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(4, 4));

                if (frameLen <= 0 || frameLen > 10 * 1024 * 1024) break;

                var frameData = new byte[frameLen];
                if (!await ReadExactAsync(stream, frameData, frameLen, ct)) break;

                Interlocked.Increment(ref _totalFramesReceived);
                Interlocked.Add(ref _totalBytesReceived, frameLen + 8);

                _frameChannel.Writer.TryWrite((frameData, frameLen, timestamp));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch { }
        finally
        {
            IsConnected = false;
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (n == 0) return false;
            offset += n;
        }
        return true;
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null;
        _tcp = null;
        IsConnected = false;
    }

    public void Dispose() => Disconnect();
}
