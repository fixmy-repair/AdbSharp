using AdbSharp.Common;
using AdbSharp.Common.Devices;
using AdbSharp.Common.Diagnostics;
using AdbSharp.Fastboot.Internal;
using AdbSharp.Fastboot.Sparse;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Fastboot;

/// <summary>
/// Client for the bootloader Fastboot protocol.
/// </summary>
public sealed class FastbootClient : IAsyncDisposable
{
    private readonly FastbootConnection connection;

    private FastbootClient(AndroidDevice device, FastbootConnection connection)
    {
        Device = device;
        this.connection = connection;
    }

    /// <summary>
    /// Gets the connected device.
    /// </summary>
    public AndroidDevice Device { get; }

    /// <summary>
    /// Connects to a Fastboot-capable device.
    /// </summary>
    /// <param name="device">The device to connect.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connected client.</returns>
    public static async ValueTask<FastbootClient> ConnectAsync(AndroidDevice device, FastbootClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Mode is not (DeviceMode.Fastboot or DeviceMode.Fastbootd or DeviceMode.Bootloader))
        {
            throw new DeviceConnectionException($"Device mode '{device.Mode}' does not expose Fastboot.");
        }

        options ??= new FastbootClientOptions();
        var factory = options.TransportFactory ?? UsbTransportRegistry.FindFactory(device.Usb);
        var transport = await UsbDeviceLockConflictHandler.OpenAsync(
            factory,
            device.Usb,
            options.LockConflictHandling,
            cancellationToken).ConfigureAwait(false);
        try
        {
            UsbTransportValidator.ValidateOpenedTransport(transport);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new FastbootClient(device, new FastbootConnection(transport, options));
    }

    /// <summary>
    /// Executes a raw Fastboot command.
    /// </summary>
    /// <param name="command">The command text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The terminal OKAY payload.</returns>
    public async ValueTask<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return (await connection.ExecuteCommandAsync(command, cancellationToken).ConfigureAwait(false)).Payload;
    }

    /// <summary>
    /// Reads a Fastboot variable.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The variable value.</returns>
    public ValueTask<string> GetVarAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ExecuteCommandAsync($"getvar:{name}", cancellationToken);
    }

    /// <summary>
    /// Reads a Fastboot variable as an unsigned 64-bit integer.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The parsed variable value.</returns>
    public async ValueTask<ulong> GetUInt64VarAsync(string name, CancellationToken cancellationToken = default)
    {
        var value = await GetVarAsync(name, cancellationToken).ConfigureAwait(false);
        return ParseFastbootInteger(value, name);
    }

    /// <summary>
    /// Reads the maximum Fastboot download size reported by the device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The max download size in bytes.</returns>
    public ValueTask<ulong> GetMaxDownloadSizeAsync(CancellationToken cancellationToken = default)
    {
        return GetUInt64VarAsync("max-download-size", cancellationToken);
    }

    /// <summary>
    /// Reads the maximum Fastboot fetch size reported by the device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The max fetch size in bytes.</returns>
    public ValueTask<ulong> GetMaxFetchSizeAsync(CancellationToken cancellationToken = default)
    {
        return GetUInt64VarAsync("max-fetch-size", cancellationToken);
    }

    /// <summary>
    /// Reads the partition size reported by the device.
    /// </summary>
    /// <param name="partition">The partition name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The partition size in bytes.</returns>
    public ValueTask<ulong> GetPartitionSizeAsync(string partition, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(partition, nameof(partition));
        return GetUInt64VarAsync($"partition-size:{partition}", cancellationToken);
    }

    /// <summary>
    /// Reads whether a partition is logical according to the device.
    /// </summary>
    /// <param name="partition">The partition name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true" /> when the partition is logical.</returns>
    public async ValueTask<bool> IsLogicalPartitionAsync(string partition, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(partition, nameof(partition));
        var value = await GetVarAsync($"is-logical:{partition}", cancellationToken).ConfigureAwait(false);
        return string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads whether the connected Fastboot implementation is userspace Fastboot.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true" /> when the device reports userspace Fastboot.</returns>
    public async ValueTask<bool> IsUserspaceAsync(CancellationToken cancellationToken = default)
    {
        var value = await GetVarAsync("is-userspace", cancellationToken).ConfigureAwait(false);
        return string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Downloads data to the device staging buffer.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <param name="length">The number of bytes to send.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask DownloadAsync(Stream source, long length, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return connection.DownloadAsync(source, length, progress, cancellationToken);
    }

    /// <summary>
    /// Downloads and flashes an image to a partition.
    /// </summary>
    /// <param name="partition">The partition name.</param>
    /// <param name="image">The image stream.</param>
    /// <param name="length">The image length. When omitted, the stream must be seekable.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask FlashPartitionAsync(string partition, Stream image, long? length = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(partition, nameof(partition));
        ArgumentNullException.ThrowIfNull(image);

        var imageLength = length ?? (image.CanSeek ? image.Length - image.Position : throw new ArgumentException("Image length is required for non-seekable streams.", nameof(length)));
        await DownloadAsync(image, imageLength, progress, cancellationToken).ConfigureAwait(false);
        await ExecuteCommandAsync($"flash:{partition}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates, downloads, and flashes an Android sparse image.
    /// </summary>
    /// <param name="partition">The partition name.</param>
    /// <param name="sparseImage">The sparse image stream. The stream must be seekable.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The parsed sparse image metadata.</returns>
    public async ValueTask<SparseImageInfo> FlashSparsePartitionAsync(string partition, Stream sparseImage, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(partition, nameof(partition));
        ArgumentNullException.ThrowIfNull(sparseImage);
        if (!sparseImage.CanSeek)
        {
            throw new ArgumentException("Sparse image validation requires a seekable stream.", nameof(sparseImage));
        }

        var start = sparseImage.Position;
        var info = await SparseImageReader.ReadInfoAsync(sparseImage, cancellationToken).ConfigureAwait(false);
        sparseImage.Position = start;
        await FlashPartitionAsync(partition, sparseImage, checked((long)info.EncodedLength), progress, cancellationToken).ConfigureAwait(false);
        return info;
    }

    /// <summary>
    /// Erases a partition.
    /// </summary>
    /// <param name="partition">The partition name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask EraseAsync(string partition, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(partition, nameof(partition));
        return new ValueTask(ExecuteCommandAsync($"erase:{partition}", cancellationToken).AsTask());
    }

    /// <summary>
    /// Boots the previously downloaded boot image.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask BootAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("boot", cancellationToken).AsTask());
    }

    /// <summary>
    /// Continues normal boot.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask ContinueAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("continue", cancellationToken).AsTask());
    }

    /// <summary>
    /// Reboots the device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask RebootAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("reboot", cancellationToken).AsTask());
    }

    /// <summary>
    /// Reboots to bootloader.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask RebootBootloaderAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("reboot-bootloader", cancellationToken).AsTask());
    }

    /// <summary>
    /// Reboots to userspace Fastboot.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask RebootFastbootAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("reboot-fastboot", cancellationToken).AsTask());
    }

    /// <summary>
    /// Executes an OEM command.
    /// </summary>
    /// <param name="command">The OEM command payload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The terminal OKAY payload.</returns>
    public ValueTask<string> OemAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return ExecuteCommandAsync($"oem {command}", cancellationToken);
    }

    /// <summary>
    /// Sends a flashing unlock command.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask FlashingUnlockAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("flashing unlock", cancellationToken).AsTask());
    }

    /// <summary>
    /// Sends a flashing lock command.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask FlashingLockAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("flashing lock", cancellationToken).AsTask());
    }

    /// <summary>
    /// Sends a critical flashing unlock command.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask FlashingUnlockCriticalAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("flashing unlock_critical", cancellationToken).AsTask());
    }

    /// <summary>
    /// Sends a critical flashing lock command.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask FlashingLockCriticalAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(ExecuteCommandAsync("flashing lock_critical", cancellationToken).AsTask());
    }

    /// <summary>
    /// Uploads data staged by the previous Fastboot command.
    /// </summary>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The uploaded bytes.</returns>
    public ValueTask<byte[]> UploadAsync(IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return connection.UploadAsync("upload", progress, cancellationToken);
    }

    /// <summary>
    /// Uploads data staged by the previous Fastboot command to a destination stream.
    /// </summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask UploadAsync(Stream destination, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return connection.UploadToAsync("upload", destination, progress, cancellationToken);
    }

    /// <summary>
    /// Fetches a named artifact when supported by the device.
    /// </summary>
    /// <param name="artifact">The artifact name.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The fetched bytes.</returns>
    public ValueTask<byte[]> FetchAsync(string artifact, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(artifact, nameof(artifact));
        return connection.UploadAsync($"fetch:{artifact}", progress, cancellationToken);
    }

    /// <summary>
    /// Fetches a named artifact to a destination stream when supported by the device.
    /// </summary>
    /// <param name="artifact">The artifact name.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask FetchAsync(string artifact, Stream destination, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(artifact, nameof(artifact));
        return connection.UploadToAsync($"fetch:{artifact}", destination, progress, cancellationToken);
    }

    /// <summary>
    /// Fetches a partition range to a destination stream when supported by the device.
    /// </summary>
    /// <param name="partition">The partition name.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="offset">Optional partition offset.</param>
    /// <param name="length">Optional number of bytes to fetch.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask FetchPartitionAsync(string partition, Stream destination, long? offset = null, long? length = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(partition, nameof(partition));
        ArgumentNullException.ThrowIfNull(destination);
        if (offset is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (length is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var command = offset is null
            ? $"fetch:{partition}"
            : length is null
                ? FormattableString.Invariant($"fetch:{partition}:0x{offset.Value:x8}")
                : FormattableString.Invariant($"fetch:{partition}:0x{offset.Value:x8}:0x{length.Value:x8}");
        return connection.UploadToAsync(command, destination, progress, cancellationToken);
    }

    /// <summary>
    /// Creates a logical partition when supported by the connected Fastboot implementation.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="sizeBytes">The requested size in bytes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask CreateLogicalPartitionAsync(string name, long sizeBytes, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        return new ValueTask(ExecuteCommandAsync($"create-logical-partition:{name}:{sizeBytes}", cancellationToken).AsTask());
    }

    /// <summary>
    /// Deletes a logical partition when supported by the connected Fastboot implementation.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask DeleteLogicalPartitionAsync(string name, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(name, nameof(name));
        return new ValueTask(ExecuteCommandAsync($"delete-logical-partition:{name}", cancellationToken).AsTask());
    }

    /// <summary>
    /// Resizes a logical partition when supported by the connected Fastboot implementation.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="sizeBytes">The requested size in bytes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask ResizeLogicalPartitionAsync(string name, long sizeBytes, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        return new ValueTask(ExecuteCommandAsync($"resize-logical-partition:{name}:{sizeBytes}", cancellationToken).AsTask());
    }

    /// <summary>
    /// Updates super partition metadata using a previously downloaded image.
    /// </summary>
    /// <param name="superPartition">The super partition name.</param>
    /// <param name="wipe">Whether to wipe existing logical partitions.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask UpdateSuperAsync(string superPartition, bool wipe = false, CancellationToken cancellationToken = default)
    {
        ValidateFastbootToken(superPartition, nameof(superPartition));
        var command = wipe ? $"update-super:{superPartition}:wipe" : $"update-super:{superPartition}";
        return new ValueTask(ExecuteCommandAsync(command, cancellationToken).AsTask());
    }

    /// <summary>
    /// Executes a snapshot update operation.
    /// </summary>
    /// <param name="operation">The snapshot operation, such as <c>cancel</c> or <c>merge</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The terminal OKAY payload.</returns>
    public ValueTask<string> SnapshotUpdateAsync(string operation, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (operation is not ("cancel" or "merge"))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), "Snapshot update operation must be 'cancel' or 'merge'.");
        }

        return ExecuteCommandAsync($"snapshot-update:{operation}", cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return connection.DisposeAsync();
    }

    private static ulong ParseFastbootInteger(string value, string variableName)
    {
        var trimmed = value.Trim();
        var style = System.Globalization.NumberStyles.Integer;
        var digits = trimmed;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            style = System.Globalization.NumberStyles.HexNumber;
            digits = trimmed[2..];
        }

        if (!ulong.TryParse(digits, style, System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException($"Fastboot variable '{variableName}' value '{value}' is not an unsigned integer.");
        }

        return result;
    }

    private static void ValidateFastbootToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains(':', StringComparison.Ordinal) || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Fastboot command tokens cannot contain ':' or NUL.", parameterName);
        }
    }
}
