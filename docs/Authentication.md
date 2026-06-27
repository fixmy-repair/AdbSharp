# Authentication

ADB authorization uses RSA host keys.

AdbSharp provides:

- `AdbKeyPair.Create()`
- PEM import/export for private keys
- PKCS#1 SHA-1 token signing for `AUTH`
- Android-specific ADB public key payload export
- self-signed X.509 client certificates derived from the ADB RSA key for `STLS`
- `AdbKeyStore.LoadOrCreateAsync(...)`
- `AdbRsaAuthenticator`
- `IAdbTlsAuthenticator` for custom TLS credential sources
- `AdbPairingClient` and `IAdbPairingBackend` for Android wireless debugging pairing

Example:

```csharp
var key = await AdbKeyStore.LoadOrCreateAsync(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".adbsharp", "adbkey"));
using var authenticator = new AdbRsaAuthenticator(key);

var options = new AdbClientOptions
{
    Authenticator = authenticator
};

await using var client = await AdbClient.ConnectAsync(device, options);
```

Wireless ADB endpoints that request `STLS` use the same authenticator:

```csharp
var keyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".android",
    "adbkey");
using var authenticator = new AdbRsaAuthenticator(await AdbKeyStore.LoadOrCreateAsync(keyPath));

await using var client = await AdbClient.ConnectWirelessAsync(
    "192.168.1.42",
    options: new AdbClientOptions { Authenticator = authenticator });
```

`AdbClientOptions.TlsCertificateProvider` can be used when an application wants to supply a certificate without using `AdbRsaAuthenticator`.

Wireless pairing uses the ADB RSA key as the local peer info:

```csharp
using var keyPair = await AdbKeyStore.LoadOrCreateAsync(keyPath);
var pairingResult = await AdbPairingClient.PairAsync(
    "192.168.1.42",
    pairingPort,
    pairingCode,
    keyPair);
```

The default pairing backend performs the AOSP TLS 1.3 handshake, exports the ADB pairing keying material, derives the SPAKE2 secret from the pairing code, and encrypts peer-info records with AES-GCM. `AdbPairingOptions.Backend` remains available for applications that need to supply a custom secure-channel implementation.
