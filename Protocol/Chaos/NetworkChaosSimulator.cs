namespace Protocol.Chaos;

public class NetworkChaosSimulator
{
    private volatile int _dropRatePercent;
    private long _totalInspected;
    private long _totalDropped;

    public int DropRatePercent
    {
        get => _dropRatePercent;
        set => _dropRatePercent = Math.Clamp(value, 0, 100);
    }

    public long TotalInspected => Interlocked.Read(ref _totalInspected);
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

    public bool ShouldDrop()
    {
        Interlocked.Increment(ref _totalInspected);
        int rate = _dropRatePercent;
        if (rate <= 0) return false;
        if (rate >= 100)
        {
            Interlocked.Increment(ref _totalDropped);
            return true;
        }

        if (Random.Shared.Next(100) < rate)
        {
            Interlocked.Increment(ref _totalDropped);
            return true;
        }

        return false;
    }

    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalInspected, 0);
        Interlocked.Exchange(ref _totalDropped, 0);
    }
}

public sealed class ChaosSimulator : NetworkChaosSimulator
{
}
