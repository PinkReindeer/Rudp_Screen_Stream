namespace Protocol.Rudp;

/// <summary>
/// A high-performance, thread-safe generic circular ring buffer.
/// Uses power-of-two sizing for bitwise modulo indexing.
/// </summary>
/// <typeparam name="T">Type of element stored in the ring buffer.</typeparam>
public class RingBuffer<T>
{
    private readonly T?[] _slots;
    private readonly int _mask;
    private readonly Lock _lock = new();

    public int Capacity { get; }

    public RingBuffer(int capacity = 64)
    {
        int powerOfTwo = 1;
        while (powerOfTwo < capacity) powerOfTwo <<= 1;
        Capacity = powerOfTwo;
        _mask = Capacity - 1;
        _slots = new T?[Capacity];
    }

    public void Set(long sequenceNumber, T? item)
    {
        lock (_lock)
        {
            _slots[sequenceNumber & _mask] = item;
        }
    }

    public bool TryGet(long sequenceNumber, out T? item)
    {
        lock (_lock)
        {
            item = _slots[sequenceNumber & _mask];
            return item != null;
        }
    }

    public T? Get(long sequenceNumber)
    {
        lock (_lock)
        {
            return _slots[sequenceNumber & _mask];
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_slots, 0, Capacity);
        }
    }
}
