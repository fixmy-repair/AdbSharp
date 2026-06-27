using AdbSharp.Fastboot;

namespace AdbSharp.Fastbootd;

internal static class FastbootdCapabilityProbe
{
    public static async ValueTask<bool> TryIsUserspaceAsync(FastbootClient client, CancellationToken cancellationToken)
    {
        try
        {
            return await client.IsUserspaceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FastbootCommandException)
        {
            return false;
        }
    }

    public static async ValueTask<bool> TryIsLogicalPartitionAsync(FastbootClient client, string partition, CancellationToken cancellationToken)
    {
        try
        {
            return await client.IsLogicalPartitionAsync(partition, cancellationToken).ConfigureAwait(false);
        }
        catch (FastbootCommandException)
        {
            return false;
        }
    }

    public static async ValueTask<string?> TryGetVarAsync(FastbootClient client, string name, CancellationToken cancellationToken)
    {
        try
        {
            var value = await client.GetVarAsync(name, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (FastbootCommandException)
        {
            return null;
        }
    }
}
