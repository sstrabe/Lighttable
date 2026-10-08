using System.Net;
using System.Net.Sockets;

namespace PhotoProcessing.Core.Net;

internal static class HttpHandlers
{
    /// <summary>The handler for talking to the strabix.com servers (Nextcloud, Heimdall).</summary>
    public static SocketsHttpHandler Create() => new() { ConnectCallback = ConnectAnyAddressAsync };

    /// <summary>
    /// Tries each resolved address with a short timeout. .NET otherwise walks the addresses one by one
    /// with the OS connect timeout (21 s on Windows), so a host with a dead IPv6 route — as
    /// cloud.strabix.com has been seen to have from some networks — stalls every new connection.
    /// </summary>
    private static async ValueTask<Stream> ConnectAnyAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (e is SocketException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                socket.Dispose();
                last = e;
            }
        }

        throw new HttpRequestException($"could not connect to {endpoint.Host}:{endpoint.Port} on any of {addresses.Length} address(es)", last);
    }
}
