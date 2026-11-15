using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Engine.Transport;

public sealed class TcpStreamSender : IDisposable
{
    private TcpListener? _listener;
    private NetworkStream? _clientStream;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private long _totalFramesSent;
    private long _totalBytesSent;

    public long TotalFramesSent => Interlocked.Read(ref _totalFramesSent);
    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
    public int ListenPort { get; private set; }
    public bool HasClient => _clientStream != null;

    public void Start(int port)
    {
        Stop();
        ListenPort = port;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        lock (_lock)
        {
            try { _clientStream?.Dispose(); } catch { }
            _clientStream = null;
        }
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                client.SendBufferSize = 4 * 1024 * 1024;
                lock (_lock)
                {
                    try { _clientStream?.Dispose(); } catch { }
                    _clientStream = client.GetStream();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> jpegBytes, CancellationToken ct = default)
    {
        NetworkStream? stream;
        lock (_lock) stream = _clientStream;
        if (stream == null) return;

        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), jpegBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)Environment.TickCount64);

        try
        {
            await stream.WriteAsync(header, ct);
            await stream.WriteAsync(jpegBytes, ct);
            await stream.FlushAsync(ct);
            Interlocked.Increment(ref _totalFramesSent);
            Interlocked.Add(ref _totalBytesSent, jpegBytes.Length + 8);
        }
        catch
        {
            lock (_lock)
            {
                try { _clientStream?.Dispose(); } catch { }
                _clientStream = null;
            }
        }
    }

    public void Dispose() => Stop();
}
