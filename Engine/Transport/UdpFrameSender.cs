using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Protocol.Packets;
using Protocol.Rudp;

namespace Engine.Transport;

public sealed class UdpFrameSender : IDisposable
{
    private sealed class SentFrame
    {
        public uint FrameID;
        public byte[][] Fragments = [];
        public int[] FragmentLengths = [];
    }

    private readonly Socket _socket;
    private readonly RingBuffer<SentFrame> _sentFramesRingBuffer = new(capacity: 64);
    private readonly CancellationTokenSource _cts = new();
    private uint _frameCounter;
    private long _totalPacketsSent;
    private long _totalBytesSent;
    private long _totalNacksReceived;
    private long _totalPacketsRetransmitted;

    public long TotalPacketsSent => Interlocked.Read(ref _totalPacketsSent);
    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
    public long TotalNacksReceived => Interlocked.Read(ref _totalNacksReceived);
    public long TotalPacketsRetransmitted => Interlocked.Read(ref _totalPacketsRetransmitted);

    public int LocalPort => (_socket.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    public UdpFrameSender()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = 4 * 1024 * 1024, // 4 MB
            ReceiveBufferSize = 1 * 1024 * 1024 // 1 MB
        };

        // Bind to any available local port so the socket can also receive NACK packets
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        // Start listening for incoming NACK retransmission requests
        _ = Task.Run(() => ReceiveNackLoopAsync(_cts.Token));
    }

    public async ValueTask SendProbeAsync(IPEndPoint targetEndpoint, CancellationToken ct = default)
    {
        var targetEp = targetEndpoint.Address.IsIPv4MappedToIPv6
            ? new IPEndPoint(targetEndpoint.Address.MapToIPv4(), targetEndpoint.Port)
            : targetEndpoint;

        byte[] packetBuffer = ArrayPool<byte>.Shared.Rent(RudpHeader.Size);
        try
        {
            var header = new RudpHeader(0, 0, 1, PacketFlags.Probe, (uint)Environment.TickCount64, 0);
            header.WriteTo(packetBuffer.AsSpan(0, RudpHeader.Size));
            await _socket.SendToAsync(packetBuffer.AsMemory(0, RudpHeader.Size), SocketFlags.None, targetEp, ct);
            // Send a second probe 10ms later to guard against rare transient packet drop during connect
            await Task.Delay(10, ct);
            await _socket.SendToAsync(packetBuffer.AsMemory(0, RudpHeader.Size), SocketFlags.None, targetEp, ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetBuffer);
        }
    }

    public async ValueTask<ushort> SendFrameAsync(ReadOnlyMemory<byte> jpegBytes, IPEndPoint targetEndpoint, CancellationToken ct = default)
    {
        var targetEp = targetEndpoint.Address.IsIPv4MappedToIPv6
            ? new IPEndPoint(targetEndpoint.Address.MapToIPv4(), targetEndpoint.Port)
            : targetEndpoint;

        uint frameID = ++_frameCounter;
        ushort totalFrags = FrameFragmenter.CalculateTotalFragments(jpegBytes.Length);
        uint timestamp = (uint)Environment.TickCount64;

        var sentFrame = new SentFrame
        {
            FrameID = frameID,
            Fragments = new byte[totalFrags][],
            FragmentLengths = new int[totalFrags]
        };

        byte[] packetBuffer = ArrayPool<byte>.Shared.Rent(RudpHeader.MaxPacketSize);
        try
        {
            for (ushort fragID = 0; fragID < totalFrags; fragID++)
            {
                int packetLen = FrameFragmenter.WriteFragmentPacket(frameID, fragID, totalFrags, timestamp, jpegBytes.Span, packetBuffer);

                // Cache packet for possible NACK retransmission
                byte[] cachedPacket = new byte[packetLen];
                packetBuffer.AsSpan(0, packetLen).CopyTo(cachedPacket);
                sentFrame.Fragments[fragID] = cachedPacket;
                sentFrame.FragmentLengths[fragID] = packetLen;

                await _socket.SendToAsync(packetBuffer.AsMemory(0, packetLen), SocketFlags.None, targetEp, ct);

                Interlocked.Increment(ref _totalPacketsSent);
                Interlocked.Add(ref _totalBytesSent, packetLen);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetBuffer);
        }

        // Store in circular buffer for retransmission lookups
        _sentFramesRingBuffer.Set(frameID, sentFrame);

        return totalFrags;
    }

    private async Task ReceiveNackLoopAsync(CancellationToken ct)
    {
        byte[] recvBuffer = new byte[RudpHeader.MaxPacketSize];
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _socket.ReceiveFromAsync(recvBuffer.AsMemory(), SocketFlags.None, remoteEp, ct);
                var packetSpan = recvBuffer.AsSpan(0, result.ReceivedBytes);

                if (NackPacket.TryParse(packetSpan, out uint nackFrameId, out var missingFragments))
                {
                    Interlocked.Increment(ref _totalNacksReceived);

                    if (_sentFramesRingBuffer.TryGet(nackFrameId, out var sentFrame) && sentFrame != null && sentFrame.FrameID == nackFrameId)
                    {
                        foreach (var fragId in missingFragments)
                        {
                            if (fragId < sentFrame.Fragments.Length && sentFrame.Fragments[fragId] != null)
                            {
                                var fragBytes = sentFrame.Fragments[fragId];
                                int fragLen = sentFrame.FragmentLengths[fragId];

                                await _socket.SendToAsync(fragBytes.AsMemory(0, fragLen), SocketFlags.None, result.RemoteEndPoint, ct);

                                Interlocked.Increment(ref _totalPacketsRetransmitted);
                                Interlocked.Add(ref _totalBytesSent, fragLen);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue; // Ignore ICMP Port Unreachable on Windows UDP
            }
            catch { continue; }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _socket.Dispose();
        _sentFramesRingBuffer.Clear();
    }
}
