using System.Net.Sockets;
using Protocol.Control;

namespace Engine.Transport;

public sealed class TcpControlClient : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? StatusReceived;

    public bool IsConnected { get; private set; }

    // ── Connect ───────────────────────────────────────────────────────────────

    public async Task ConnectAsync(string host, int tcpPort, ushort myUdpPort,
                                   CancellationToken ct = default)
    {
        Disconnect();

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(3)); // 3-second connection timeout

        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, tcpPort, connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            tcp.Dispose();
            throw new TimeoutException($"Connection timed out connecting to Host at {host}:{tcpPort}");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        var stream = tcp.GetStream();
        lock (_lock)
        {
            _tcp = tcp;
            _stream = stream;
        }

        // Send Connect command containing our UDP receiving port and target Host IP
        await stream.WriteAsync(ControlProtocol.CmdConnect(myUdpPort, host), connectCts.Token);

        // Wait for server Ack (with 3-second timeout)
        var resp = await ControlProtocol.ReadMessageAsync(stream, connectCts.Token);
        if (resp == null)
        {
            Disconnect();
            throw new InvalidOperationException("Connection closed by server during handshake.");
        }

        if (resp.Value.Type != (byte)ControlResponseType.Ack)
        {
            Disconnect();
            throw new InvalidOperationException($"Unexpected response from server: 0x{resp.Value.Type:X2}");
        }

        var (ok, message) = ControlProtocol.ParseAck(resp.Value.Payload);
        if (!ok)
        {
            Disconnect();
            throw new InvalidOperationException(message);
        }

        // Handshake succeeded!
        lock (_lock)
        {
            IsConnected = true;
        }

        Connected?.Invoke();

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public Task SendUdpProbeAckAsync(CancellationToken ct = default) =>
        SendAsync(ControlProtocol.CmdUdpProbeAck(), ct);

    public Task SendStartStreamAsync(CancellationToken ct = default) =>
        SendAsync(ControlProtocol.CmdStart(), ct);

    public Task SendStopStreamAsync(CancellationToken ct = default) =>
        SendAsync(ControlProtocol.CmdStop(), ct);

    public Task SendSetQualityAsync(byte quality, CancellationToken ct = default) =>
        SendAsync(ControlProtocol.CmdQuality(quality), ct);

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return;

        try
        {
            await SendAsync(ControlProtocol.CmdDisconnect(), ct);
        }
        catch { }

        Disconnect();
    }

    public void Disconnect()
    {
        if (!IsConnected && _tcp == null && _stream == null) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        lock (_lock)
        {
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Dispose(); } catch { }
            _stream = null;
            _tcp = null;
        }

        HandleDisconnect();
    }

    private async Task SendAsync(byte[] data, CancellationToken ct)
    {
        NetworkStream? stream;
        lock (_lock) stream = _stream;
        if (stream is null) return;

        try { await stream.WriteAsync(data, ct); }
        catch { HandleDisconnect(); }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var msg = await ControlProtocol.ReadMessageAsync(_stream, ct);
                if (msg is null) break;

                string text = msg.Value.Type switch
                {
                    (byte)ControlResponseType.Ack =>
                        ControlProtocol.ParseAck(msg.Value.Payload).Message,
                    (byte)ControlResponseType.Info =>
                        System.Text.Encoding.UTF8.GetString(msg.Value.Payload),
                    _ => $"Unknown response 0x{msg.Value.Type:X2}"
                };

                StatusReceived?.Invoke(text);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            HandleDisconnect();
        }
    }

    private void HandleDisconnect()
    {
        if (!IsConnected) return;
        IsConnected = false;
        Disconnected?.Invoke();
    }

    public void Dispose()
    {
        Disconnect();
    }
}
