using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using AdbSharp.Adb.Internal;
using AdbSharp.Adb.Sync;
using AdbSharp.Common;
using AdbSharp.Common.Devices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Adb;

/// <summary>
/// Client for Android Debug Bridge services over native USB or TCP transports.
/// </summary>
public sealed class AdbClient : IAsyncDisposable
{
    private const int DefaultTcpPort = 5555;
    private const int MaxUsbConnectAttempts = 2;
    private const string StatV2Feature = "stat_v2";
    private const string ListV2Feature = "ls_v2";
    private const string SendReceiveV2Feature = "sendrecv_v2";
    private const string SendReceiveV2BrotliFeature = "sendrecv_v2_brotli";
    private const string SendReceiveV2Lz4Feature = "sendrecv_v2_lz4";
    private const string SendReceiveV2ZstdFeature = "sendrecv_v2_zstd";
    private readonly AdbConnection connection;

    private AdbClient(AndroidDevice device, AdbConnection connection)
    {
        Device = device;
        this.connection = connection;
    }

    /// <summary>
    /// Gets the connected device.
    /// </summary>
    public AndroidDevice Device { get; }

    /// <summary>
    /// Connects to an ADB-capable device.
    /// </summary>
    /// <param name="device">The device to connect.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connected ADB client.</returns>
    public static async ValueTask<AdbClient> ConnectAsync(AndroidDevice device, AdbClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Mode is not (DeviceMode.Adb or DeviceMode.Recovery or DeviceMode.Sideload))
        {
            throw new DeviceConnectionException($"Device mode '{device.Mode}' does not expose ADB.");
        }

        options ??= new AdbClientOptions();
        for (var attempt = 1; attempt <= MaxUsbConnectAttempts; attempt++)
        {
            var factory = options.TransportFactory ?? UsbTransportRegistry.FindFactory(device.Usb);
            var transport = await factory.OpenAsync(device.Usb, cancellationToken).ConfigureAwait(false);
            try
            {
                UsbTransportValidator.ValidateOpenedTransport(transport);
            }
            catch
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            var connection = new AdbConnection(new UsbAdbTransport(transport), options);
            try
            {
                await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return new AdbClient(device, connection);
            }
            catch (UsbTransportException ex) when (attempt < MaxUsbConnectAttempts && IsRecoverableUsbConnectFailure(ex))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        throw new DeviceConnectionException("ADB USB connection failed after retrying a recoverable transport failure.");
    }

    /// <summary>
    /// Connects directly to an ADB daemon that is already listening on a TCP endpoint, commonly used for wireless debugging.
    /// </summary>
    /// <param name="host">The device host name or IP address.</param>
    /// <param name="port">The ADB TCP port. The default is 5555.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connected ADB client.</returns>
    public static async ValueTask<AdbClient> ConnectTcpAsync(string host, int port = DefaultTcpPort, AdbClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "TCP ports must be in the range 1 through 65535.");
        }

        options ??= new AdbClientOptions();
        var transport = await TcpAdbTransport.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        var connection = new AdbConnection(transport, options);
        try
        {
            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new AdbClient(CreateTcpDevice(host, port, connection.TlsActive), connection);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Connects directly to an ADB daemon that is already listening for wireless debugging.
    /// </summary>
    /// <param name="host">The device host name or IP address.</param>
    /// <param name="port">The ADB TCP port. The default is 5555.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connected ADB client.</returns>
    public static ValueTask<AdbClient> ConnectWirelessAsync(string host, int port = DefaultTcpPort, AdbClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ConnectTcpAsync(host, port, options, cancellationToken);
    }

    private static bool IsRecoverableUsbConnectFailure(UsbTransportException exception)
    {
        return OperatingSystem.IsMacOS()
            && exception.Error == UsbTransportError.OperationAborted
            && exception.Message.Contains("macOS USB interface", StringComparison.Ordinal);
    }

    /// <summary>
    /// Connects directly to an ADB daemon discovered through mDNS.
    /// </summary>
    /// <param name="service">The discovered ADB connection service.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connected ADB client.</returns>
    public static ValueTask<AdbClient> ConnectWirelessAsync(AdbMdnsService service, AdbClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.Kind is not (AdbMdnsServiceKind.Connect or AdbMdnsServiceKind.LegacyAdb))
        {
            throw new ArgumentException("The mDNS service must be an ADB connection service.", nameof(service));
        }

        return ConnectTcpAsync(service.Host, service.Port, options, cancellationToken);
    }

    /// <summary>
    /// Opens a raw ADB service stream.
    /// </summary>
    /// <param name="service">The service name, such as <c>shell:getprop</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The opened stream.</returns>
    public ValueTask<AdbStream> OpenStreamAsync(string service, CancellationToken cancellationToken = default)
    {
        return connection.OpenStreamAsync(service, cancellationToken);
    }

    /// <summary>
    /// Executes a shell command and returns its output.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The command output.</returns>
    public async ValueTask<string> ShellAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await using var stream = await OpenStreamAsync($"shell:{command}", cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(await stream.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Executes a shell command with the ADB shell v2 protocol and returns stdout, stderr, and the exit code separately.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The shell v2 result.</returns>
    public async ValueTask<AdbShellResult> ShellV2Async(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await using var stream = await OpenStreamAsync($"shell,v2,raw:{command}", cancellationToken).ConfigureAwait(false);
        return await AdbShellV2Protocol.ReadResultAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a command using the ADB exec service and returns its output.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The command output.</returns>
    public async ValueTask<string> ExecAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await using var stream = await OpenStreamAsync($"exec:{command}", cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(await stream.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Reads an Android system property using <c>getprop</c>.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The property value.</returns>
    public async ValueTask<string> GetPropertyAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var output = await ShellAsync($"getprop {name}", cancellationToken).ConfigureAwait(false);
        return output.Trim();
    }

    /// <summary>
    /// Pushes a stream to the device using ADB file sync.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <param name="remotePath">The destination path on the device.</param>
    /// <param name="mode">The Unix file mode.</param>
    /// <param name="progress">Optional byte progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask PushAsync(Stream source, string remotePath, int mode = 0x81a4, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);

        await using var stream = await OpenStreamAsync("sync:", cancellationToken).ConfigureAwait(false);
        await AdbSyncProtocol.PushAsync(stream, source, remotePath, mode, modifiedTime: null, useSendV2: connection.HasFeature(SendReceiveV2Feature), SelectSyncCompression(), progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pushes a stream to the device using ADB file sync with an explicit modification time.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <param name="remotePath">The destination path on the device.</param>
    /// <param name="mode">The Unix file mode.</param>
    /// <param name="modifiedTime">The modification time written to the device.</param>
    /// <param name="progress">Optional byte progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask PushAsync(Stream source, string remotePath, int mode, DateTimeOffset modifiedTime, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);

        await using var stream = await OpenStreamAsync("sync:", cancellationToken).ConfigureAwait(false);
        await AdbSyncProtocol.PushAsync(stream, source, remotePath, mode, modifiedTime, useSendV2: connection.HasFeature(SendReceiveV2Feature), SelectSyncCompression(), progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls a file from the device using ADB file sync.
    /// </summary>
    /// <param name="remotePath">The source path on the device.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="progress">Optional byte progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask PullAsync(string remotePath, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentNullException.ThrowIfNull(destination);

        await using var stream = await OpenStreamAsync("sync:", cancellationToken).ConfigureAwait(false);
        await AdbSyncProtocol.PullAsync(stream, remotePath, destination, connection.HasFeature(SendReceiveV2Feature), SelectSyncCompression(), progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads file metadata without following symbolic links when the connected device supports that distinction.
    /// </summary>
    /// <param name="remotePath">The device path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The file metadata, or <see langword="null" /> when the path does not exist.</returns>
    public async ValueTask<AdbFileStat?> LstatAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        await using var stream = await OpenStreamAsync("sync:", cancellationToken).ConfigureAwait(false);
        return await AdbSyncProtocol.StatAsync(stream, remotePath, followSymlinks: false, connection.HasFeature(StatV2Feature), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads file metadata. On devices without sync stat v2, this falls back to the legacy lstat operation.
    /// </summary>
    /// <param name="remotePath">The device path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The file metadata, or <see langword="null" /> when the path does not exist.</returns>
    public async ValueTask<AdbFileStat?> StatAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        await using var stream = await OpenStreamAsync("sync:", cancellationToken).ConfigureAwait(false);
        return await AdbSyncProtocol.StatAsync(stream, remotePath, followSymlinks: true, connection.HasFeature(StatV2Feature), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists a directory using ADB file sync.
    /// </summary>
    /// <param name="remotePath">The device directory path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The directory entries reported by the device.</returns>
    public async ValueTask<IReadOnlyList<AdbDirectoryEntry>> ListDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        await using var stream = await OpenStreamAsync("sync:", cancellationToken).ConfigureAwait(false);
        return await AdbSyncProtocol.ListDirectoryAsync(stream, remotePath, connection.HasFeature(ListV2Feature), cancellationToken).ConfigureAwait(false);
    }

    private AdbSyncCompression SelectSyncCompression()
    {
        if (!connection.HasFeature(SendReceiveV2Feature))
        {
            return AdbSyncCompression.None;
        }

        if (connection.HasFeature(SendReceiveV2ZstdFeature))
        {
            return AdbSyncCompression.Zstd;
        }

        if (connection.HasFeature(SendReceiveV2Lz4Feature))
        {
            return AdbSyncCompression.Lz4;
        }

        return connection.HasFeature(SendReceiveV2BrotliFeature)
            ? AdbSyncCompression.Brotli
            : AdbSyncCompression.None;
    }

    /// <summary>
    /// Starts streaming logcat output as UTF-8 lines.
    /// </summary>
    /// <param name="arguments">Optional arguments passed to <c>logcat</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async stream of logcat lines.</returns>
    public async IAsyncEnumerable<string> LogcatAsync(string arguments = "", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var service = string.IsNullOrWhiteSpace(arguments) ? "shell:logcat" : $"shell:logcat {arguments}";
        await using var stream = await OpenStreamAsync(service, cancellationToken).ConfigureAwait(false);

        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[81920];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        var line = new StringBuilder();

        while (true)
        {
            var read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (line.Length != 0)
                {
                    yield return TrimTrailingCarriageReturn(line);
                }

                yield break;
            }

            var charCount = decoder.GetChars(bytes.AsSpan(0, read), chars, flush: false);
            for (var index = 0; index < charCount; index++)
            {
                var value = chars[index];
                if (value == '\n')
                {
                    yield return TrimTrailingCarriageReturn(line);
                    line.Clear();
                }
                else
                {
                    line.Append(value);
                }
            }
        }
    }

    /// <summary>
    /// Installs an APK by pushing it to a temporary path and invoking package manager install.
    /// </summary>
    /// <param name="apk">The APK stream.</param>
    /// <param name="fileName">The APK file name used on the device.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package manager output.</returns>
    public async ValueTask<string> InstallApkAsync(Stream apk, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apk);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (apk.CanSeek)
        {
            var result = await InstallPackagesAsync(
                [new AdbPackageFile(apk, Path.GetFileName(fileName))],
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Output;
        }

        var remotePath = $"/data/local/tmp/{AdbPackageNameValidation.ValidateSplitName(Path.GetFileName(fileName))}";
        await PushAsync(apk, remotePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var output = await ShellAsync($"pm install -r {remotePath}", cancellationToken).ConfigureAwait(false);
        return AdbPackageManagerOutput.EnsureSuccess(output);
    }

    /// <summary>
    /// Creates an Android package install session.
    /// </summary>
    /// <param name="options">Install session options.</param>
    /// <param name="totalSize">Optional total package size for the session.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The created install session.</returns>
    public async ValueTask<AdbPackageInstallSession> CreateInstallSessionAsync(
        AdbInstallOptions? options = null,
        long? totalSize = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AdbInstallOptions();
        var command = AdbInstallCommandBuilder.CreateSession(options, totalSize);
        var output = await ShellAsync(command, cancellationToken).ConfigureAwait(false);
        var sessionId = AdbPackageManagerOutput.ParseCreatedSessionId(output);
        return new AdbPackageInstallSession(this, sessionId, options.Clone());
    }

    /// <summary>
    /// Installs one or more APK files using an Android package install session.
    /// </summary>
    /// <param name="packages">The APK files or split APK files to install.</param>
    /// <param name="options">Install session options.</param>
    /// <param name="progress">Optional cumulative byte progress across all package files.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The completed install result.</returns>
    public async ValueTask<AdbPackageInstallResult> InstallPackagesAsync(
        IReadOnlyList<AdbPackageFile> packages,
        AdbInstallOptions? options = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Count == 0)
        {
            throw new ArgumentException("At least one package file is required.", nameof(packages));
        }

        var totalSize = 0L;
        foreach (var packageFile in packages)
        {
            ArgumentNullException.ThrowIfNull(packageFile);
            totalSize = checked(totalSize + packageFile.Size);
        }

        await using var session = await CreateInstallSessionAsync(options, totalSize, cancellationToken).ConfigureAwait(false);
        var totalWritten = 0L;
        try
        {
            foreach (var packageFile in packages)
            {
                await session.WriteAsync(
                    packageFile,
                    progress is null ? null : new OffsetProgress(progress, totalWritten),
                    cancellationToken).ConfigureAwait(false);
                totalWritten += packageFile.Size;
            }

            var output = await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new AdbPackageInstallResult(session.SessionId, output, session.IsStaged);
        }
        catch
        {
            try
            {
                await session.AbandonAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (AdbPackageManagerException)
            {
            }

            throw;
        }
    }

    /// <summary>
    /// Installs one or more APK files using a staged Android package install session.
    /// </summary>
    /// <param name="packages">The APK files or split APK files to install.</param>
    /// <param name="options">Install session options.</param>
    /// <param name="progress">Optional cumulative byte progress across all package files.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The completed install result.</returns>
    public ValueTask<AdbPackageInstallResult> InstallStagedPackagesAsync(
        IReadOnlyList<AdbPackageFile> packages,
        AdbInstallOptions? options = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stagedOptions = options?.Clone() ?? new AdbInstallOptions();
        stagedOptions.Staged = true;
        return InstallPackagesAsync(packages, stagedOptions, progress, cancellationToken);
    }

    /// <summary>
    /// Lists installed package names using the device package manager.
    /// </summary>
    /// <param name="filter">Optional package-name filter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package names reported by the device.</returns>
    public async ValueTask<IReadOnlyList<string>> ListPackagesAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            AdbPackageNameValidation.ValidatePackageIdentifier(filter);
        }

        var command = string.IsNullOrWhiteSpace(filter)
            ? "pm list packages"
            : $"pm list packages {filter}";
        var output = await ShellAsync(command, cancellationToken).ConfigureAwait(false);
        var packages = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            packages.Add(line.StartsWith("package:", StringComparison.Ordinal) ? line["package:".Length..] : line);
        }

        return packages;
    }

    /// <summary>
    /// Uninstalls an Android package.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="keepData">Whether to keep package data and cache directories.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package manager output.</returns>
    public ValueTask<string> UninstallPackageAsync(string packageName, bool keepData = false, CancellationToken cancellationToken = default)
    {
        AdbPackageNameValidation.ValidatePackageIdentifier(packageName);
        return ShellAsync(keepData ? $"pm uninstall -k {packageName}" : $"pm uninstall {packageName}", cancellationToken);
    }

    /// <summary>
    /// Clears package data using the device package manager.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package manager output.</returns>
    public ValueTask<string> ClearPackageDataAsync(string packageName, CancellationToken cancellationToken = default)
    {
        AdbPackageNameValidation.ValidatePackageIdentifier(packageName);
        return ShellAsync($"pm clear {packageName}", cancellationToken);
    }

    /// <summary>
    /// Starts a local TCP listener and forwards accepted sockets to a device-side ADB socket.
    /// </summary>
    /// <param name="localPort">The local TCP port. Use zero to request an ephemeral port.</param>
    /// <param name="remote">The device-side socket specification.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The active forwarding session.</returns>
    public ValueTask<AdbPortForward> StartPortForwardAsync(int localPort, AdbSocketSpec remote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);
        cancellationToken.ThrowIfCancellationRequested();
        if (localPort is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(localPort), "TCP ports must be in the range 0 through 65535.");
        }

        var listener = new TcpListener(IPAddress.Loopback, localPort);
        listener.Start();
        return ValueTask.FromResult(new AdbPortForward(this, listener, remote));
    }

    /// <summary>
    /// Requests device-side reverse forwarding from a device socket to a host socket.
    /// </summary>
    /// <param name="remote">The device-side listening socket.</param>
    /// <param name="local">The host-side destination socket.</param>
    /// <param name="noRebind">Whether an existing reverse mapping must be preserved instead of replaced.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The device service response payload.</returns>
    public ValueTask<string> ReverseForwardAsync(AdbSocketSpec remote, AdbSocketSpec local, bool noRebind = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(local);
        var service = noRebind
            ? $"reverse:forward:norebind:{remote.Value};{local.Value}"
            : $"reverse:forward:{remote.Value};{local.Value}";
        return ExecuteServiceCommandAsync(service, cancellationToken);
    }

    /// <summary>
    /// Removes a device-side reverse forwarding rule.
    /// </summary>
    /// <param name="remote">The device-side listening socket.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The device service response payload.</returns>
    public ValueTask<string> RemoveReverseForwardAsync(AdbSocketSpec remote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);
        return ExecuteServiceCommandAsync($"reverse:killforward:{remote.Value}", cancellationToken);
    }

    /// <summary>
    /// Removes all device-side reverse forwarding rules.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The device service response payload.</returns>
    public ValueTask<string> RemoveAllReverseForwardsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteServiceCommandAsync("reverse:killforward-all", cancellationToken);
    }

    /// <summary>
    /// Lists device-side reverse forwarding rules.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The textual reverse forwarding table reported by the device.</returns>
    public ValueTask<string> ListReverseForwardsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteServiceCommandAsync("reverse:list-forward", cancellationToken);
    }

    /// <summary>
    /// Reboots the device normally.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask RebootAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await OpenStreamAsync("reboot:", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reboots the device to bootloader.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask RebootBootloaderAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await OpenStreamAsync("reboot:bootloader", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reboots the device to recovery.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask RebootRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await OpenStreamAsync("reboot:recovery", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return connection.DisposeAsync();
    }

    private async ValueTask<string> ExecuteServiceCommandAsync(string service, CancellationToken cancellationToken)
    {
        await using var stream = await OpenStreamAsync(service, cancellationToken).ConfigureAwait(false);
        var response = Encoding.UTF8.GetString(await stream.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        if (response.StartsWith("FAIL", StringComparison.Ordinal))
        {
            throw new DeviceConnectionException(response.Length > 4 ? response[4..] : $"ADB service '{service}' failed.");
        }

        return response.StartsWith("OKAY", StringComparison.Ordinal) ? response[4..] : response;
    }

    private static string TrimTrailingCarriageReturn(StringBuilder line)
    {
        if (line.Length != 0 && line[^1] == '\r')
        {
            line.Length--;
        }

        return line.ToString();
    }

    private sealed class OffsetProgress(IProgress<long> inner, long offset) : IProgress<long>
    {
        public void Report(long value)
        {
            inner.Report(offset + value);
        }
    }

    private static AndroidDevice CreateTcpDevice(string host, int port, bool tlsActive)
    {
        var endpoint = $"{host}:{port}";
        var transportId = $"tcp:{endpoint}";
        var descriptor = new UsbDeviceDescriptor(
            transportId,
            0,
            0,
            0,
            AndroidUsbClass.VendorSpecificClass,
            AndroidUsbClass.AndroidSubClass,
            AndroidUsbClass.AdbProtocol,
            endpoint,
            "TCP",
            "ADB over TCP");

        return new AndroidDevice(
            new DeviceIdentity(endpoint, "TCP", "ADB over TCP", null, transportId),
            DeviceMode.Adb,
            DeviceCapabilities.Empty with { SupportsAdb = true, SupportsFileSync = true, SupportsTls = tlsActive },
            descriptor);
    }
}
