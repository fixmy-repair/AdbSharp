using AdbSharp.Authentication.Adb;

namespace AdbSharp.Tests.Authentication;

public sealed class AdbKeyPairTests
{
    [Fact]
    public void Signs_and_verifies_token()
    {
        using var key = AdbKeyPair.Create();
        var token = new byte[] { 1, 2, 3, 4, 5 };

        var signature = key.SignToken(token);

        Assert.True(key.VerifyToken(token, signature));
    }

    [Fact]
    public void Exports_null_terminated_adb_public_key()
    {
        using var key = AdbKeyPair.Create();

        var payload = key.ExportAdbPublicKey("test");

        Assert.EndsWith(" test\0", System.Text.Encoding.ASCII.GetString(payload), StringComparison.Ordinal);
    }
}
