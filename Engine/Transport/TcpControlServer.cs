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
                    await SendResponseAsync(ControlProtocol.RespAckError("Handshake failed: First command must be Connect"), ct);
                    return;
                }

                ushort viewerUdpPort = ControlProtocol.ParseConnect(firstMsg.Value.Payload);
                var clientIp = remoteEp.Address.IsIPv4MappedToIPv6
                    ? remoteEp.Address.MapToIPv4()
                    : remoteEp.Address;

                // 2. Validate Target Host IP: Ensure viewer did not dial an invalid IP
                var validHostIps = ControlProtocol.GetLocalHostIps();
                if (!string.IsNullOrEmpty(targetHostIp) && !validHostIps.Contains(targetHostIp))
                {
                    await SendResponseAsync(ControlProtocol.RespAckError(
                        $"Host IP mismatch: Viewer dialed '{targetHostIp}', but this Host IP is {string.Join(" or ", validHostIps)}!"), ct);
                    return;
                }

                if (localEp != null)
                {
                    string localIpStr = localEp.Address.IsIPv4MappedToIPv6
                        ? localEp.Address.MapToIPv4().ToString()
                        : localEp.Address.ToString();

                    if (!validHostIps.Contains(localIpStr))
                    {
                        await SendResponseAsync(ControlProtocol.RespAckError(
                            $"Host IP mismatch: Connected to '{localIpStr}', which is not a valid Host address!"), ct);
                        return;
                    }
                }

                // 3. Validate Viewer IP and UDP Port against Host configuration
                if (ClientValidator != null)
                {
                    var (valid, errorMsg) = ClientValidator(remoteEp.Address, viewerUdpPort, targetHostIp);
                    if (!valid)
                    {
                        await SendResponseAsync(ControlProtocol.RespAckError(errorMsg), ct);
                        return;
                    }
                }

                // 4. UDP Probe check
                if (UdpProbeSender != null)
                {
                    var clientIp = remoteEp.Address.IsIPv4MappedToIPv6 ? remoteEp.Address.MapToIPv4() : remoteEp.Address;
                    var viewerUdpEp = new IPEndPoint(clientIp, viewerUdpPort);
                    await UdpProbeSender(viewerUdpEp);

                    using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    probeCts.CancelAfter(TimeSpan.FromSeconds(2));

                    var probeResp = await ControlProtocol.ReadMessageAsync(stream, probeCts.Token);
                    if (probeResp == null || probeResp.Value.Type != (byte)ControlCommandType.UdpProbeAck)
                    {
                        await SendResponseAsync(ControlProtocol.RespAckError(
                            $"UDP check failed: UDP probe packet not received on port {viewerUdpPort}"), ct);
                        return;
                    }
                }

                handshakeSuccess = true;
                IsClientConnected = true;
                await SendResponseAsync(ControlProtocol.RespAckOk(), ct);

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
                await SendResponseAsync(ControlProtocol.RespAckOk(), ct);
                break;

            case ControlCommandType.StopStream:
                StopStreamRequested?.Invoke();
                await SendResponseAsync(ControlProtocol.RespAckOk(), ct);
                break;

            case ControlCommandType.SetQuality:
                if (payload.Length >= 1)
                {
                    int quality = Math.Clamp((int)payload[0], 1, 100);
                    QualityChangeRequested?.Invoke(quality);
                    await SendResponseAsync(ControlProtocol.RespInfo($"Quality={quality}"), ct);
                }
                break;

            case ControlCommandType.SwitchMode:
                if (payload.Length >= 1)
                {
                    SwitchModeRequested?.Invoke(payload[0]);
                    await SendResponseAsync(ControlProtocol.RespAckOk(), ct);
                }
                break;

            case ControlCommandType.Disconnect:
                StopStreamRequested?.Invoke();
                await SendResponseAsync(ControlProtocol.RespAckOk(), ct);
                DisconnectClient();
                break;
        }
    }

    private async Task SendResponseAsync(byte[] data, CancellationToken ct)
    {
        NetworkStream? stream;
        lock (_streamLock) stream = _clientStream;
        if (stream is null) return;

        try { await stream.WriteAsync(data, ct); }
        catch { }
    }

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
