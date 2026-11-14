using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using Engine.Transport;

namespace ScreenStream.Tests;

[TestClass]
public class ConnectionTests
{
    [TestMethod]
    public async Task Test_HostAndViewer_ConnectionHandshake()
    {
        int tcpPort = 19001;
        int udpPort = 19000;

        using var server = new TcpControlServer(tcpPort);
        using var sender = new UdpFrameSender();
        server.UdpProbeSender = async ep => await sender.SendProbeAsync(ep);
        server.Start();

        using var receiver = new UdpFrameReceiver(udpPort);
        using var client = new TcpControlClient();

        receiver.ProbeReceived += async _ =>
        {
            await client.SendUdpProbeAckAsync();
        };

        bool clientConnectedEventFired = false;
        client.Connected += () => clientConnectedEventFired = true;

        await client.ConnectAsync("127.0.0.1", tcpPort, (ushort)udpPort);

        Assert.IsTrue(client.IsConnected);
        Assert.IsTrue(clientConnectedEventFired);
        Assert.IsTrue(server.IsClientConnected);
    }

    [TestMethod]
    public async Task Test_HostAndViewer_FullStreamTransfer()
    {
        int tcpPort = 19003;
        int udpPort = 19002;

        using var server = new TcpControlServer(tcpPort);
        using var sender = new UdpFrameSender();
        server.UdpProbeSender = async ep => await sender.SendProbeAsync(ep);

        IPEndPoint? clientUdpEndpoint = null;
        server.ClientConnected += (remoteEp, viewerUdpPort) =>
        {
            var normIp = remoteEp.Address.IsIPv4MappedToIPv6 ? remoteEp.Address.MapToIPv4() : remoteEp.Address;
            clientUdpEndpoint = new IPEndPoint(normIp, viewerUdpPort);
        };

        server.Start();

        using var receiver = new UdpFrameReceiver(udpPort);
        using var client = new TcpControlClient();

        receiver.ProbeReceived += async _ =>
        {
            await client.SendUdpProbeAckAsync();
        };

        await client.ConnectAsync("127.0.0.1", tcpPort, (ushort)udpPort);
        Assert.IsTrue(client.IsConnected);
        Assert.IsNotNull(clientUdpEndpoint);

        // Generate a sample JPEG image to send
        using var bmp = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.CornflowerBlue);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        byte[] jpegBytes = ms.ToArray();

        // Send a frame from sender to viewer
        await sender.SendFrameAsync(jpegBytes, clientUdpEndpoint);

        // Receive frame on viewer
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var completedFrame = await receiver.CompletedFrames.ReadAsync(cts.Token);

        Assert.IsNotNull(completedFrame.PooledBuffer);
        Assert.AreEqual(jpegBytes.Length, completedFrame.Length);
        completedFrame.Dispose();
    }
}
