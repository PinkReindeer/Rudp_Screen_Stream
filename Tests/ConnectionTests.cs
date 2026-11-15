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
        server.Start();

        using var client = new TcpControlClient();

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

        IPEndPoint? clientUdpEndpoint = null;
        server.ClientConnected += clientUdpEp => clientUdpEndpoint = clientUdpEp;
        server.Start();

        using var receiver = new UdpFrameReceiver(udpPort);
        using var client = new TcpControlClient();

        await client.ConnectAsync("127.0.0.1", tcpPort, (ushort)udpPort);
        Assert.IsTrue(client.IsConnected);
        Assert.IsNotNull(clientUdpEndpoint);

        using var bmp = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.CornflowerBlue);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        byte[] jpegBytes = ms.ToArray();

        await sender.SendFrameAsync(jpegBytes, clientUdpEndpoint);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var completedFrame = await receiver.CompletedFrames.ReadAsync(cts.Token);

        Assert.IsNotNull(completedFrame.PooledBuffer);
        Assert.AreEqual(jpegBytes.Length, completedFrame.Length);
        completedFrame.Dispose();
    }

    [TestMethod]
    public async Task Test_HostAndViewer_TcpStreamTransfer()
    {
        int tcpStreamPort = 19010;
        using var sender = new TcpStreamSender();
        sender.Start(tcpStreamPort);

        using var receiver = new TcpStreamReceiver();
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await receiver.ConnectAsync("127.0.0.1", tcpStreamPort, connectCts.Token);
        Assert.IsTrue(receiver.IsConnected);

        await Task.Delay(50);
        Assert.IsTrue(sender.HasClient);

        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        await sender.SendFrameAsync(payload);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var (data, length, _) = await receiver.CompletedFrames.ReadAsync(readCts.Token);

        Assert.AreEqual(payload.Length, length);
        CollectionAssert.AreEqual(payload, data[..length]);
    }

    [TestMethod]
    public async Task Test_HostAndViewer_SwitchModeSignaling()
    {
        int tcpPort = 19011;
        int udpPort = 19012;

        using var server = new TcpControlServer(tcpPort);
        server.Start();

        using var client = new TcpControlClient();

        byte lastModeReceived = 0xFF;
        var modeTcs = new TaskCompletionSource<byte>();
        server.SwitchModeRequested += mode =>
        {
            lastModeReceived = mode;
            modeTcs.TrySetResult(mode);
        };

        await client.ConnectAsync("127.0.0.1", tcpPort, (ushort)udpPort);
        Assert.IsTrue(client.IsConnected);

        await client.SendSwitchModeAsync(0x01);
        var completed = await Task.WhenAny(modeTcs.Task, Task.Delay(2000));
        Assert.AreEqual(modeTcs.Task, completed);
        Assert.AreEqual((byte)0x01, lastModeReceived);

        modeTcs = new TaskCompletionSource<byte>();
        await client.SendSwitchModeAsync(0x00);
        completed = await Task.WhenAny(modeTcs.Task, Task.Delay(2000));
        Assert.AreEqual(modeTcs.Task, completed);
        Assert.AreEqual((byte)0x00, lastModeReceived);
    }
}
