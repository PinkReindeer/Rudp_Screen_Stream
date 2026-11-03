using Protocol.Packets;

namespace Protocol.Rudp;

public static class FrameFragmenter
{
    public static ushort CalculateTotalFragments(int frameByteLength)
    {
        if (frameByteLength <= 0) return 0;
        return (ushort)((frameByteLength + RudpHeader.MaxPayloadSize - 1) / RudpHeader.MaxPayloadSize);
    }

    public static int WriteFragmentPacket(uint frameID, ushort fragmentID, ushort totalFragments,
        uint timestampMs, ReadOnlySpan<byte> fullFrameData, Span<byte> packetBuffer)
    {
        int offset = fragmentID * RudpHeader.MaxPayloadSize;
        int payloadLength = Math.Min(RudpHeader.MaxPayloadSize, fullFrameData.Length - offset);

        var header = new RudpHeader(frameID, fragmentID, totalFragments, PacketFlags.Data, timestampMs, (ushort)payloadLength);

        header.WriteTo(packetBuffer[..RudpHeader.Size]);
        fullFrameData.Slice(offset, payloadLength).CopyTo(packetBuffer.Slice(RudpHeader.Size, payloadLength));
        
        return RudpHeader.Size + payloadLength;
    }
}
