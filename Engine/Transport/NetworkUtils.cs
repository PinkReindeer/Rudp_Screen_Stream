using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Engine.Transport;

public static class NetworkUtils
{
    public static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep &&
                !IPAddress.IsLoopback(ep.Address) &&
                !ep.Address.ToString().StartsWith("169.254."))
            {
                return ep.Address.ToString();
            }
        }
        catch { }

        try
        {
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(ni => ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                .ThenByDescending(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                        ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

            foreach (var ni in activeInterfaces)
            {
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address) &&
                        !addr.Address.ToString().StartsWith("169.254."))
                    {
                        return addr.Address.ToString();
                    }
                }
            }

            var hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (var addr in hostAddresses)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr) &&
                    !addr.ToString().StartsWith("169.254."))
                {
                    return addr.ToString();
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }
}
