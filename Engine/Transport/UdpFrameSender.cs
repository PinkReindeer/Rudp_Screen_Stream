using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Protocol.Packets;
using Protocol.Rudp;

namespace Engine.Transport;

public sealed class UdpFrameSender : IDisposable
{
    private readonly Socket _socket;
    private readonly RingBuffer _ringBuffer = new();
    private readonly CancellationTokenSource _cts = new();
    private uint _frameCounter;
    private long _totalPacketsSent;
    private long _totalBytesSent;
    private long _totalNacksReceived;
    private long _totalRetransmittedPackets;

    public long TotalPacketsSent => Interlocked.Read(ref _totalPacketsSent);
    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
    public long TotalNacksReceived => Interlocked.Read(ref _totalNacksReceived);
    public long TotalRetransmittedPackets => Interlocked.Read(ref _totalRetransmittedPackets);
    public RingBuffer Buffer => _ringBuffer;

    public UdpFrameSender()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = 4 * 1024 * 1024, // 4 MB
            ReceiveBufferSize = 2 * 1024 * 1024
        };
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        Task.Run(() => NackListenLoopAsync(_cts.Token));
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

                _ringBuffer.Add(frameID, fragID, packetBuffer.AsSpan(0, packetLen));

                await _socket.SendToAsync(packetBuffer.AsMemory(0, packetLen), SocketFlags.None, targetEndpoint, ct);

                Interlocked.Increment(ref _totalPacketsSent);
                Interlocked.Add(ref _totalBytesSent, packetLen);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetBuffer);
        }

        return totalFrags;
    }

    private async Task NackListenLoopAsync(CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(RudpHeader.MaxPacketSize);
        byte[] retransmitBuffer = ArrayPool<byte>.Shared.Rent(RudpHeader.MaxPacketSize);
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _socket.ReceiveFromAsync(buffer.AsMemory(0, RudpHeader.MaxPacketSize), SocketFlags.None, remoteEp, ct);
                if (result.ReceivedBytes >= RudpHeader.Size)
                {
                    if (NackTracker.TryParseNack(buffer.AsSpan(0, result.ReceivedBytes), out uint frameID, out ushort[] missingFragIDs))
                    {
                        Interlocked.Increment(ref _totalNacksReceived);

                        foreach (var fragID in missingFragIDs)
                        {
                            if (_ringBuffer.TryCopy(frameID, fragID, retransmitBuffer, out int packetLen))
                            {
                                await _socket.SendToAsync(retransmitBuffer.AsMemory(0, packetLen), SocketFlags.None, result.RemoteEndPoint, ct);
                                Interlocked.Increment(ref _totalRetransmittedPackets);
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch { }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(retransmitBuffer);
        }
    }

    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalPacketsSent, 0);
        Interlocked.Exchange(ref _totalBytesSent, 0);
        Interlocked.Exchange(ref _totalNacksReceived, 0);
        Interlocked.Exchange(ref _totalRetransmittedPackets, 0);
        _ringBuffer.Clear();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _socket.Dispose();
    }
}
