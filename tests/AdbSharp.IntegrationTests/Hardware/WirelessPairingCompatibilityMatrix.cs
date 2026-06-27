using System.Text.Json;
using AdbSharp.Adb;

namespace AdbSharp.IntegrationTests.Hardware;

internal sealed class WirelessPairingCompatibilityMatrix
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? KeyPath { get; set; }

    public string PublicKeyComment { get; set; } = "AdbSharp";

    public bool VerifyAdbConnection { get; set; } = true;

    public int DefaultAdbPort { get; set; } = 5555;

    public int MinimumVendorCount { get; set; } = 1;

    public List<AdbPairingCompatibilityEndpoint> Endpoints { get; set; } = [];

    public static async ValueTask<WirelessPairingCompatibilityMatrix> LoadFromEnvironmentAsync(CancellationToken cancellationToken)
    {
        var matrixValue = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_MATRIX");
        WirelessPairingCompatibilityMatrix matrix;
        if (!string.IsNullOrWhiteSpace(matrixValue))
        {
            var json = LooksLikeJson(matrixValue)
                ? matrixValue
                : await File.ReadAllTextAsync(ExpandPath(matrixValue), cancellationToken).ConfigureAwait(false);

            matrix = Deserialize(json);
        }
        else
        {
            matrix = LoadSingleEndpointFromEnvironment();
        }

        ApplyEnvironmentOverrides(matrix);
        return matrix;
    }

    public string ResolveKeyPath()
    {
        if (!string.IsNullOrWhiteSpace(KeyPath))
        {
            return ExpandPath(KeyPath);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Cannot locate the current user's home directory for the default ADB key.");
        }

        return Path.Combine(home, ".android", "adbkey");
    }

    private static WirelessPairingCompatibilityMatrix LoadSingleEndpointFromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_HOST");
        var pairingPort = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_PORT");
        var pairingCode = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_CODE");
        if (string.IsNullOrWhiteSpace(host)
            && string.IsNullOrWhiteSpace(pairingPort)
            && string.IsNullOrWhiteSpace(pairingCode))
        {
            return new WirelessPairingCompatibilityMatrix();
        }

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(pairingPort) || string.IsNullOrWhiteSpace(pairingCode))
        {
            throw new InvalidOperationException(
                "Set ADBSHARP_PAIRING_HOST, ADBSHARP_PAIRING_PORT, and ADBSHARP_PAIRING_CODE together for single-endpoint pairing validation.");
        }

        return new WirelessPairingCompatibilityMatrix
        {
            KeyPath = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_KEY_PATH"),
            Endpoints =
            [
                new AdbPairingCompatibilityEndpoint(
                    Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_VENDOR") ?? "unknown",
                    Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_MODEL") ?? "unknown",
                    host,
                    ParseRequiredPort(pairingPort, "ADBSHARP_PAIRING_PORT"),
                    pairingCode)
                {
                    AdbPort = ParseOptionalPort(Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_ADB_PORT"), "ADBSHARP_PAIRING_ADB_PORT"),
                    ExpectedManufacturer = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_EXPECTED_MANUFACTURER"),
                    ExpectedModel = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_EXPECTED_MODEL")
                }
            ]
        };
    }

    private static void ApplyEnvironmentOverrides(WirelessPairingCompatibilityMatrix matrix)
    {
        matrix.KeyPath = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_KEY_PATH") ?? matrix.KeyPath;
        matrix.PublicKeyComment = Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_PUBLIC_KEY_COMMENT") ?? matrix.PublicKeyComment;
        matrix.VerifyAdbConnection = ParseOptionalBoolean(Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_VERIFY_ADB")) ?? matrix.VerifyAdbConnection;
        matrix.DefaultAdbPort = ParseOptionalPort(Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_DEFAULT_ADB_PORT"), "ADBSHARP_PAIRING_DEFAULT_ADB_PORT")
            ?? matrix.DefaultAdbPort;
        matrix.MinimumVendorCount = ParseOptionalPositiveInteger(Environment.GetEnvironmentVariable("ADBSHARP_PAIRING_MIN_VENDORS"), "ADBSHARP_PAIRING_MIN_VENDORS")
            ?? matrix.MinimumVendorCount;
        matrix.Endpoints ??= [];
    }

    private static WirelessPairingCompatibilityMatrix Deserialize(string json)
    {
        var trimmed = json.AsSpan().TrimStart();
        if (!trimmed.IsEmpty && trimmed[0] == '[')
        {
            return new WirelessPairingCompatibilityMatrix
            {
                Endpoints = JsonSerializer.Deserialize<List<AdbPairingCompatibilityEndpoint>>(json, JsonOptions) ?? []
            };
        }

        return JsonSerializer.Deserialize<WirelessPairingCompatibilityMatrix>(json, JsonOptions)
            ?? new WirelessPairingCompatibilityMatrix();
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }

    private static string ExpandPath(string path)
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

    private static int ParseRequiredPort(string value, string name)
    {
        return ParseOptionalPort(value, name)
            ?? throw new InvalidOperationException($"{name} must be set.");
    }

    private static int? ParseOptionalPort(string? value, string name)
    {
        var port = ParseOptionalPositiveInteger(value, name);
        if (port is null)
        {
            return null;
        }

        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{name} must be a TCP port from 1 through 65535.");
        }

        return port;
    }

    private static int? ParseOptionalPositiveInteger(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out var result) || result < 1)
        {
            throw new InvalidOperationException($"{name} must be a positive integer.");
        }

        return result;
    }

    private static bool? ParseOptionalBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value is "1"
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
