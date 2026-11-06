using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Protocol.Control;

/// <summary>
/// Commands sent from ViewerApp (Client) → HostApp (Server) via TCP.
/// Wire format: [1 byte CommandType][1 byte PayloadLen][PayloadLen bytes]
/// </summary>
public enum ControlCommandType : byte
{
    Connect      = 0x01,   // Payload: [2 bytes ushort viewerUdpPort LE] + [UTF-8 targetHostIp]
    StartStream  = 0x02,   // Start sending live screen frames; no payload
    StopStream   = 0x03,   // Stop sending frames; no payload
    SetQuality   = 0x04,   // 1-byte payload: JPEG quality (1–100)
    Disconnect   = 0x05,   // Client signals disconnecting; no payload
    UdpProbeAck  = 0x06,   // Viewer confirms it received the UDP probe packet; no payload
}

/// <summary>
/// Responses sent from HostApp (Server) → ViewerApp (Client) via TCP.
/// Wire format: [1 byte ResponseType][1 byte PayloadLen][PayloadLen bytes]
/// </summary>
public enum ControlResponseType : byte
{
    Ack  = 0x80,   // Byte 0: 0x00 = OK, 0x01 = Error. Followed by optional UTF-8 error message.
    Info = 0x81,   // UTF-8 text payload with status info
}

public static class ControlProtocol
{
    public const int TcpControlPort = 9001;

    // ── Serialise ──────────────────────────────────────────────────────────────

    public static byte[] BuildCommand(ControlCommandType cmd, byte[]? payload = null)
    {
        payload ??= Array.Empty<byte>();
        var buf = new byte[2 + payload.Length];
        buf[0] = (byte)cmd;
        buf[1] = (byte)payload.Length;
        payload.CopyTo(buf, 2);
        return buf;
    }

    public static byte[] BuildResponse(ControlResponseType resp, byte[]? payload = null)
    {
        payload ??= Array.Empty<byte>();
        var buf = new byte[2 + payload.Length];
        buf[0] = (byte)resp;
        buf[1] = (byte)payload.Length;
        payload.CopyTo(buf, 2);
        return buf;
    }

    // ── Convenience helpers ───────────────────────────────────────────────────

    public static byte[] CmdConnect(ushort viewerUdpPort, string targetHostIp)
    {
        byte[] ipBytes = Encoding.UTF8.GetBytes(targetHostIp.Trim());
        int maxIpLen = Math.Min(ipBytes.Length, 200);
        byte[] payload = new byte[2 + maxIpLen];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, viewerUdpPort);
        Array.Copy(ipBytes, 0, payload, 2, maxIpLen);
        return BuildCommand(ControlCommandType.Connect, payload);
    }

    public static (ushort UdpPort, string TargetHostIp) ParseConnect(ReadOnlySpan<byte> payload)
    {
        ushort port = 9000;
        string targetHostIp = "";
        if (payload.Length >= 2)
        {
            port = BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2]);
            if (payload.Length > 2)
            {
                targetHostIp = Encoding.UTF8.GetString(payload[2..]).Trim();
            }
        }
        return (port, targetHostIp);
    }

    public static HashSet<string> GetLocalHostIps()
    {
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1", "localhost", "::1"
        };

        try
        {
            var hostName = Dns.GetHostName();
            var addresses = Dns.GetHostAddresses(hostName);
            foreach (var addr in addresses)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    ips.Add(addr.ToString());
                }
            }
        }
        catch { }

        return ips;
    }

    public static byte[] CmdStart()         => BuildCommand(ControlCommandType.StartStream);
    public static byte[] CmdStop()          => BuildCommand(ControlCommandType.StopStream);
    public static byte[] CmdQuality(byte q) => BuildCommand(ControlCommandType.SetQuality, [q]);
    public static byte[] CmdDisconnect()    => BuildCommand(ControlCommandType.Disconnect);
    public static byte[] CmdUdpProbeAck()   => BuildCommand(ControlCommandType.UdpProbeAck);

    public static byte[] RespAckOk() => BuildResponse(ControlResponseType.Ack, [0x00]);

    public static byte[] RespAckError(string reason)
    {
        byte[] reasonBytes = Encoding.UTF8.GetBytes(reason);
        int maxLen = Math.Min(reasonBytes.Length, 250);
        byte[] payload = new byte[1 + maxLen];
        payload[0] = 0x01; // Error code
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

    // ── Read a single message from a NetworkStream ────────────────────────────

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
