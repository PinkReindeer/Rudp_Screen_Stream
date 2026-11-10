using System.Buffers.Binary;
using System.Text;

namespace Protocol.Control;

public enum ControlCommandType : byte
{
    Connect     = 0x01,
    StartStream = 0x02,
    StopStream  = 0x03,
    SetQuality  = 0x04,
    Disconnect  = 0x05
}

public enum ControlResponseType : byte
{
    Ack  = 0x80,
    Info = 0x81
}

public static class ControlProtocol
{
    public const int DefaultTcpPort = 9001;

    public static byte[] BuildCommand(ControlCommandType cmd, byte[]? payload = null)
    {
        payload ??= [];
        var buf = new byte[2 + payload.Length];
        buf[0] = (byte)cmd;
        buf[1] = (byte)payload.Length;
        payload.CopyTo(buf, 2);
        return buf;
    }

    public static byte[] BuildResponse(ControlResponseType resp, byte[]? payload = null)
    {
        payload ??= [];
        var buf = new byte[2 + payload.Length];
        buf[0] = (byte)resp;
        buf[1] = (byte)payload.Length;
        payload.CopyTo(buf, 2);
        return buf;
    }

    public static byte[] CmdConnect(ushort viewerUdpPort)
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, viewerUdpPort);
        return BuildCommand(ControlCommandType.Connect, payload);
    }

    public static ushort ParseConnect(ReadOnlySpan<byte> payload) =>
        payload.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2]) : (ushort)9000;

    public static byte[] CmdStart()         => BuildCommand(ControlCommandType.StartStream);
    public static byte[] CmdStop()          => BuildCommand(ControlCommandType.StopStream);
    public static byte[] CmdQuality(byte q) => BuildCommand(ControlCommandType.SetQuality, [q]);
    public static byte[] CmdDisconnect()    => BuildCommand(ControlCommandType.Disconnect);

    public static byte[] RespAckOk() => BuildResponse(ControlResponseType.Ack, [0x00]);

    public static byte[] RespAckError(string reason)
    {
        byte[] reasonBytes = Encoding.UTF8.GetBytes(reason);
        int maxLen = Math.Min(reasonBytes.Length, 250);
        var payload = new byte[1 + maxLen];
        payload[0] = 0x01;
        Array.Copy(reasonBytes, 0, payload, 1, maxLen);
        return BuildResponse(ControlResponseType.Ack, payload);
    }

    public static (bool Success, string Message) ParseAck(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return (false, "Empty Ack response");
        bool ok = payload[0] == 0x00;
        string msg = ok ? "OK" : (payload.Length > 1 ? Encoding.UTF8.GetString(payload[1..]) : "Error");
        return (ok, msg);
    }

    public static byte[] RespInfo(string text) =>
        BuildResponse(ControlResponseType.Info, Encoding.UTF8.GetBytes(text));

    public static async Task<(byte Type, byte[] Payload)?> ReadMessageAsync(
        System.Net.Sockets.NetworkStream stream, CancellationToken ct = default)
    {
        var header = new byte[2];
        if (!await ReadExactAsync(stream, header, ct)) return null;

        byte type = header[0];
        byte len  = header[1];
        var payload = new byte[len];
        if (len > 0 && !await ReadExactAsync(stream, payload, ct)) return null;

        return (type, payload);
    }

    private static async Task<bool> ReadExactAsync(
        System.Net.Sockets.NetworkStream stream, byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buf.Length)
        {
            int n = await stream.ReadAsync(buf.AsMemory(offset, buf.Length - offset), ct);
            if (n == 0) return false;
            offset += n;
        }
        return true;
    }
}
