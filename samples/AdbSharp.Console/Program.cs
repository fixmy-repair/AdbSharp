using AdbSharp;
using AdbSharp.Adb;
using AdbSharp.Authentication.Adb;
using AdbSharp.Common;
using AdbSharp.Common.Devices;
using AdbSharp.Discovery;
using AdbSharp.Fastboot;
using AdbSharp.Fastbootd;
using AdbSharp.Transport.Usb;
using System.Text.Json;

if (args.Length == 2 && string.Equals(args[0], "pairing-compat", StringComparison.OrdinalIgnoreCase))
{
    await RunPairingCompatibilityAsync(args[1]);
    return;
}

if (args.Length >= 1 && string.Equals(args[0], "mdns", StringComparison.OrdinalIgnoreCase))
{
    await RunMdnsDiscoveryAsync(args[1..]);
    return;
}

if (args.Length == 1 && string.Equals(args[0], "usb-diagnostics", StringComparison.OrdinalIgnoreCase))
{
    DefaultTransportProvider.RegisterBuiltInTransports();
    var diagnostics = await UsbTransportRegistry.FindWithDiagnosticsAsync();
    foreach (var device in diagnostics.Devices)
    {
        Console.WriteLine($"device\t{device.TransportId}\t{device.VendorId:x4}:{device.ProductId:x4}\tinterface={device.InterfaceNumber}");
    }

    foreach (var issue in diagnostics.Issues)
    {
        Console.WriteLine($"issue\t{issue.EnumeratorName}\t{issue.Error}\t{issue.Message}");
    }

    return;
}

if (args.Length == 1 && string.Equals(args[0], "usb-lock-owners", StringComparison.OrdinalIgnoreCase))
{
    DefaultTransportProvider.RegisterBuiltInTransports();
    foreach (var device in await UsbTransportRegistry.FindAsync())
    {
        Console.WriteLine($"device\t{device.TransportId}\t{device.VendorId:x4}:{device.ProductId:x4}\tinterface={device.InterfaceNumber}");
        await PrintUsbLockOwnersAsync(device);
    }

    return;
}

if (args.Length == 1 && string.Equals(args[0], "usb-open-diagnostics", StringComparison.OrdinalIgnoreCase))
{
    DefaultTransportProvider.RegisterBuiltInTransports();
    foreach (var device in await UsbTransportRegistry.FindAsync())
    {
        Console.WriteLine($"device\t{device.TransportId}\t{device.VendorId:x4}:{device.ProductId:x4}\tinterface={device.InterfaceNumber}");
        var factory = UsbTransportRegistry.FindFactory(device);
        try
        {
            await using var transport = await factory.OpenAsync(device);
            Console.WriteLine($"open\t{transport.GetType().FullName}");
            Console.WriteLine($"bulk-in\t0x{transport.BulkInEndpoint.Address:x2}\t{transport.BulkInEndpoint.MaxPacketSize}");
            Console.WriteLine($"bulk-out\t0x{transport.BulkOutEndpoint.Address:x2}\t{transport.BulkOutEndpoint.MaxPacketSize}");
            if (transport is IUsbTransportDiagnostics transportDiagnostics)
            {
                var snapshot = transportDiagnostics.GetDiagnosticSnapshot();
                Console.WriteLine($"diagnostics\t{snapshot.Backend}\t{snapshot.State}\topen={snapshot.IsOpen}");
                Console.WriteLine($"diagnostics-id\t{snapshot.TransportId}");
                Console.WriteLine($"diagnostics-in\t0x{snapshot.BulkInEndpointAddress:x2}\t{snapshot.BulkInMaxPacketSize}");
                Console.WriteLine($"diagnostics-out\t0x{snapshot.BulkOutEndpointAddress:x2}\t{snapshot.BulkOutMaxPacketSize}");
                foreach (var pair in snapshot.Properties.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine($"diagnostics-property\t{pair.Key}={pair.Value}");
                }
            }
            else
            {
                Console.WriteLine("diagnostics-unavailable");
            }
        }
        catch (UsbTransportException ex)
        {
            Console.WriteLine($"open-error\t{ex.Error}\t{ex.Message}");
            if (UsbDeviceLockConflictHandler.IsLockLikeFailure(ex))
            {
                await PrintUsbLockOwnersAsync(device, ex);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"open-error\t{ex.GetType().FullName}\t{ex.Message}");
        }
    }

    return;
}

if (args.Length >= 3 && string.Equals(args[0], "tcp-shell", StringComparison.OrdinalIgnoreCase))
{
    var (host, port) = ParseTcpEndpoint(args[1]);
    var command = string.Join(' ', args.Skip(2));
    using var authenticator = await CreateDefaultAdbAuthenticatorAsync();
    await using var client = await AdbClient.ConnectTcpAsync(host, port, new AdbClientOptions { Authenticator = authenticator });
    Console.Write(await client.ShellAsync(command));
    return;
}

if (args.Length == 2 && string.Equals(args[0], "tcp-getprop", StringComparison.OrdinalIgnoreCase))
{
    var (host, port) = ParseTcpEndpoint(args[1]);
    using var authenticator = await CreateDefaultAdbAuthenticatorAsync();
    await using var client = await AdbClient.ConnectTcpAsync(host, port, new AdbClientOptions { Authenticator = authenticator });
    Console.Write(await client.ShellAsync("getprop"));
    return;
}

var devices = await DeviceManager.FindAsync();
if (devices.Count == 0)
{
    Console.WriteLine("No Android USB devices were discovered.");
    return;
}

foreach (var device in devices)
{
    Console.WriteLine($"{device.Identity.SerialNumber ?? device.Identity.TransportId}\t{device.Mode}\t{device.Identity.Product}");
}

var adbDevice = devices.FirstOrDefault(static device => device.Mode is DeviceMode.Adb or DeviceMode.Recovery or DeviceMode.Sideload);
var fastbootDevice = devices.FirstOrDefault(static device => device.Mode is DeviceMode.Fastboot or DeviceMode.Fastbootd or DeviceMode.Bootloader);
if (args.Length >= 2 && string.Equals(args[0], "shell", StringComparison.OrdinalIgnoreCase))
{
    var command = string.Join(' ', args.Skip(1));
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    try
    {
        using var authenticator = await CreateDefaultAdbAuthenticatorAsync();
        await using var client = await AdbClient.ConnectAsync(adbDevice, new AdbClientOptions { Authenticator = authenticator });
        Console.Write(await client.ShellAsync(command));
    }
    catch (UsbTransportException ex)
    {
        Console.Error.WriteLine($"{ex.Error}: {ex.Message}");
        Environment.ExitCode = 1;
    }
    catch (DeviceConnectionException ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
}
else if (args.Length >= 2 && string.Equals(args[0], "shell2", StringComparison.OrdinalIgnoreCase))
{
    var command = string.Join(' ', args.Skip(1));
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    var result = await client.ShellV2Async(command);
    Console.Write(result.StandardOutput);
    Console.Error.Write(result.StandardError);
    Environment.ExitCode = result.ExitCode ?? 1;
}
else if (args.Length >= 1 && string.Equals(args[0], "packages", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    foreach (var package in await client.ListPackagesAsync(args.ElementAtOrDefault(1)))
    {
        Console.WriteLine(package);
    }
}
else if (args.Length >= 2 && string.Equals(args[0], "install", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    var result = await InstallPackageFilesAsync(client, args[1..], staged: false);
    Console.WriteLine(result.Output);
}
else if (args.Length >= 2 && string.Equals(args[0], "install-staged", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    var result = await InstallPackageFilesAsync(client, args[1..], staged: true);
    Console.WriteLine(result.Output);
}
else if (args.Length >= 1 && string.Equals(args[0], "logcat", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    await foreach (var line in client.LogcatAsync(string.Join(' ', args.Skip(1))))
    {
        Console.WriteLine(line);
    }
}
else if (args.Length == 2 && string.Equals(args[0], "stat", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    var stat = await client.StatAsync(args[1]);
    if (stat is null)
    {
        Console.Error.WriteLine("Path not found.");
        return;
    }

    Console.WriteLine($"{stat.Mode:x}\t{stat.Size}\t{stat.ModifiedTime:u}\t{stat.Path}");
}
else if (args.Length == 2 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    foreach (var entry in await client.ListDirectoryAsync(args[1]))
    {
        Console.WriteLine($"{entry.Statistics.Mode:x}\t{entry.Statistics.Size}\t{entry.Name}");
    }
}
else if (args.Length == 3 && string.Equals(args[0], "push", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    await using var source = File.OpenRead(args[1]);
    await client.PushAsync(source, args[2]);
}
else if (args.Length == 3 && string.Equals(args[0], "pull", StringComparison.OrdinalIgnoreCase))
{
    if (adbDevice is null)
    {
        Console.Error.WriteLine("No ADB-capable device was discovered.");
        return;
    }

    await using var client = await AdbClient.ConnectAsync(adbDevice);
    await using var destination = File.Create(args[2]);
    await client.PullAsync(args[1], destination);
}
else if (args.Length == 2 && string.Equals(args[0], "fb-getvar", StringComparison.OrdinalIgnoreCase))
{
    if (fastbootDevice is null)
    {
        Console.Error.WriteLine("No Fastboot-capable device was discovered.");
        return;
    }

    await using var client = await FastbootClient.ConnectAsync(fastbootDevice);
    Console.WriteLine(await client.GetVarAsync(args[1]));
}
else if (args.Length == 3 && string.Equals(args[0], "fb-fetch", StringComparison.OrdinalIgnoreCase))
{
    if (fastbootDevice is null)
    {
        Console.Error.WriteLine("No Fastboot-capable device was discovered.");
        return;
    }

    await using var client = await FastbootClient.ConnectAsync(fastbootDevice);
    await using var output = File.Create(args[2]);
    await client.FetchPartitionAsync(args[1], output);
}
else if (args.Length == 3 && string.Equals(args[0], "fb-flash", StringComparison.OrdinalIgnoreCase))
{
    if (fastbootDevice is null)
    {
        Console.Error.WriteLine("No Fastboot-capable device was discovered.");
        return;
    }

    await using var client = await FastbootClient.ConnectAsync(fastbootDevice);
    await using var image = File.OpenRead(args[2]);
    await client.FlashPartitionAsync(args[1], image);
}
else if (args.Length == 3 && string.Equals(args[0], "fb-flash-sparse", StringComparison.OrdinalIgnoreCase))
{
    if (fastbootDevice is null)
    {
        Console.Error.WriteLine("No Fastboot-capable device was discovered.");
        return;
    }

    await using var client = await FastbootClient.ConnectAsync(fastbootDevice);
    await using var image = File.OpenRead(args[2]);
    var info = await client.FlashSparsePartitionAsync(args[1], image);
    Console.WriteLine($"{info.Header.TotalChunks} chunks, {info.ExpandedLength} expanded bytes");
}
else if (args.Length == 1 && string.Equals(args[0], "fbd-capabilities", StringComparison.OrdinalIgnoreCase))
{
    if (fastbootDevice is null)
    {
        Console.Error.WriteLine("No Fastboot-capable device was discovered.");
        return;
    }

    await using var client = await FastbootdClient.ConnectAsync(fastbootDevice);
    Console.WriteLine($"userspace={client.Capabilities.IsUserspace}");
    Console.WriteLine($"dynamic-partitions={client.Capabilities.SupportsDynamicPartitions}");
    Console.WriteLine($"logical-partitions={client.Capabilities.SupportsLogicalPartitions}");
    Console.WriteLine($"snapshot-updates={client.Capabilities.SupportsSnapshotUpdates}");
    Console.WriteLine($"virtual-ab={client.Capabilities.SupportsVirtualAb}");
    Console.WriteLine($"super={client.Capabilities.SuperPartitionName ?? string.Empty}");
    Console.WriteLine($"snapshot-status={client.Capabilities.SnapshotUpdateStatus ?? string.Empty}");
}
else if (args.Length == 3 && string.Equals(args[0], "fb-unified-flash", StringComparison.OrdinalIgnoreCase))
{
    if (fastbootDevice is null)
    {
        Console.Error.WriteLine("No Fastboot-capable device was discovered.");
        return;
    }

    await using var image = File.OpenRead(args[2]);
    var result = await UnifiedFastbootFlasher.FlashPartitionAsync(fastbootDevice, args[1], image);
    Console.WriteLine($"{result.Partition} flashed via {(result.UsedFastbootd ? "fastbootd" : "fastboot")}");
}
else if (args.Length == 3 && string.Equals(args[0], "fb-unified-flash-sparse", StringComparison.OrdinalIgnoreCase))
{
    if (fastbootDevice is null)
    {
        Console.Error.WriteLine("No Fastboot-capable device was discovered.");
        return;
    }

    await using var image = File.OpenRead(args[2]);
    var result = await UnifiedFastbootFlasher.FlashSparsePartitionAsync(fastbootDevice, args[1], image);
    Console.WriteLine($"{result.Partition} flashed via {(result.UsedFastbootd ? "fastbootd" : "fastboot")}");
    Console.WriteLine($"{result.SparseImage?.Header.TotalChunks ?? 0} chunks, {result.SparseImage?.ExpandedLength ?? 0} expanded bytes");
}

static (string Host, int Port) ParseTcpEndpoint(string endpoint)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
    const int defaultPort = 5555;

    if (endpoint[0] == '[')
    {
        var closeBracket = endpoint.IndexOf(']', StringComparison.Ordinal);
        if (closeBracket <= 1)
        {
            throw new ArgumentException("Bracketed wireless endpoints must use [host] or [host]:port.", nameof(endpoint));
        }

        var host = endpoint[1..closeBracket];
        if (closeBracket == endpoint.Length - 1)
        {
            return (host, defaultPort);
        }

        if (endpoint[closeBracket + 1] == ':' && int.TryParse(endpoint[(closeBracket + 2)..], out var bracketPort))
        {
            return (host, bracketPort);
        }

        throw new ArgumentException("Bracketed wireless endpoints must use [host] or [host]:port.", nameof(endpoint));
    }

    var separator = endpoint.LastIndexOf(':', StringComparison.Ordinal);
    if (separator > 0 && endpoint.IndexOf(':', StringComparison.Ordinal) == separator)
    {
        if (int.TryParse(endpoint[(separator + 1)..], out var port))
        {
            return (endpoint[..separator], port);
        }

        throw new ArgumentException("Wireless endpoints must use host or host:port.", nameof(endpoint));
    }

    return (endpoint, defaultPort);
}

static async ValueTask PrintUsbLockOwnersAsync(UsbDeviceDescriptor device, UsbTransportException? openFailure = null)
{
    var resolution = await UsbDeviceLockOwnerResolverRegistry.ResolveAsync(device, openFailure);
    Console.WriteLine($"lock-resolution\t{resolution.Status}\t{resolution.DevicePath ?? "-"}\t{resolution.Message ?? "-"}");
    foreach (var owner in resolution.Owners)
    {
        Console.WriteLine(
            $"lock-owner\tpid={owner.ProcessId}\tname={owner.ProcessName ?? "-"}\tkind={owner.Kind}\tconfidence={owner.Confidence}\tpath={owner.ExecutablePath ?? "-"}\tevidence={owner.Evidence ?? "-"}");
    }
}

static async ValueTask<AdbRsaAuthenticator> CreateDefaultAdbAuthenticatorAsync(CancellationToken cancellationToken = default)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (string.IsNullOrWhiteSpace(home))
    {
        throw new InvalidOperationException("Cannot locate the current user's home directory for the default ADB key.");
    }

    var key = await AdbKeyStore.LoadOrCreateAsync(Path.Combine(home, ".android", "adbkey"), cancellationToken);
    return new AdbRsaAuthenticator(key);
}

static async ValueTask<AdbPackageInstallResult> InstallPackageFilesAsync(AdbClient client, IReadOnlyList<string> paths, bool staged)
{
    var streams = new List<FileStream>(paths.Count);
    try
    {
        var packages = new List<AdbPackageFile>(paths.Count);
        foreach (var path in paths)
        {
            var stream = File.OpenRead(path);
            streams.Add(stream);
            packages.Add(new AdbPackageFile(stream, Path.GetFileName(path)));
        }

        return await client.InstallPackagesAsync(
            packages,
            new AdbInstallOptions
            {
                Staged = staged,
                StagedReadyTimeout = staged ? TimeSpan.Zero : null
            });
    }
    finally
    {
        foreach (var stream in streams)
        {
            await stream.DisposeAsync();
        }
    }
}

static async ValueTask RunMdnsDiscoveryAsync(IReadOnlyList<string> serviceKinds, CancellationToken cancellationToken = default)
{
    var options = new AdbMdnsDiscoveryOptions
    {
        ServiceKinds = serviceKinds.Count == 0 ? null : serviceKinds.Select(ParseMdnsServiceKind).ToArray()
    };
    var services = await AdbMdnsDiscovery.FindAsync(options, cancellationToken);
    foreach (var service in services)
    {
        Console.WriteLine(
            $"{service.Kind}\t{service.InstanceName}\t{service.Host}\t{service.Port}\t{service.ServiceType}\t{service.TargetHost}");
    }
}

static AdbMdnsServiceKind ParseMdnsServiceKind(string value)
{
    return value.ToUpperInvariant() switch
    {
        "ADB" or "LEGACY" or "LEGACY-ADB" => AdbMdnsServiceKind.LegacyAdb,
        "PAIR" or "PAIRING" or "ADB-TLS-PAIRING" => AdbMdnsServiceKind.Pairing,
        "CONNECT" or "TLS-CONNECT" or "ADB-TLS-CONNECT" => AdbMdnsServiceKind.Connect,
        _ => throw new ArgumentException("mDNS service kind must be adb, pairing, or connect.", nameof(value))
    };
}

static async ValueTask RunPairingCompatibilityAsync(string matrixPath, CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(matrixPath);

    var matrix = await LoadPairingCompatibilityMatrixAsync(matrixPath, cancellationToken);
    if (matrix.Endpoints.Count == 0)
    {
        throw new InvalidOperationException("The pairing compatibility matrix does not contain any endpoints.");
    }

    using var keyPair = await AdbKeyStore.LoadOrCreateAsync(ResolveAdbKeyPath(matrix.KeyPath), cancellationToken);
    var options = new AdbPairingCompatibilityOptions
    {
        PairingOptions = new AdbPairingOptions { PublicKeyComment = matrix.PublicKeyComment },
        VerifyAdbConnection = matrix.VerifyAdbConnection,
        DefaultAdbPort = matrix.DefaultAdbPort
    };

    var results = new List<AdbPairingCompatibilityResult>(matrix.Endpoints.Count);
    foreach (var endpoint in matrix.Endpoints)
    {
        var result = await AdbPairingCompatibilityValidator.ValidateAsync(endpoint, keyPair, options, cancellationToken);
        results.Add(result);
        var status = result.IsCompatible ? "PASS" : "FAIL";
        Console.Error.WriteLine($"{status}\t{result.Vendor}\t{result.Model}\t{result.Host}:{result.PairingPort}");
    }

    var compatibleVendorCount = results
        .Where(static result => result.IsCompatible)
        .Select(static result => result.Vendor)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    var report = new PairingCompatibilityReport(
        DateTimeOffset.UtcNow,
        matrix.Endpoints.Count,
        results.Count(static result => result.IsCompatible),
        compatibleVendorCount,
        matrix.MinimumVendorCount,
        results.All(static result => result.IsCompatible) && compatibleVendorCount >= matrix.MinimumVendorCount,
        results);

    Console.WriteLine(JsonSerializer.Serialize(report, SampleJson.Options));
    Environment.ExitCode = report.Passed ? 0 : 2;
}

static async ValueTask<PairingCompatibilityMatrix> LoadPairingCompatibilityMatrixAsync(string matrixPath, CancellationToken cancellationToken)
{
    var json = await File.ReadAllTextAsync(ExpandPath(matrixPath), cancellationToken);
    var matrix = DeserializePairingCompatibilityMatrix(json);
    matrix.Endpoints ??= [];
    if (matrix.MinimumVendorCount < 1)
    {
        throw new InvalidOperationException("minimumVendorCount must be at least 1.");
    }

    if (matrix.DefaultAdbPort is < 1 or > 65535)
    {
        throw new InvalidOperationException("defaultAdbPort must be a TCP port from 1 through 65535.");
    }

    return matrix;
}

static PairingCompatibilityMatrix DeserializePairingCompatibilityMatrix(string json)
{
    var trimmed = json.AsSpan().TrimStart();
    if (!trimmed.IsEmpty && trimmed[0] == '[')
    {
        return new PairingCompatibilityMatrix
        {
            Endpoints = JsonSerializer.Deserialize<List<AdbPairingCompatibilityEndpoint>>(json, SampleJson.WebOptions) ?? []
        };
    }

    return JsonSerializer.Deserialize<PairingCompatibilityMatrix>(json, SampleJson.WebOptions)
        ?? throw new InvalidOperationException("The pairing compatibility matrix could not be parsed.");
}

static string ResolveAdbKeyPath(string? keyPath)
{
    if (!string.IsNullOrWhiteSpace(keyPath))
    {
        return ExpandPath(keyPath);
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (string.IsNullOrWhiteSpace(home))
    {
        throw new InvalidOperationException("Cannot locate the current user's home directory for the default ADB key.");
    }

    return Path.Combine(home, ".android", "adbkey");
}

static string ExpandPath(string path)
{
    if (path.StartsWith("~/", StringComparison.Ordinal))
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Cannot expand '~' because the current user's home directory was not found.");
        }

        return Path.Combine(home, path[2..]);
    }

    return path;
}

file sealed class PairingCompatibilityMatrix
{
    public string? KeyPath { get; set; }

    public string PublicKeyComment { get; set; } = "AdbSharp";

    public bool VerifyAdbConnection { get; set; } = true;

    public int DefaultAdbPort { get; set; } = 5555;

    public int MinimumVendorCount { get; set; } = 1;

    public List<AdbPairingCompatibilityEndpoint> Endpoints { get; set; } = [];
}

file sealed record PairingCompatibilityReport(
    DateTimeOffset CreatedUtc,
    int EndpointCount,
    int CompatibleEndpointCount,
    int CompatibleVendorCount,
    int MinimumVendorCount,
    bool Passed,
    IReadOnlyList<AdbPairingCompatibilityResult> Results);

file static class SampleJson
{
    public static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
