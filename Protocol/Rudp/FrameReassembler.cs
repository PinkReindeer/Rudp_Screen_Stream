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
    private uint _currentFrameID;
    private ushort _totalFragments;
    private ushort _receivedCount;
    private int _accumulatedLength;
    private byte[]? _frameBuffer;
    private bool[] _receivedFlags = new bool[1024];

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

        TotalFragmentsReceived++;

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

        // Calculate the accumulated length when the last fragment is received
        int endPos = destOffset + header.PayloadLength;
        if (endPos > _accumulatedLength)
        {
            _accumulatedLength = endPos;
        }

        // Check if all fragments have been received
        if (_receivedCount == _totalFragments)
        {
            TotalFramesCompleted++;
            completedFrame = new CompletedFrame(_currentFrameID, _frameBuffer, _accumulatedLength, header.TimestampMs);
            _frameBuffer = null; // Transfer ownership to CompletedFrame
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
    }
}
