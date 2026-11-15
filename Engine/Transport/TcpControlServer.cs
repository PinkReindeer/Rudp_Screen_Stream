using System.Net;
using System.Net.Sockets;
using Protocol.Control;

namespace Engine.Transport;

public sealed class TcpControlServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private NetworkStream? _clientStream;
    private readonly object _streamLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event Action<IPEndPoint>? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action? StartStreamRequested;
    public event Action? StopStreamRequested;
    public event Action<int>? QualityChangeRequested;
    public event Action<byte>? SwitchModeRequested;

    public bool IsClientConnected { get; private set; }
    public int CurrentPort { get; private set; }

    public TcpControlServer(int port = ControlProtocol.DefaultTcpPort)
    {
        CurrentPort = port;
    }

    public void Start(int port)
    {
        Stop();
        CurrentPort = port;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
    }

    public void Start() => Start(CurrentPort);

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        DisconnectClient();

        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { continue; }

            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        using (client)
        {
            var stream = client.GetStream();
            lock (_streamLock) _clientStream = stream;

            var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
            if (remoteEp == null) return;

            bool handshakeSuccess = false;

            try
            {
                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshakeCts.CancelAfter(TimeSpan.FromSeconds(3));

                var firstMsg = await ControlProtocol.ReadMessageAsync(stream, handshakeCts.Token);
                if (firstMsg == null || firstMsg.Value.Type != (byte)ControlCommandType.Connect)
                {
                    await SendDataAsync(ControlProtocol.RespAckError("Handshake failed: First command must be Connect"), ct);
                    return;
                }

                ushort viewerUdpPort = ControlProtocol.ParseConnect(firstMsg.Value.Payload);
                var clientIp = remoteEp.Address.IsIPv4MappedToIPv6
                    ? remoteEp.Address.MapToIPv4()
                    : remoteEp.Address;

                var clientUdpEndpoint = new IPEndPoint(clientIp, viewerUdpPort);

                handshakeSuccess = true;
                IsClientConnected = true;
                await SendDataAsync(ControlProtocol.RespAckOk(), ct);
                ClientConnected?.Invoke(clientUdpEndpoint);

                while (!ct.IsCancellationRequested)
                {
                    var msg = await ControlProtocol.ReadMessageAsync(stream, ct);
                    if (msg is null) break;

                    await HandleCommandAsync(msg.Value.Type, msg.Value.Payload, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                lock (_streamLock) _clientStream = null;
                IsClientConnected = false;
                if (handshakeSuccess)
                {
                    ClientDisconnected?.Invoke();
                }
            }
        }
    }

    private async Task HandleCommandAsync(byte type, byte[] payload, CancellationToken ct)
    {
        switch ((ControlCommandType)type)
        {
            case ControlCommandType.StartStream:
                StartStreamRequested?.Invoke();
                await SendDataAsync(ControlProtocol.RespAckOk(), ct);
                break;

            case ControlCommandType.StopStream:
                StopStreamRequested?.Invoke();
                await SendDataAsync(ControlProtocol.RespAckOk(), ct);
                break;

            case ControlCommandType.SetQuality:
                if (payload.Length >= 1)
                {
                    int quality = Math.Clamp((int)payload[0], 1, 100);
                    QualityChangeRequested?.Invoke(quality);
                    await SendDataAsync(ControlProtocol.RespInfo($"Quality={quality}"), ct);
                }
                break;

            case ControlCommandType.SwitchMode:
                if (payload.Length >= 1)
                {
                    SwitchModeRequested?.Invoke(payload[0]);
                    await SendDataAsync(ControlProtocol.RespAckOk(), ct);
                }
                break;

            case ControlCommandType.Disconnect:
                StopStreamRequested?.Invoke();
                await SendDataAsync(ControlProtocol.RespAckOk(), ct);
                DisconnectClient();
                break;
        }
    }

    public async Task SendDataAsync(byte[] data, CancellationToken ct = default)
    {
        NetworkStream? stream;
        lock (_streamLock) stream = _clientStream;
        if (stream is null) return;

        try
        {
            await _sendLock.WaitAsync(ct);
            try
            {
                await stream.WriteAsync(data, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch { }
    }

    public Task SendStartStreamAsync(CancellationToken ct = default) =>
        SendDataAsync(ControlProtocol.CmdStart(), ct);

    public Task SendStopStreamAsync(CancellationToken ct = default) =>
        SendDataAsync(ControlProtocol.CmdStop(), ct);

    public Task SendSetQualityAsync(byte quality, CancellationToken ct = default) =>
        SendDataAsync(ControlProtocol.CmdQuality(quality), ct);

    public Task SendSwitchModeAsync(byte mode, CancellationToken ct = default) =>
        SendDataAsync(ControlProtocol.CmdSwitchMode(mode), ct);

    public void DisconnectClient()
    {
        lock (_streamLock)
        {
            if (_clientStream != null)
            {
                try { _clientStream.Dispose(); } catch { }
                _clientStream = null;
            }
        }
    }

    public void Dispose() => Stop();
}
