using AdbSharp.Adb;
using AdbSharp.Authentication.Adb;
using Xunit.Abstractions;

namespace AdbSharp.IntegrationTests.Hardware;

public sealed class WirelessPairingCompatibilityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Android_11_pairing_endpoints_pass_vendor_matrix()
    {
        if (!HardwareTestsEnabled())
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var matrix = await WirelessPairingCompatibilityMatrix.LoadFromEnvironmentAsync(timeout.Token);
        if (matrix.Endpoints.Count == 0)
        {
            output.WriteLine("No Android 11+ pairing endpoints were configured. Set ADBSHARP_PAIRING_MATRIX or the single-endpoint ADBSHARP_PAIRING_* variables.");
            return;
        }

        using var keyPair = await AdbKeyStore.LoadOrCreateAsync(matrix.ResolveKeyPath(), timeout.Token);
        var options = new AdbPairingCompatibilityOptions
        {
            PairingOptions = new AdbPairingOptions { PublicKeyComment = matrix.PublicKeyComment },
            VerifyAdbConnection = matrix.VerifyAdbConnection,
            DefaultAdbPort = matrix.DefaultAdbPort
        };

        var results = new List<AdbPairingCompatibilityResult>(matrix.Endpoints.Count);
        foreach (var endpoint in matrix.Endpoints)
        {
            var result = await AdbPairingCompatibilityValidator.ValidateAsync(endpoint, keyPair, options, timeout.Token);
            results.Add(result);
            output.WriteLine(FormatResult(result));
        }

        var compatibleVendorCount = results
            .Where(static result => result.IsCompatible)
            .Select(static result => result.Vendor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.True(
            compatibleVendorCount >= matrix.MinimumVendorCount,
            $"Expected at least {matrix.MinimumVendorCount} compatible vendor(s), but only {compatibleVendorCount} passed.");

        foreach (var result in results)
        {
            Assert.True(result.IsCompatible, FormatFailure(result));
        }
    }

    private static bool HardwareTestsEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable("ADBSHARP_HARDWARE_TESTS"), "1", StringComparison.Ordinal);
    }

    private static string FormatResult(AdbPairingCompatibilityResult result)
    {
        var status = result.IsCompatible ? "PASS" : "FAIL";
        var adb = result.AdbConnectionTested
            ? $" adb={result.AdbConnectionSucceeded}"
            : " adb=not-tested";
        return $"{status} {result.Vendor} {result.Model} {result.Host}:{result.PairingPort} pairing={result.PairingSucceeded}{adb} elapsed={result.Elapsed.TotalSeconds:n1}s";
    }

    private static string FormatFailure(AdbPairingCompatibilityResult result)
    {
        return $"{FormatResult(result)} error={result.ErrorType}: {result.ErrorMessage}";
    }
}
