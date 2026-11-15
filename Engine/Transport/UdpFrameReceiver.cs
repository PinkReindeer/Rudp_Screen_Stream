using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Engine.Telemetry;
using Protocol.Chaos;
using Protocol.Packets;
using Protocol.Rudp;

namespace Engine.Transport;

public sealed class UdpFrameReceiver : IDisposable
{
    private Socket? _socket;
    private readonly FrameReassembler _reassembler = new();
    private readonly Channel<CompletedFrame> _frameChannel;
    private CancellationTokenSource? _cts;
    private readonly Lock _socketLock = new();
    private bool _isDisposed;

    public event Action<IPEndPoint>? ProbeReceived;

    private EndPoint? _lastSenderEndpoint;

    public ChannelReader<CompletedFrame> CompletedFrames => _frameChannel.Reader;
    public FrameReassembler Reassembler => _reassembler;
    public ChaosSimulator Chaos { get; } = new();
    public NackTracker NackTracker { get; } = new();
    public NetworkMetrics Metrics { get; } = new();

    public bool EnableRingBuffer
    {
        get => _reassembler.EnableRingBuffer;
        set => _reassembler.EnableRingBuffer = value;
    }

    public bool EnableNack
    {
        get => NackTracker.IsEnabled;
        set
        {
            NackTracker.IsEnabled = value;
            if (value)
            {
                _reassembler.EnableRingBuffer = true;
            }
        }
    }

    public int CurrentPort { get; private set; }

    public UdpFrameReceiver(int listenPort)
    {
        CurrentPort = listenPort;

        _frameChannel = Channel.CreateBounded<CompletedFrame>(new BoundedChannelOptions(4)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        }, droppedFrame => droppedFrame.Dispose());

        NackTracker.NackRequested += OnNackRequested;

        BindAndStart(listenPort);
    }

    public void Rebind(int newPort)
    {
        lock (_socketLock)
        {
            if (_isDisposed) return;
            if (newPort == CurrentPort && _socket != null) return;
            CurrentPort = newPort;
            StopReceiving();
            BindAndStart(newPort);
        }
    }

    private void BindAndStart(int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 8 * 1024 * 1024,
            SendBufferSize = 1 * 1024 * 1024
        };
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));

        _cts = new CancellationTokenSource();
        var socket = _socket;
        var token = _cts.Token;

        _ = Task.Run(() => ReceiveLoopAsync(socket, token), token);
        _ = Task.Run(() => NackTimeoutLoopAsync(token), token);
    }

    private void StopReceiving()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _socket?.Dispose();
        _socket = null;
    }

    private async Task ReceiveLoopAsync(Socket socket, CancellationToken ct)
    {
        byte[] recvBuffer = ArrayPool<byte>.Shared.Rent(RudpHeader.MaxPacketSize);
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try
                {
                    result = await socket.ReceiveFromAsync(recvBuffer.AsMemory(), SocketFlags.None, remoteEp, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue;
                }
                catch { continue; }

                _lastSenderEndpoint = result.RemoteEndPoint;

                var packetSpan = recvBuffer.AsSpan(0, result.ReceivedBytes);
                if (packetSpan.Length < RudpHeader.Size) continue;

                var header = RudpHeader.ReadFrom(packetSpan);

                if ((header.Flags & PacketFlags.Probe) != 0)
                {
                    if (result.RemoteEndPoint is IPEndPoint rep)
                    {
                        ProbeReceived?.Invoke(rep);
                    }
                    continue;
                }

                if (Chaos.Intercept(packetSpan))
                {
                    Metrics.RecordPacketDropped();
                    continue;
                }

                Metrics.RecordPacket(result.ReceivedBytes);

                if (EnableNack && (header.Flags & PacketFlags.Data) != 0)
                {
                    NackTracker.OnFragmentReceived(header.FrameID, header.FragmentID, header.TotalFragments);
                }

                if (_reassembler.TryPushPacket(packetSpan, out var completedFrame))
                {
                    if (EnableNack)
                    {
                        NackTracker.OnFrameCompleted(completedFrame.FrameID);
                    }

                    Metrics.RecordFrame(completedFrame.Length, (uint)Environment.TickCount64 - completedFrame.TimestampMs);
                    _frameChannel.Writer.TryWrite(completedFrame);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(recvBuffer);
        }
    }

    private async Task NackTimeoutLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                if (EnableNack)
                {
                    NackTracker.CheckTimeouts();
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void OnNackRequested(uint frameId, ushort[] missingFragments)
    {
        var targetEp = _lastSenderEndpoint;
        var socket = _socket;
        if (targetEp == null || socket == null || missingFragments.Length == 0) return;

        try
        {
            byte[] nackBuf = ArrayPool<byte>.Shared.Rent(RudpHeader.Size + missingFragments.Length * sizeof(ushort));
            try
            {
                int len = NackPacket.WriteBatch(nackBuf, frameId, missingFragments);
                socket.SendTo(nackBuf.AsSpan(0, len), SocketFlags.None, targetEp);
                Metrics.RecordNack();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(nackBuf);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_socketLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopReceiving();
            NackTracker.NackRequested -= OnNackRequested;
            _reassembler.Dispose();
        }
    }
}
