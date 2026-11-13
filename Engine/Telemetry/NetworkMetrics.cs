namespace Engine.Telemetry;

public sealed class NetworkMetrics
{
    private long _frameCount;
    private long _byteCount;
    private long _lastCalculatedTick;
    private long _lastFrameCount;
    private long _lastByteCount;

    public double CurrentFps { get; private set; }
    public double CurrentBitrateMbps { get; private set; }
    public double PacketLossPercent { get; private set; }
    public double LatencyMs { get; private set; }

    public NetworkMetrics()
    {
        _lastCalculatedTick = Environment.TickCount64;
    }

    public void OnFrameProcessed(int byteLength, uint latencyMs = 0)
    {
        Interlocked.Increment(ref _frameCount);
        Interlocked.Add(ref _byteCount, byteLength);
        if (latencyMs > 0)
        {
            LatencyMs = LatencyMs <= 0 ? latencyMs : (LatencyMs * 0.8) + (latencyMs * 0.2);
        }
    }

    public void UpdateLoss(long droppedPackets, long totalPackets)
    {
        if (totalPackets <= 0)
        {
            PacketLossPercent = 0;
            return;
        }

        PacketLossPercent = Math.Clamp((double)droppedPackets / totalPackets * 100.0, 0.0, 100.0);
    }

    public void Tick()
    {
        long now = Environment.TickCount64;
        long elapsedMs = now - _lastCalculatedTick;
        if (elapsedMs < 400) return;

        long currentFrames = Interlocked.Read(ref _frameCount);
        long currentBytes = Interlocked.Read(ref _byteCount);

        long deltaFrames = currentFrames - _lastFrameCount;
        long deltaBytes = currentBytes - _lastByteCount;

        _lastFrameCount = currentFrames;
        _lastByteCount = currentBytes;
        _lastCalculatedTick = now;

        double seconds = elapsedMs / 1000.0;
        CurrentFps = seconds > 0 ? deltaFrames / seconds : 0;
        CurrentBitrateMbps = seconds > 0 ? (deltaBytes * 8.0) / (seconds * 1_000_000.0) : 0;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _frameCount, 0);
        Interlocked.Exchange(ref _byteCount, 0);
        _lastFrameCount = 0;
        _lastByteCount = 0;
        _lastCalculatedTick = Environment.TickCount64;
        CurrentFps = 0;
        CurrentBitrateMbps = 0;
        PacketLossPercent = 0;
        LatencyMs = 0;
    }
}
