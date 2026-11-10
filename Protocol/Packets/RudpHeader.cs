using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Protocol.Packets;

[Flags]
public enum PacketFlags : byte
{
    None = 0,
    Data = 1 << 0,  // 0x01
    Ack = 1 << 1,  // 0x02
    Nack = 1 << 2,  // 0x04
    Keyframe = 1 << 3  // 0x08
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RudpHeader
{
    public const int Size = 15;
    public const int MaxPayloadSize = 1385; // 15 + 1385 = 1400 bytes
    public const int MaxPacketSize = Size + MaxPayloadSize;

    public readonly uint FrameID;           // 4 bytes
    public readonly ushort FragmentID;      // 2 bytes
    public readonly ushort TotalFragments;  // 2 bytes
    public readonly PacketFlags Flags;      // 1 byte
    public readonly uint TimestampMs;       // 4 bytes
    public readonly ushort PayloadLength;   // 2 bytes

    public RudpHeader(uint frameID, ushort fragmentID, ushort totalFragments, PacketFlags flags, uint timestampMs, ushort payloadLength)
    {
        FrameID = frameID;
        FragmentID = fragmentID;
        TotalFragments = totalFragments;
        Flags = flags;
        TimestampMs = timestampMs;
        PayloadLength = payloadLength;
    }

    public void WriteTo(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination[0..4], FrameID);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..6], FragmentID);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..8], TotalFragments);
        destination[8] = (byte)Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[9..13], TimestampMs);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[13..15], PayloadLength);
    }

    public static RudpHeader ReadFrom(ReadOnlySpan<byte> source)
    {
        return new RudpHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(source[0..4]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[4..6]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[6..8]),
            (PacketFlags)source[8],
            BinaryPrimitives.ReadUInt32LittleEndian(source[9..13]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[13..15])
        );
    }
}