using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
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
    private readonly object _lock = new();

    private EndPoint? _senderEndpoint;
    private readonly List<(uint FrameID, ushort FragmentID)> _missingBuffer = new(64);
    private readonly byte[] _nackBuffer = new byte[RudpHeader.MaxPacketSize];

    public ChannelReader<CompletedFrame> CompletedFrames => _frameChannel.Reader;
    public FrameReassembler Reassembler => _reassembler;
    public ChaosSimulator Chaos { get; } = new();
    public NackTracker Nack { get; } = new();
    public bool NackEnabled { get; set; } = true;
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

        BindAndStart(listenPort);
    }

    public void Rebind(int newPort)
    {
        lock (_lock)
        {
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
            ReceiveBufferSize = 8 * 1024 * 1024, // 8 MB
            SendBufferSize = 1 * 1024 * 1024
        };
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));

        _cts = new CancellationTokenSource();
        var socket = _socket;
        var token = _cts.Token;

        _ = Task.Run(() => ReceiveLoopAsync(socket, token));
        _ = Task.Run(() => NackMaintenanceLoopAsync(socket, token));
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
                var result = await socket.ReceiveFromAsync(recvBuffer.AsMemory(), SocketFlags.None, remoteEp, ct);
                _senderEndpoint = result.RemoteEndPoint;

                if (Chaos.ShouldDrop())
                {
                    continue;
                }

                if (result.ReceivedBytes < RudpHeader.Size)
                {
                    continue;
                }

                var header = RudpHeader.ReadFrom(recvBuffer.AsSpan(0, result.ReceivedBytes));
                if ((header.Flags & PacketFlags.Data) != 0)
                {
                    Nack.MarkReceived(header.FrameID, header.FragmentID);

                    if (_reassembler.TryPushPacket(recvBuffer.AsSpan(0, result.ReceivedBytes), out var completedFrame))
                    {
                        Nack.RemoveFrame(completedFrame.FrameID);
                        _frameChannel.Writer.TryWrite(completedFrame);
                    }
                    else if (NackEnabled)
                    {
                        TriggerNacksForFrame(socket, header.FrameID);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch { }
        finally
        {
            ArrayPool<byte>.Shared.Return(recvBuffer);
        }
    }

    private async Task NackMaintenanceLoopAsync(Socket socket, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(15));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            if (!NackEnabled || _senderEndpoint == null) continue;
            try
            {
                TriggerPeriodicNacks(socket);
            }
            catch { }
        }
    }

    private void TriggerNacksForFrame(Socket socket, uint currentFrameID)
    {
        if (_senderEndpoint == null) return;

        lock (_missingBuffer)
        {
            _missingBuffer.Clear();
            _reassembler.CollectMissingFragments(currentFrameID, _missingBuffer);
            SendMissingNacks(socket);
        }
    }

    private void TriggerPeriodicNacks(Socket socket)
    {
        if (_senderEndpoint == null) return;

        lock (_missingBuffer)
        {
            _missingBuffer.Clear();
            _reassembler.CollectMissingFragments(uint.MaxValue, _missingBuffer);
            SendMissingNacks(socket);
        }
    }

    private void SendMissingNacks(Socket socket)
    {
        if (_missingBuffer.Count == 0 || _senderEndpoint == null) return;

        long now = Environment.TickCount64;
        var groups = _missingBuffer.GroupBy(x => x.FrameID);

        foreach (var group in groups)
        {
            var missingList = new List<ushort>();
            foreach (var item in group)
            {
                if (Nack.ShouldRequest(item.FrameID, item.FragmentID, now))
                {
                    missingList.Add(item.FragmentID);
                }
            }

            if (missingList.Count > 0)
            {
                int len = NackTracker.WriteNackPacket(group.Key, missingList.ToArray(), _nackBuffer);
                try
                {
                    socket.SendTo(_nackBuffer.AsSpan(0, len), SocketFlags.None, _senderEndpoint);
                    Nack.IncrementNackSentCount();
                }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            StopReceiving();
            _reassembler.Dispose();
            Nack.Clear();
            Chaos.ResetStats();
        }
    }
}
