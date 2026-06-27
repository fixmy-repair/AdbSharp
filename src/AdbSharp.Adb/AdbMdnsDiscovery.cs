using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AdbSharp.Adb.Internal;
using AdbSharp.Common;

namespace AdbSharp.Adb;

/// <summary>
/// Discovers Android Debug Bridge services advertised through mDNS/DNS-SD.
/// </summary>
public static class AdbMdnsDiscovery
{
    private static readonly IPAddress MdnsIpv4Address = IPAddress.Parse(AdbMdnsProtocol.MdnsIpv4Address);
    private static readonly IPAddress MdnsIpv6Address = IPAddress.Parse(AdbMdnsProtocol.MdnsIpv6Address);

    /// <summary>
    /// Queries the local network for Android ADB mDNS services.
    /// </summary>
    /// <param name="options">Discovery options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The discovered ADB mDNS services.</returns>
    public static async ValueTask<IReadOnlyList<AdbMdnsService>> FindAsync(
        AdbMdnsDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AdbMdnsDiscoveryOptions();
        ValidateOptions(options);
        var serviceKinds = GetServiceKinds(options);
        var query = AdbMdnsProtocol.CreateQuery(serviceKinds);
        var services = new ConcurrentDictionary<string, AdbMdnsService>(StringComparer.OrdinalIgnoreCase);
        var sockets = CreateSockets(options);
        if (sockets.Count == 0)
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        var receiveTasks = sockets
            .Select(socket => ReceiveLoopAsync(socket.Client, services, timeout.Token))
            .ToArray();

        try
        {
            await SendQueriesAsync(sockets, query, timeout.Token).ConfigureAwait(false);
            var secondQueryDelay = TimeSpan.FromMilliseconds(Math.Min(750, options.Timeout.TotalMilliseconds / 2));
            if (secondQueryDelay > TimeSpan.Zero)
            {
                await Task.Delay(secondQueryDelay, timeout.Token).ConfigureAwait(false);
                await SendQueriesAsync(sockets, query, timeout.Token).ConfigureAwait(false);
            }

            await Task.Delay(options.Timeout, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        finally
        {
            await timeout.CancelAsync().ConfigureAwait(false);
            await WaitForReceiveLoopsAsync(receiveTasks).ConfigureAwait(false);
            foreach (var socket in sockets)
            {
                socket.Dispose();
            }
        }

        return [.. services.Values
            .OrderBy(static service => service.Kind)
            .ThenBy(static service => service.InstanceName, StringComparer.OrdinalIgnoreCase)];
    }

    private static async ValueTask ReceiveLoopAsync(
        UdpClient client,
        ConcurrentDictionary<string, AdbMdnsService> services,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            IReadOnlyList<AdbMdnsService> parsed;
            try
            {
                parsed = AdbMdnsProtocol.ParseResponse(result.Buffer);
            }
            catch (ProtocolException)
            {
                continue;
            }

            foreach (var service in parsed)
            {
                services.AddOrUpdate(CreateKey(service), service, (_, existing) => Merge(existing, service));
            }
        }
    }

    private static async ValueTask SendQueriesAsync(
        IReadOnlyList<MdnsSocket> sockets,
        ReadOnlyMemory<byte> query,
        CancellationToken cancellationToken)
    {
        foreach (var socket in sockets)
        {
            try
            {
                await socket.Client.SendAsync(query, socket.EndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
            }
        }
    }

    private static IReadOnlyList<MdnsSocket> CreateSockets(AdbMdnsDiscoveryOptions options)
    {
        var sockets = new List<MdnsSocket>();
        if (options.IncludeIPv4)
        {
            AddSocket(sockets, CreateIpv4Socket, new IPEndPoint(MdnsIpv4Address, AdbMdnsProtocol.MdnsPort));
        }

        if (options.IncludeIPv6)
        {
            foreach (var interfaceIndex in GetIpv6MulticastInterfaceIndexes())
            {
                AddSocket(
                    sockets,
                    () => CreateIpv6Socket(interfaceIndex),
                    new IPEndPoint(new IPAddress(MdnsIpv6Address.GetAddressBytes(), interfaceIndex), AdbMdnsProtocol.MdnsPort));
            }
        }

        return sockets;
    }

    private static void AddSocket(List<MdnsSocket> sockets, Func<UdpClient> createSocket, IPEndPoint endPoint)
    {
        try
        {
            sockets.Add(new MdnsSocket(createSocket(), endPoint));
        }
        catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException or ObjectDisposedException)
        {
        }
    }

    private static UdpClient CreateIpv4Socket()
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        return client;
    }

    private static UdpClient CreateIpv6Socket(long interfaceIndex)
    {
        var client = new UdpClient(AddressFamily.InterNetworkV6);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 255);
        client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, checked((int)interfaceIndex));
        client.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        return client;
    }

    private static IEnumerable<long> GetIpv6MulticastInterfaceIndexes()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || !networkInterface.SupportsMulticast
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || !networkInterface.Supports(NetworkInterfaceComponent.IPv6))
            {
                continue;
            }

            var index = networkInterface.GetIPProperties().GetIPv6Properties()?.Index;
            if (index > 0)
            {
                yield return index.Value;
            }
        }
    }

    private static async ValueTask WaitForReceiveLoopsAsync(IReadOnlyList<ValueTask> receiveTasks)
    {
        foreach (var task in receiveTasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static AdbMdnsService Merge(AdbMdnsService existing, AdbMdnsService candidate)
    {
        var addresses = existing.Addresses
            .Concat(candidate.Addresses)
            .Distinct()
            .ToArray();
        var txtRecords = new Dictionary<string, string>(existing.TxtRecords, StringComparer.OrdinalIgnoreCase);
        foreach (var item in candidate.TxtRecords)
        {
            txtRecords[item.Key] = item.Value;
        }

        return new AdbMdnsService(
            existing.InstanceName,
            existing.ServiceType,
            existing.Kind,
            existing.TargetHost,
            existing.Port,
            addresses,
            txtRecords);
    }

    private static IReadOnlyList<AdbMdnsServiceKind> GetServiceKinds(AdbMdnsDiscoveryOptions options)
    {
        return options.ServiceKinds is { Count: > 0 }
            ? options.ServiceKinds.ToArray()
            : [AdbMdnsServiceKind.LegacyAdb, AdbMdnsServiceKind.Pairing, AdbMdnsServiceKind.Connect];
    }

    private static string CreateKey(AdbMdnsService service)
    {
        return $"{service.Kind}|{service.InstanceName}|{service.TargetHost}|{service.Port}";
    }

    private static void ValidateOptions(AdbMdnsDiscoveryOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "mDNS discovery timeout must be greater than zero.");
        }

        if (!options.IncludeIPv4 && !options.IncludeIPv6)
        {
            throw new ArgumentException("At least one IP version must be enabled for mDNS discovery.", nameof(options));
        }
    }

    private sealed record MdnsSocket(UdpClient Client, IPEndPoint EndPoint) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
        }
    }
}
