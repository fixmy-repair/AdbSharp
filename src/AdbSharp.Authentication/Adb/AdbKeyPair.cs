using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AdbSharp.Authentication.Adb;

/// <summary>
/// Represents an RSA key pair usable for ADB authentication.
/// </summary>
public sealed class AdbKeyPair : IDisposable
{
    private const int KeySizeBits = 2048;
    private const int KeySizeBytes = KeySizeBits / 8;
    private const int KeySizeWords = KeySizeBytes / 4;
    private readonly RSA rsa;

    private AdbKeyPair(RSA rsa)
    {
        this.rsa = rsa;
    }

    /// <summary>
    /// Creates a new 2048-bit RSA ADB key pair.
    /// </summary>
    /// <returns>The created key pair.</returns>
    public static AdbKeyPair Create()
    {
        var rsa = RSA.Create(KeySizeBits);
        return new AdbKeyPair(rsa);
    }

    /// <summary>
    /// Imports a PEM-encoded private key.
    /// </summary>
    /// <param name="pem">The private key PEM.</param>
    /// <returns>The imported key pair.</returns>
    public static AdbKeyPair ImportPrivateKeyPem(ReadOnlySpan<char> pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new AdbKeyPair(rsa);
    }

    /// <summary>
    /// Exports the private key in PKCS#8 PEM format.
    /// </summary>
    /// <returns>The private key PEM.</returns>
    public string ExportPrivateKeyPem() => rsa.ExportPkcs8PrivateKeyPem();

    /// <summary>
    /// Creates a self-signed X.509 certificate for ADB TLS authentication.
    /// </summary>
    /// <returns>A self-signed certificate with the ADB RSA private key.</returns>
    public X509Certificate2 CreateSelfSignedCertificate()
    {
        var subject = new X500DistinguishedName("CN=Adb,O=Android,C=US");
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));
    }

    /// <summary>
    /// Signs an ADB authentication token.
    /// </summary>
    /// <param name="token">The token to sign.</param>
    /// <returns>The PKCS#1 SHA-1 signature.</returns>
    public byte[] SignToken(ReadOnlySpan<byte> token)
    {
        return rsa.SignData(token, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
    }

    /// <summary>
    /// Verifies an ADB authentication signature.
    /// </summary>
    /// <param name="token">The token that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <returns><see langword="true"/> when the signature is valid.</returns>
    public bool VerifyToken(ReadOnlySpan<byte> token, ReadOnlySpan<byte> signature)
    {
        return rsa.VerifyData(token, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
    }

    /// <summary>
    /// Exports the Android-specific ADB public key payload.
    /// </summary>
    /// <param name="comment">The key comment.</param>
    /// <returns>A null-terminated ADB public key payload.</returns>
    public byte[] ExportAdbPublicKey(string comment = "AdbSharp")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);

        var parameters = rsa.ExportParameters(false);
        if (parameters.Modulus is null || parameters.Exponent is null)
        {
            throw new CryptographicException("RSA public key parameters are incomplete.");
        }

        if (parameters.Modulus.Length != KeySizeBytes)
        {
            throw new NotSupportedException("ADB public key export currently requires a 2048-bit RSA key.");
        }

        Span<byte> key = stackalloc byte[4 + 4 + KeySizeBytes + KeySizeBytes + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(key[..4], KeySizeWords);

        var modulusLittleEndian = parameters.Modulus.Reverse().ToArray();
        var n0 = BinaryPrimitives.ReadUInt32LittleEndian(modulusLittleEndian.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(key.Slice(4, 4), unchecked(0u - ModInverse32(n0)));
        modulusLittleEndian.CopyTo(key.Slice(8, KeySizeBytes));

        var modulus = new BigInteger(parameters.Modulus, isUnsigned: true, isBigEndian: true);
        var r = BigInteger.One << KeySizeBits;
        var rr = BigInteger.Remainder(r * r, modulus);
        WriteFixedLittleEndian(rr, key.Slice(8 + KeySizeBytes, KeySizeBytes));

        var exponent = new BigInteger(parameters.Exponent, isUnsigned: true, isBigEndian: true);
        BinaryPrimitives.WriteUInt32LittleEndian(key.Slice(8 + KeySizeBytes + KeySizeBytes, 4), checked((uint)exponent));

        var body = Convert.ToBase64String(key);
        return Encoding.ASCII.GetBytes($"{body} {comment}\0");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        rsa.Dispose();
    }

    private static void WriteFixedLittleEndian(BigInteger value, Span<byte> destination)
    {
        destination.Clear();
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (bytes.Length > destination.Length)
        {
            throw new CryptographicException("Integer does not fit in destination.");
        }

        bytes.CopyTo(destination);
    }

    private static uint ModInverse32(uint value)
    {
        long t = 0;
        long newT = 1;
        long r = 1L << 32;
        long newR = value;

        while (newR != 0)
        {
            var quotient = r / newR;
            (t, newT) = (newT, t - quotient * newT);
            (r, newR) = (newR, r - quotient * newR);
        }

        if (r > 1)
        {
            throw new CryptographicException("Value is not invertible modulo 2^32.");
        }

        if (t < 0)
        {
            t += 1L << 32;
        }

        return unchecked((uint)t);
    }
}
