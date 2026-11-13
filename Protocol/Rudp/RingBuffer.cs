using Protocol.Packets;

namespace Protocol.Rudp;

public sealed class RingBuffer
{
    private sealed class Slot
    {
        public uint FrameID;
        public ushort FragmentID;
        public readonly byte[] Buffer = new byte[RudpHeader.MaxPacketSize];
        public int Length;
    }

    private readonly Slot[] _slots;
    private readonly int _capacity;
    private readonly int _mask;
    private int _head;
    private readonly object _lock = new();

    public RingBuffer(int capacity = 4096)
    {
        _capacity = 1;
        while (_capacity < capacity)
        {
            _capacity <<= 1;
        }
        _mask = _capacity - 1;

        _slots = new Slot[_capacity];
        for (int i = 0; i < _capacity; i++)
        {
            _slots[i] = new Slot();
        }
    }

    public void Add(uint frameID, ushort fragmentID, ReadOnlySpan<byte> packetData)
    {
        lock (_lock)
        {
            var slot = _slots[_head & _mask];
            slot.FrameID = frameID;
            slot.FragmentID = fragmentID;
            slot.Length = packetData.Length;
            packetData.CopyTo(slot.Buffer);
            _head++;
        }
    }

    public bool TryCopy(uint frameID, ushort fragmentID, Span<byte> destination, out int bytesWritten)
    {
        lock (_lock)
        {
            int head = _head;
            int count = Math.Min(head, _capacity);
            for (int i = 0; i < count; i++)
            {
                int index = (head - 1 - i) & _mask;
                var slot = _slots[index];
                if (slot.FrameID == frameID && slot.FragmentID == fragmentID)
                {
                    slot.Buffer.AsSpan(0, slot.Length).CopyTo(destination);
                    bytesWritten = slot.Length;
                    return true;
                }
                if (frameID > slot.FrameID + 16)
                {
                    break;
                }
            }

            bytesWritten = 0;
            return false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            for (int i = 0; i < _capacity; i++)
            {
                _slots[i].FrameID = 0;
                _slots[i].FragmentID = 0;
                _slots[i].Length = 0;
            }
        }
    }
}
