using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Protocol.Packets;
using Protocol.Rudp;

namespace Engine.Transport;

public sealed class UdpFrameReceiver : IDisposable
{
    private readonly Socket _socket;
    private readonly FrameReassembler _reassembler = new();
    private readonly Channel<CompletedFrame> _frameChannel;
    private CancellationTokenSource? _cts;

    public ChannelReader<CompletedFrame> CompletedFrames => _frameChannel.Reader;
    public FrameReassembler Reassembler => _reassembler;

    public UdpFrameReceiver(int listenPort)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 8 * 1024 * 1024 // 8 MB
        };
        _socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));

        _frameChannel = Channel.CreateBounded<CompletedFrame>(new BoundedChannelOptions(2)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        }, droppedFrame => droppedFrame.Dispose());
    }

    public void StartReceiving()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] recvBuffer = ArrayPool<byte>.Shared.Rent(RudpHeader.MaxPacketSize);
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _socket.ReceiveFromAsync(recvBuffer.AsMemory(), SocketFlags.None, remoteEp, ct);

                if (_reassembler.TryPushPacket(recvBuffer.AsSpan(0, result.ReceivedBytes), out var completedFrame))
                {
                    _frameChannel.Writer.TryWrite(completedFrame);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            ArrayPool<byte>.Shared.Return(recvBuffer);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _socket.Dispose();
        _reassembler.Dispose();
    }
}