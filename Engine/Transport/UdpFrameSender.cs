using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Protocol.Packets;
using Protocol.Rudp;

namespace Engine.Transport;

public sealed class UdpFrameSender : IDisposable
{
    private readonly Socket _socket;
    private uint _frameCounter;

    public long TotalPacketsSent { get; private set; }
    public long TotalBytesSent { get; private set; }

    public UdpFrameSender()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = 4 * 1024 * 1024 // 4 MB
        };
    }

    public async ValueTask<ushort> SendFrameAsync(ReadOnlyMemory<byte> jpegBytes, IPEndPoint targetEndpoint, CancellationToken ct = default)
    {
        uint frameID = ++_frameCounter;
        ushort totalFrags = FrameFragmenter.CalculateTotalFragments(jpegBytes.Length);
        uint timestamp = (uint)Environment.TickCount64;

        byte[] packetBuffer = ArrayPool<byte>.Shared.Rent(RudpHeader.MaxPacketSize);
        try
        {
            for (ushort fragID = 0; fragID < totalFrags; fragID++)
            {
                int packetLen = FrameFragmenter.WriteFragmentPacket(frameID, fragID, totalFrags, timestamp, jpegBytes.Span, packetBuffer);

                await _socket.SendToAsync(packetBuffer.AsMemory(0, packetLen), SocketFlags.None, targetEndpoint, ct);

                TotalPacketsSent++;
                TotalBytesSent += packetLen;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetBuffer);
        }

        return totalFrags;
    }

    public void Dispose() => _socket.Dispose();
}