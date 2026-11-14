namespace Engine.Telemetry;

/// <summary>
/// Tracks network and streaming metrics including FPS, Bitrate, Packet Loss, and RTT.
/// </summary>
public sealed class NetworkMetrics
{
    private long _framesCounter;
    private long _bytesCounter;
    private long _packetsCounter;
    private long _packetsDroppedCounter;
    private long _nacksCounter;
    private long _retransmissionCounter;

    private long _lastTickTime = Environment.TickCount64;
    private long _lastFrames;
    private long _lastBytes;
    private long _lastPackets;
    private long _lastDropped;

    public double CurrentFps { get; private set; }
    public double CurrentBitrateMbps { get; private set; }
    public double CurrentPacketLossPercent { get; private set; }
    public uint CurrentLatencyMs { get; private set; }

    public long TotalFrames => Interlocked.Read(ref _framesCounter);
    public long TotalBytes => Interlocked.Read(ref _bytesCounter);
    public long TotalPackets => Interlocked.Read(ref _packetsCounter);
    public long TotalPacketsDropped => Interlocked.Read(ref _packetsDroppedCounter);
    public long TotalNacks => Interlocked.Read(ref _nacksCounter);
    public long TotalRetransmissions => Interlocked.Read(ref _retransmissionCounter);

    public void RecordFrame(int frameBytes, uint latencyMs)
    {
        Interlocked.Increment(ref _framesCounter);
        Interlocked.Add(ref _bytesCounter, frameBytes);
        CurrentLatencyMs = latencyMs;
    }

    public void RecordPacket(int packetBytes)
    {
        Interlocked.Increment(ref _packetsCounter);
        Interlocked.Add(ref _bytesCounter, packetBytes);
    }

    public void RecordPacketDropped()
    {
        Interlocked.Increment(ref _packetsDroppedCounter);
    }

    public void RecordNack()
    {
        Interlocked.Increment(ref _nacksCounter);
    }

    public void RecordRetransmission()
    {
        Interlocked.Increment(ref _retransmissionCounter);
    }

    /// <summary>
    /// Updates windowed rates (FPS, Bitrate, Loss Rate). Recommended to call every 500ms - 1000ms.
    /// </summary>
    public void UpdateSnapshot()
    {
        long now = Environment.TickCount64;
        long elapsedMs = Math.Max(1, now - _lastTickTime);
        _lastTickTime = now;

        long currentFrames = Interlocked.Read(ref _framesCounter);
        long currentBytes = Interlocked.Read(ref _bytesCounter);
        long currentPackets = Interlocked.Read(ref _packetsCounter);
        long currentDropped = Interlocked.Read(ref _packetsDroppedCounter);

        long deltaFrames = currentFrames - _lastFrames;
        long deltaBytes = currentBytes - _lastBytes;
        long deltaPackets = currentPackets - _lastPackets;
        long deltaDropped = currentDropped - _lastDropped;

        _lastFrames = currentFrames;
        _lastBytes = currentBytes;
        _lastPackets = currentPackets;
        _lastDropped = currentDropped;

        double elapsedSeconds = elapsedMs / 1000.0;
        CurrentFps = deltaFrames / elapsedSeconds;
        CurrentBitrateMbps = (deltaBytes * 8.0) / (elapsedSeconds * 1_000_000.0);

        long totalSampled = deltaPackets + deltaDropped;
        CurrentPacketLossPercent = totalSampled > 0 ? (deltaDropped * 100.0) / totalSampled : 0.0;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _framesCounter, 0);
        Interlocked.Exchange(ref _bytesCounter, 0);
        Interlocked.Exchange(ref _packetsCounter, 0);
        Interlocked.Exchange(ref _packetsDroppedCounter, 0);
        Interlocked.Exchange(ref _nacksCounter, 0);
        Interlocked.Exchange(ref _retransmissionCounter, 0);
        _lastFrames = 0;
        _lastBytes = 0;
        _lastPackets = 0;
        _lastDropped = 0;
        CurrentFps = 0;
        CurrentBitrateMbps = 0;
        CurrentPacketLossPercent = 0;
        CurrentLatencyMs = 0;
    }
}
