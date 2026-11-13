using System.Buffers;
using Protocol.Packets;

namespace Protocol.Rudp;

public readonly struct CompletedFrame : IDisposable
{
    public readonly uint FrameID;
    public readonly byte[] PooledBuffer;
    public readonly int Length;
    public readonly uint TimestampMs;

    public CompletedFrame(uint frameID, byte[] pooledBuffer, int length, uint timestampMs)
    {
        FrameID = frameID;
        PooledBuffer = pooledBuffer;
        Length = length;
        TimestampMs = timestampMs;
    }

    public ReadOnlySpan<byte> AsSpan() => PooledBuffer.AsSpan(0, Length);

    public void Dispose()
    {
        if (PooledBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(PooledBuffer);
        }
    }
}

public sealed class FrameReassembler : IDisposable
{
    private sealed class FrameSlot
    {
        public uint FrameID;
        public ushort TotalFragments;
        public ushort ReceivedCount;
        public int AccumulatedLength;
        public uint TimestampMs;
        public long CreatedTick;
        public byte[]? Buffer;
        public bool[] ReceivedFlags = new bool[512];

        public void Reset()
        {
            FrameID = 0;
            TotalFragments = 0;
            ReceivedCount = 0;
            AccumulatedLength = 0;
            TimestampMs = 0;
            CreatedTick = 0;
            if (Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(Buffer);
                Buffer = null;
            }
        }
    }

    private const int MaxPendingFrames = 8;
    private const int MaxFrameAgeMs = 250;

    private readonly List<FrameSlot> _activeSlots = new(MaxPendingFrames);
    private readonly object _lock = new();

    private uint _lastCompletedFrameID;

    public long TotalFragmentsReceived { get; private set; }
    public long TotalFramesCompleted { get; private set; }
    public long TotalFramesDroppedIncomplete { get; private set; }

    public bool TryPushPacket(ReadOnlySpan<byte> rawPacket, out CompletedFrame completedFrame)
    {
        completedFrame = default;
        if (rawPacket.Length < RudpHeader.Size) return false;

        var header = RudpHeader.ReadFrom(rawPacket);
        if ((header.Flags & PacketFlags.Data) == 0) return false;
        if (rawPacket.Length < RudpHeader.Size + header.PayloadLength) return false;
        if (header.FragmentID >= header.TotalFragments || header.TotalFragments == 0) return false;

        lock (_lock)
        {
            TotalFragmentsReceived++;

            if (_lastCompletedFrameID > 0 && header.FrameID < _lastCompletedFrameID)
            {
                return false;
            }

            long currentTick = Environment.TickCount64;
            EvictStaleFramesLocked(currentTick);

            var slot = FindOrCreateSlotLocked(header.FrameID, header.TotalFragments, header.TimestampMs, currentTick);
            if (slot == null || slot.Buffer == null)
            {
                return false;
            }

            if (header.FragmentID >= slot.ReceivedFlags.Length)
            {
                Array.Resize(ref slot.ReceivedFlags, Math.Max(slot.ReceivedFlags.Length * 2, (int)header.TotalFragments));
            }

            if (slot.ReceivedFlags[header.FragmentID])
            {
                return false;
            }

            slot.ReceivedFlags[header.FragmentID] = true;
            slot.ReceivedCount++;

            int destOffset = header.FragmentID * RudpHeader.MaxPayloadSize;
            rawPacket.Slice(RudpHeader.Size, header.PayloadLength).CopyTo(slot.Buffer.AsSpan(destOffset, header.PayloadLength));

            int endPos = destOffset + header.PayloadLength;
            if (endPos > slot.AccumulatedLength)
            {
                slot.AccumulatedLength = endPos;
            }

            if (slot.ReceivedCount == slot.TotalFragments)
            {
                TotalFramesCompleted++;
                _lastCompletedFrameID = slot.FrameID;

                var finalBuffer = slot.Buffer;
                int finalLength = slot.AccumulatedLength;
                uint finalTimestamp = slot.TimestampMs;

                slot.Buffer = null;
                _activeSlots.Remove(slot);

                PruneOlderSlotsLocked(header.FrameID);

                completedFrame = new CompletedFrame(header.FrameID, finalBuffer, finalLength, finalTimestamp);
                return true;
            }

            return false;
        }
    }

    public void CollectMissingFragments(uint currentFrameID, List<(uint FrameID, ushort FragmentID)> output)
    {
        lock (_lock)
        {
            foreach (var slot in _activeSlots)
            {
                if (slot.FrameID == 0 || slot.TotalFragments == 0 || slot.Buffer == null)
                {
                    continue;
                }

                ushort maxFragToCheck = slot.FrameID < currentFrameID ? slot.TotalFragments : (ushort)0;
                if (slot.FrameID == currentFrameID)
                {
                    for (ushort i = (ushort)(slot.TotalFragments - 1); i < slot.TotalFragments; i--)
                    {
                        if (slot.ReceivedFlags[i])
                        {
                            maxFragToCheck = i;
                            break;
                        }
                    }
                }

                for (ushort i = 0; i < maxFragToCheck; i++)
                {
                    if (!slot.ReceivedFlags[i])
                    {
                        output.Add((slot.FrameID, i));
                    }
                }
            }
        }
    }

    private FrameSlot? FindOrCreateSlotLocked(uint frameID, ushort totalFragments, uint timestampMs, long currentTick)
    {
        for (int i = 0; i < _activeSlots.Count; i++)
        {
            if (_activeSlots[i].FrameID == frameID)
            {
                return _activeSlots[i];
            }
        }

        if (_activeSlots.Count >= MaxPendingFrames)
        {
            int oldestIdx = 0;
            uint oldestFrameID = _activeSlots[0].FrameID;
            for (int i = 1; i < _activeSlots.Count; i++)
            {
                if (_activeSlots[i].FrameID < oldestFrameID)
                {
                    oldestFrameID = _activeSlots[i].FrameID;
                    oldestIdx = i;
                }
            }

            var evicted = _activeSlots[oldestIdx];
            TotalFramesDroppedIncomplete++;
            evicted.Reset();
            _activeSlots.RemoveAt(oldestIdx);
        }

        var newSlot = new FrameSlot
        {
            FrameID = frameID,
            TotalFragments = totalFragments,
            ReceivedCount = 0,
            AccumulatedLength = 0,
            TimestampMs = timestampMs,
            CreatedTick = currentTick
        };

        if (newSlot.ReceivedFlags.Length < totalFragments)
        {
            newSlot.ReceivedFlags = new bool[totalFragments];
        }
        else
        {
            Array.Clear(newSlot.ReceivedFlags, 0, newSlot.ReceivedFlags.Length);
        }

        int maxPossibleBytes = totalFragments * RudpHeader.MaxPayloadSize;
        newSlot.Buffer = ArrayPool<byte>.Shared.Rent(maxPossibleBytes);

        _activeSlots.Add(newSlot);
        return newSlot;
    }

    private void EvictStaleFramesLocked(long currentTick)
    {
        for (int i = _activeSlots.Count - 1; i >= 0; i--)
        {
            var slot = _activeSlots[i];
            if (currentTick - slot.CreatedTick > MaxFrameAgeMs)
            {
                TotalFramesDroppedIncomplete++;
                slot.Reset();
                _activeSlots.RemoveAt(i);
            }
        }
    }

    private void PruneOlderSlotsLocked(uint completedFrameID)
    {
        for (int i = _activeSlots.Count - 1; i >= 0; i--)
        {
            var slot = _activeSlots[i];
            if (slot.FrameID < completedFrameID)
            {
                TotalFramesDroppedIncomplete++;
                slot.Reset();
                _activeSlots.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var slot in _activeSlots)
            {
                slot.Reset();
            }
            _activeSlots.Clear();
        }
    }
}
