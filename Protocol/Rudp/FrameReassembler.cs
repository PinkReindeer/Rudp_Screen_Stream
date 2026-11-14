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
        public byte[]? Buffer;
        public bool[] ReceivedFlags = new bool[512];
        public bool IsComplete;

        public void Reset()
        {
            if (Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(Buffer);
                Buffer = null;
            }
            FrameID = 0;
            TotalFragments = 0;
            ReceivedCount = 0;
            AccumulatedLength = 0;
            TimestampMs = 0;
            IsComplete = false;
            Array.Clear(ReceivedFlags, 0, ReceivedFlags.Length);
        }
    }

    // Single-slot fields (Legacy / RingBuffer disabled mode)
    private uint _currentFrameID;
    private ushort _totalFragments;
    private ushort _receivedCount;
    private int _accumulatedLength;
    private byte[]? _frameBuffer;
    private bool[] _receivedFlags = new bool[1024];

    // Multi-slot RingBuffer fields
    private const int RingBufferSize = 32;
    private const int RingBufferMask = RingBufferSize - 1;
    private readonly FrameSlot[] _slots = new FrameSlot[RingBufferSize];

    public bool EnableRingBuffer { get; set; } = false;

    public long TotalFragmentsReceived { get; private set; }
    public long TotalFramesCompleted { get; private set; }
    public long TotalFramesDroppedIncomplete { get; private set; }

    public FrameReassembler()
    {
        for (int i = 0; i < RingBufferSize; i++)
        {
            _slots[i] = new FrameSlot();
        }
    }

    public bool TryPushPacket(ReadOnlySpan<byte> rawPacket, out CompletedFrame completedFrame)
    {
        completedFrame = default;
        if (rawPacket.Length < RudpHeader.Size) return false;

        var header = RudpHeader.ReadFrom(rawPacket);
        if ((header.Flags & PacketFlags.Data) == 0) return false;
        if (rawPacket.Length < RudpHeader.Size + header.PayloadLength) return false;
        if (header.FragmentID >= header.TotalFragments || header.TotalFragments == 0) return false;

        TotalFragmentsReceived++;

        if (EnableRingBuffer)
        {
            return PushWithRingBuffer(header, rawPacket, out completedFrame);
        }
        else
        {
            return PushSingleSlot(header, rawPacket, out completedFrame);
        }
    }

    private bool PushSingleSlot(in RudpHeader header, ReadOnlySpan<byte> rawPacket, out CompletedFrame completedFrame)
    {
        completedFrame = default;

        // Discard fragments belonging to the previous frame if we have already moved on to a new frame
        if (header.FrameID < _currentFrameID && _currentFrameID != 0)
        {
            return false;
        }

        // If receiving a new frame, initialize new slot
        if (header.FrameID > _currentFrameID || _frameBuffer == null)
        {
            if (_frameBuffer != null && _receivedCount < _totalFragments)
            {
                TotalFramesDroppedIncomplete++;
                ArrayPool<byte>.Shared.Return(_frameBuffer);
            }

            _currentFrameID = header.FrameID;
            _totalFragments = header.TotalFragments;
            _receivedCount = 0;
            _accumulatedLength = 0;

            int maxPossibleBytes = _totalFragments * RudpHeader.MaxPayloadSize;
            _frameBuffer = ArrayPool<byte>.Shared.Rent(maxPossibleBytes);

            if (_receivedFlags.Length < _totalFragments)
                _receivedFlags = new bool[_totalFragments * 2];
            else
                Array.Clear(_receivedFlags, 0, _totalFragments);
        }

        // Prevent duplicate fragments
        if (_receivedFlags[header.FragmentID]) return false;

        _receivedFlags[header.FragmentID] = true;
        _receivedCount++;

        int destOffset = header.FragmentID * RudpHeader.MaxPayloadSize;
        rawPacket.Slice(RudpHeader.Size, header.PayloadLength).CopyTo(_frameBuffer.AsSpan(destOffset, header.PayloadLength));

        int endPos = destOffset + header.PayloadLength;
        if (endPos > _accumulatedLength)
        {
            _accumulatedLength = endPos;
        }

        if (_receivedCount == _totalFragments)
        {
            TotalFramesCompleted++;
            completedFrame = new CompletedFrame(_currentFrameID, _frameBuffer, _accumulatedLength, header.TimestampMs);
            _frameBuffer = null; // Transfer ownership to CompletedFrame
            return true;
        }

        return false;
    }

    private bool PushWithRingBuffer(in RudpHeader header, ReadOnlySpan<byte> rawPacket, out CompletedFrame completedFrame)
    {
        completedFrame = default;

        int slotIdx = (int)(header.FrameID & RingBufferMask);
        var slot = _slots[slotIdx];

        // If slot has a different frame ID
        if (slot.FrameID != header.FrameID)
        {
            if (slot.Buffer != null && !slot.IsComplete && slot.ReceivedCount < slot.TotalFragments)
            {
                TotalFramesDroppedIncomplete++;
            }

            slot.Reset();
            slot.FrameID = header.FrameID;
            slot.TotalFragments = header.TotalFragments;
            slot.TimestampMs = header.TimestampMs;

            int maxPossibleBytes = slot.TotalFragments * RudpHeader.MaxPayloadSize;
            slot.Buffer = ArrayPool<byte>.Shared.Rent(maxPossibleBytes);

            if (slot.ReceivedFlags.Length < slot.TotalFragments)
                slot.ReceivedFlags = new bool[slot.TotalFragments * 2];
            else
                Array.Clear(slot.ReceivedFlags, 0, slot.TotalFragments);
        }

        if (slot.IsComplete || slot.Buffer == null)
        {
            return false;
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
            slot.IsComplete = true;
            TotalFramesCompleted++;
            completedFrame = new CompletedFrame(slot.FrameID, slot.Buffer, slot.AccumulatedLength, slot.TimestampMs);
            slot.Buffer = null; // Transfer ownership to CompletedFrame
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_frameBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_frameBuffer);
            _frameBuffer = null;
        }

        for (int i = 0; i < RingBufferSize; i++)
        {
            _slots[i].Reset();
        }
    }
}
