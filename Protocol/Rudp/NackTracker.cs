using System.Buffers.Binary;
using Protocol.Packets;

namespace Protocol.Rudp;

public sealed class NackTracker
{
    private sealed class NackRecord
    {
        public long LastSentTick;
        public int Retries;
    }

    private readonly Dictionary<(uint FrameID, ushort FragmentID), NackRecord> _records = new();
    private readonly object _lock = new();

    public long TotalNacksSent { get; private set; }
    public long TotalFragmentsRequested { get; private set; }

    public bool ShouldRequest(uint frameID, ushort fragmentID, long currentTickMs, int minIntervalMs = 25, int maxRetries = 4)
    {
        lock (_lock)
        {
            var key = (frameID, fragmentID);
            if (_records.TryGetValue(key, out var record))
            {
                if (record.Retries >= maxRetries)
                {
                    return false;
                }

                if (currentTickMs - record.LastSentTick < minIntervalMs)
                {
                    return false;
                }

                record.LastSentTick = currentTickMs;
                record.Retries++;
                TotalFragmentsRequested++;
                return true;
            }

            _records[key] = new NackRecord
            {
                LastSentTick = currentTickMs,
                Retries = 1
            };
            TotalFragmentsRequested++;
            return true;
        }
    }

    public void MarkReceived(uint frameID, ushort fragmentID)
    {
        lock (_lock)
        {
            _records.Remove((frameID, fragmentID));
        }
    }

    public void RemoveFrame(uint frameID)
    {
        lock (_lock)
        {
            var toRemove = new List<(uint, ushort)>();
            foreach (var key in _records.Keys)
            {
                if (key.FrameID <= frameID)
                {
                    toRemove.Add(key);
                }
            }

            foreach (var key in toRemove)
            {
                _records.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _records.Clear();
            TotalNacksSent = 0;
            TotalFragmentsRequested = 0;
        }
    }

    public static int WriteNackPacket(uint frameID, ReadOnlySpan<ushort> missingFragmentIDs, Span<byte> destination)
    {
        ushort payloadLen = (ushort)(missingFragmentIDs.Length * 2);
        ushort primaryFrag = missingFragmentIDs.Length > 0 ? missingFragmentIDs[0] : (ushort)0;
        var header = new RudpHeader(frameID, primaryFrag, 0, PacketFlags.Nack, (uint)Environment.TickCount64, payloadLen);
        header.WriteTo(destination[..RudpHeader.Size]);

        for (int i = 0; i < missingFragmentIDs.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(RudpHeader.Size + i * 2, 2), missingFragmentIDs[i]);
        }

        return RudpHeader.Size + payloadLen;
    }

    public static bool TryParseNack(ReadOnlySpan<byte> packet, out uint frameID, out ushort[] missingFragmentIDs)
    {
        if (packet.Length < RudpHeader.Size)
        {
            frameID = 0;
            missingFragmentIDs = [];
            return false;
        }

        var header = RudpHeader.ReadFrom(packet);
        if ((header.Flags & PacketFlags.Nack) == 0)
        {
            frameID = 0;
            missingFragmentIDs = [];
            return false;
        }

        frameID = header.FrameID;
        int payloadLen = header.PayloadLength;
        if (payloadLen >= 2 && packet.Length >= RudpHeader.Size + payloadLen)
        {
            int count = payloadLen / 2;
            missingFragmentIDs = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                missingFragmentIDs[i] = BinaryPrimitives.ReadUInt16LittleEndian(
                    packet.Slice(RudpHeader.Size + i * 2, 2));
            }
        }
        else
        {
            missingFragmentIDs = [header.FragmentID];
        }

        return true;
    }

    public void IncrementNackSentCount()
    {
        TotalNacksSent++;
    }
}
