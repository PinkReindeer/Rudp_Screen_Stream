using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
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

    public ChannelReader<CompletedFrame> CompletedFrames => _frameChannel.Reader;
    public FrameReassembler Reassembler => _reassembler;
    public int CurrentPort { get; private set; }

    public UdpFrameReceiver(int listenPort)
    {
        CurrentPort = listenPort;

        _frameChannel = Channel.CreateBounded<CompletedFrame>(new BoundedChannelOptions(2)
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
            ReceiveBufferSize = 8 * 1024 * 1024 // 8 MB
        };
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));

        _cts = new CancellationTokenSource();
        var socket = _socket;
        var token = _cts.Token;
        _ = Task.Run(() => ReceiveLoopAsync(socket, token));
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

                if (_reassembler.TryPushPacket(recvBuffer.AsSpan(0, result.ReceivedBytes), out var completedFrame))
                {
                    _frameChannel.Writer.TryWrite(completedFrame);
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

    public void Dispose()
    {
        lock (_lock)
        {
            StopReceiving();
            _reassembler.Dispose();
        }
    }
}
