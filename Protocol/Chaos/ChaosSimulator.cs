namespace Protocol.Chaos;

/// <summary>
/// Simulates adverse network conditions such as packet loss (drop rate) and latency.
/// </summary>
public class ChaosSimulator
{
    private double _dropRate;
    private long _totalPacketsEvaluated;
    private long _totalPacketsDropped;

    /// <summary>
    /// Packet drop rate between 0.0 (0%) and 1.0 (100%).
    /// </summary>
    public double DropRate
    {
        get => _dropRate;
        set => _dropRate = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// Packet drop rate as an integer percentage (0 to 100).
    /// </summary>
    public int DropRatePercent
    {
        get => (int)Math.Round(_dropRate * 100.0);
        set => DropRate = value / 100.0;
    }

    /// <summary>
    /// Simulated latency in milliseconds.
    /// </summary>
    public int LatencyMs { get; set; } = 0;

    /// <summary>
    /// Total number of packets evaluated by the chaos proxy.
    /// </summary>
    public long TotalPacketsEvaluated => Interlocked.Read(ref _totalPacketsEvaluated);

    /// <summary>
    /// Total number of packets intentionally dropped by the chaos proxy.
    /// </summary>
    public long TotalPacketsDropped => Interlocked.Read(ref _totalPacketsDropped);

    /// <summary>
    /// Actual observed drop rate percentage.
    /// </summary>
    public double ActualDropRatePercent
    {
        get
        {
            long eval = TotalPacketsEvaluated;
            return eval > 0 ? (TotalPacketsDropped * 100.0) / eval : 0.0;
        }
    }

    /// <summary>
    /// Evaluates whether an incoming packet should be dropped according to the configured drop rate.
    /// </summary>
    /// <returns>True if the packet should be dropped; false to let it pass through.</returns>
    public bool ShouldDrop()
    {
        if (_dropRate <= 0.0) return false;

        Interlocked.Increment(ref _totalPacketsEvaluated);

        if (Random.Shared.NextDouble() < _dropRate)
        {
            Interlocked.Increment(ref _totalPacketsDropped);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Intercepts an incoming raw UDP packet. Returns true if the packet was dropped.
    /// </summary>
    public bool Intercept(ReadOnlySpan<byte> packet)
    {
        return ShouldDrop();
    }

    /// <summary>
    /// Resets packet counters.
    /// </summary>
    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalPacketsEvaluated, 0);
        Interlocked.Exchange(ref _totalPacketsDropped, 0);
    }
}