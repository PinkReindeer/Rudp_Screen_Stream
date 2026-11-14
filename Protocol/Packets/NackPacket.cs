using System.Buffers.Binary;

namespace Protocol.Packets;

public static class NackPacket
{
    /// <summary>
    /// Writes a single NACK packet to destination buffer.
    /// </summary>
    public static int WriteSingle(Span<byte> destination, uint frameId, ushort fragmentId)
    {
        var header = new RudpHeader(
            frameID: frameId,
            fragmentID: fragmentId,
            totalFragments: 0,
            flags: PacketFlags.Nack,
            timestampMs: (uint)Environment.TickCount64,
            payloadLength: 0
        );

        header.WriteTo(destination[..RudpHeader.Size]);
        return RudpHeader.Size;
    }

    /// <summary>
    /// Writes a batch NACK packet containing multiple missing fragment IDs to destination buffer.
    /// </summary>
    public static int WriteBatch(Span<byte> destination, uint frameId, ReadOnlySpan<ushort> missingFragments)
    {
        if (missingFragments.IsEmpty) return 0;

        ushort payloadLen = (ushort)(missingFragments.Length * sizeof(ushort));
        var header = new RudpHeader(
            frameID: frameId,
            fragmentID: missingFragments[0],
            totalFragments: 0,
            flags: PacketFlags.Nack,
            timestampMs: (uint)Environment.TickCount64,
            payloadLength: payloadLen
        );

        header.WriteTo(destination[..RudpHeader.Size]);
        var payloadSpan = destination.Slice(RudpHeader.Size, payloadLen);

        for (int i = 0; i < missingFragments.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payloadSpan.Slice(i * 2, 2), missingFragments[i]);
        }

        return RudpHeader.Size + payloadLen;
    }

    /// <summary>
    /// Parses a NACK packet and yields the frame ID and all missing fragment IDs requested.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> packet, out uint frameId, out List<ushort> missingFragmentIds)
    {
        missingFragmentIds = [];
        frameId = 0;

        if (packet.Length < RudpHeader.Size) return false;

        var header = RudpHeader.ReadFrom(packet);
        if ((header.Flags & PacketFlags.Nack) == 0) return false;

        frameId = header.FrameID;

        if (header.PayloadLength >= 2 && packet.Length >= RudpHeader.Size + header.PayloadLength)
        {
            int count = header.PayloadLength / 2;
            var payload = packet.Slice(RudpHeader.Size, header.PayloadLength);
            for (int i = 0; i < count; i++)
            {
                ushort fragId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(i * 2, 2));
                missingFragmentIds.Add(fragId);
            }
        }
        else
        {
            missingFragmentIds.Add(header.FragmentID);
        }

        return true;
    }
}
