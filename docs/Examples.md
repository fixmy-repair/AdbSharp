# Examples

## List Devices

```csharp
var devices = await DeviceManager.FindAsync();
foreach (var device in devices)
{
    Console.WriteLine($"{device.Identity.SerialNumber}\t{device.Mode}");
}
```

## USB Discovery Diagnostics

```csharp
DefaultTransportProvider.RegisterBuiltInTransports();
var result = await UsbTransportRegistry.FindWithDiagnosticsAsync();
foreach (var issue in result.Issues)
{
    Console.WriteLine($"{issue.EnumeratorName}: {issue.Error} {issue.Message}");
}
```

## Run Shell

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
var output = await adb.ShellAsync("getprop ro.product.model");
Console.WriteLine(output);
```

## Wireless ADB Shell

```csharp
var keyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".android",
    "adbkey");
using var authenticator = new AdbRsaAuthenticator(await AdbKeyStore.LoadOrCreateAsync(keyPath));

await using var adb = await AdbClient.ConnectWirelessAsync(
    "192.168.1.42",
    options: new AdbClientOptions { Authenticator = authenticator });
var output = await adb.ShellAsync("getprop ro.product.model");
Console.WriteLine(output);
```

## Wireless Pairing

```csharp
var keyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".android",
    "adbkey");
using var keyPair = await AdbKeyStore.LoadOrCreateAsync(keyPath);
var pairingService = (await AdbMdnsDiscovery.FindAsync(new AdbMdnsDiscoveryOptions
{
    ServiceKinds = [AdbMdnsServiceKind.Pairing]
})).First();

var result = await AdbPairingClient.PairAsync(
    pairingService,
    pairingCode,
    keyPair);

Console.WriteLine(result.PeerInfo.Kind);
```

## Wireless mDNS Discovery

```csharp
var services = await AdbMdnsDiscovery.FindAsync();
foreach (var service in services)
{
    Console.WriteLine($"{service.Kind}\t{service.InstanceName}\t{service.Host}:{service.Port}");
}
```

```sh
dotnet run --project samples/AdbSharp.Console -- mdns connect
```

## Wireless Pairing Compatibility Report

```sh
dotnet run --project samples/AdbSharp.Console -- pairing-compat pairing-matrix.json
```

The matrix contains fresh Android 11+ pairing endpoints and can require a minimum number of compatible vendors:

```json
{
  "minimumVendorCount": 2,
  "verifyAdbConnection": true,
  "endpoints": [
    {
      "vendor": "Google",
      "model": "Pixel 8",
      "host": "192.168.1.42",
      "pairingPort": 37123,
      "pairingCode": "123456",
      "adbPort": 39211
    }
  ]
}
```

## Run Shell V2

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
var result = await adb.ShellV2Async("cmd package list packages android");
Console.Write(result.StandardOutput);
Console.Error.Write(result.StandardError);
Console.WriteLine(result.ExitCode);
```

## Stream Logcat

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
await foreach (var line in adb.LogcatAsync("-d"))
{
    Console.WriteLine(line);
}
```

## Forward A Local Port

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
await using var forward = await adb.StartPortForwardAsync(0, AdbSocketSpec.Tcp(8080));
Console.WriteLine(forward.LocalEndPoint);
```

## Reverse Forward

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
await adb.ReverseForwardAsync(AdbSocketSpec.Tcp(7000), AdbSocketSpec.Tcp(8000));
```

## Package Helpers

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
var packages = await adb.ListPackagesAsync("com.example");
await adb.ClearPackageDataAsync("com.example.app");
await adb.UninstallPackageAsync("com.example.app");
```

## Install Split APKs

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
await using var baseApk = File.OpenRead("base.apk");
await using var configApk = File.OpenRead("config.en.apk");

var result = await adb.InstallPackagesAsync(
    [
        new AdbPackageFile(baseApk, "base.apk"),
        new AdbPackageFile(configApk, "config.en.apk")
    ],
    new AdbInstallOptions { GrantRuntimePermissions = true });

Console.WriteLine(result.Output);
```

## Staged Install

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
await using var apk = File.OpenRead("module.apk");

var result = await adb.InstallStagedPackagesAsync(
    [new AdbPackageFile(apk, "base.apk")],
    new AdbInstallOptions
    {
        EnableRollback = true,
        StagedReadyTimeout = TimeSpan.Zero
    });

Console.WriteLine(result.Output);
```

## Push And Pull

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
await using var input = File.OpenRead("local.txt");
await adb.PushAsync(input, "/data/local/tmp/local.txt");

await using var output = File.Create("copy.txt");
await adb.PullAsync("/data/local/tmp/local.txt", output);
```

## File Metadata And Directory Listing

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
var stat = await adb.StatAsync("/data/local/tmp/local.txt");
if (stat is not null)
{
    Console.WriteLine($"{stat.Size} {stat.ModifiedTime:u}");
}

foreach (var entry in await adb.ListDirectoryAsync("/data/local/tmp"))
{
    Console.WriteLine($"{entry.Name} {entry.Statistics.Mode:x} {entry.Statistics.Size}");
}
```

## Flash Boot Image

```csharp
await using var fastboot = await FastbootClient.ConnectAsync(device);
await using var image = File.OpenRead("boot.img");
await fastboot.FlashPartitionAsync("boot", image);
await fastboot.RebootAsync();
```

## Flash Sparse Image

```csharp
await using var fastboot = await FastbootClient.ConnectAsync(device);
await using var image = File.OpenRead("system.img");
var info = await fastboot.FlashSparsePartitionAsync("system", image);
Console.WriteLine($"{info.Header.TotalChunks} chunks, {info.ExpandedLength} expanded bytes");
```

## Fetch Partition

```csharp
await using var fastboot = await FastbootClient.ConnectAsync(device);
await using var output = File.Create("boot.img");
await fastboot.FetchPartitionAsync("boot", output);
```

## Logical Partition Command

```csharp
await using var fastboot = await FastbootClient.ConnectAsync(device);
await fastboot.ResizeLogicalPartitionAsync("system_a", 2L * 1024 * 1024 * 1024);
```

## Resize Logical Partition

```csharp
await using var fastbootd = await FastbootdClient.ConnectAsync(device, options);
await fastbootd.ResizeLogicalPartitionAsync("system_a", 2L * 1024 * 1024 * 1024);
```

## Detect Fastbootd Capabilities

```csharp
await using var fastbootd = await FastbootdClient.ConnectAsync(device);
Console.WriteLine(fastbootd.Capabilities.IsUserspace);
Console.WriteLine(fastbootd.Capabilities.SuperPartitionName);
```

## Unified Logical Flash

```csharp
await using var image = File.OpenRead("system.img");
var result = await UnifiedFastbootFlasher.FlashPartitionAsync(device, "system_a", image);
Console.WriteLine(result.UsedFastbootd);
```
