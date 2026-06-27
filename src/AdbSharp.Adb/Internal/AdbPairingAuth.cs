using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using AdbSharp.Common;

namespace AdbSharp.Adb.Internal;

internal sealed class AdbPairingAuth
{
    private const int MessageSize = 32;
    private const int KeyMaterialSize = 64;
    private const int AesKeySize = 16;
    private const int AesGcmNonceSize = 12;
    private const int AesGcmTagSize = 16;
    private static readonly byte[] ClientName = Encoding.ASCII.GetBytes("adb pair client\0");
    private static readonly byte[] ServerName = Encoding.ASCII.GetBytes("adb pair server\0");
    private static readonly byte[] AesInfo = Encoding.ASCII.GetBytes("adb pairing_auth aes-128-gcm key");
    private readonly bool isClient;
    private readonly byte[] passwordHash;
    private readonly BigInteger passwordScalar;
    private readonly BigInteger privateScalar;
    private readonly byte[] message;
    private byte[]? aesKey;
    private ulong encryptSequence;
    private ulong decryptSequence;

    private AdbPairingAuth(bool isClient, ReadOnlySpan<byte> password)
    {
        if (password.IsEmpty)
        {
            throw new ArgumentException("ADB pairing password cannot be empty.", nameof(password));
        }

        this.isClient = isClient;
        passwordHash = SHA512.HashData(password);
        passwordScalar = AdjustPasswordScalar(ReduceScalar(passwordHash));
        privateScalar = CreatePrivateScalar();

        var generator = Ed25519Point.ScalarMultiply(Ed25519Point.BasePoint, privateScalar);
        var maskPoint = isClient ? Ed25519Point.M : Ed25519Point.N;
        var mask = Ed25519Point.ScalarMultiply(maskPoint, passwordScalar);
        message = Ed25519Point.Add(generator, mask).Encode();
    }

    public static AdbPairingAuth CreateClient(ReadOnlySpan<byte> password)
    {
        return new AdbPairingAuth(isClient: true, password);
    }

    public static AdbPairingAuth CreateServer(ReadOnlySpan<byte> password)
    {
        return new AdbPairingAuth(isClient: false, password);
    }

    public byte[] CreateMessage()
    {
        return [.. message];
    }

    public void InitializeCipher(ReadOnlySpan<byte> peerMessage)
    {
        if (aesKey is not null)
        {
            throw new ProtocolException("ADB pairing cipher has already been initialized.");
        }

        if (peerMessage.Length != MessageSize)
        {
            throw new ProtocolException($"ADB pairing SPAKE2 message length {peerMessage.Length} is invalid.");
        }

        var peerPoint = Ed25519Point.Decode(peerMessage);
        var peerMaskPoint = isClient ? Ed25519Point.N : Ed25519Point.M;
        var peerMask = Ed25519Point.ScalarMultiply(peerMaskPoint, passwordScalar);
        var unmaskedPeerPoint = Ed25519Point.Subtract(peerPoint, peerMask);
        var sharedPoint = Ed25519Point.ScalarMultiply(unmaskedPeerPoint, privateScalar);
        var sharedPointEncoded = sharedPoint.Encode();

        var keyMaterial = DeriveSpake2KeyMaterial(peerMessage, sharedPointEncoded);
        try
        {
            aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, keyMaterial, AesKeySize, salt: [], info: AesInfo);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
            CryptographicOperations.ZeroMemory(sharedPointEncoded);
        }
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("ADB pairing plaintext cannot be empty.", nameof(plaintext));
        }

        var key = aesKey ?? throw new ProtocolException("ADB pairing cipher has not been initialized.");
        var ciphertext = new byte[plaintext.Length + AesGcmTagSize];
        Span<byte> nonce = stackalloc byte[AesGcmNonceSize];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce, encryptSequence);
        using var aes = new AesGcm(key, AesGcmTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length), associatedData: []);
        encryptSequence++;
        return ciphertext;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.Length <= AesGcmTagSize)
        {
            throw new ProtocolException("ADB pairing ciphertext is too short.");
        }

        var key = aesKey ?? throw new ProtocolException("ADB pairing cipher has not been initialized.");
        var plaintextLength = ciphertext.Length - AesGcmTagSize;
        var plaintext = new byte[plaintextLength];
        Span<byte> nonce = stackalloc byte[AesGcmNonceSize];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce, decryptSequence);
        using var aes = new AesGcm(key, AesGcmTagSize);
        try
        {
            aes.Decrypt(nonce, ciphertext[..plaintextLength], ciphertext[plaintextLength..], plaintext, associatedData: []);
        }
        catch (CryptographicException ex)
        {
            throw new ProtocolException("ADB pairing encrypted payload authentication failed.", ex);
        }

        decryptSequence++;
        return plaintext;
    }

    private byte[] DeriveSpake2KeyMaterial(ReadOnlySpan<byte> peerMessage, ReadOnlySpan<byte> sharedPoint)
    {
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        if (isClient)
        {
            AppendLengthPrefixed(incrementalHash, ClientName);
            AppendLengthPrefixed(incrementalHash, ServerName);
            AppendLengthPrefixed(incrementalHash, message);
            AppendLengthPrefixed(incrementalHash, peerMessage);
        }
        else
        {
            AppendLengthPrefixed(incrementalHash, ClientName);
            AppendLengthPrefixed(incrementalHash, ServerName);
            AppendLengthPrefixed(incrementalHash, peerMessage);
            AppendLengthPrefixed(incrementalHash, message);
        }

        AppendLengthPrefixed(incrementalHash, sharedPoint);
        AppendLengthPrefixed(incrementalHash, passwordHash);
        return incrementalHash.GetHashAndReset();
    }

    private static void AppendLengthPrefixed(IncrementalHash hash, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(length, checked((ulong)data.Length));
        hash.AppendData(length);
        hash.AppendData(data);
    }

    private static BigInteger CreatePrivateScalar()
    {
        Span<byte> random = stackalloc byte[KeyMaterialSize];
        RandomNumberGenerator.Fill(random);
        var reduced = ReduceScalar(random);
        return reduced << 3;
    }

    private static BigInteger AdjustPasswordScalar(BigInteger value)
    {
        if (!value.IsEven)
        {
            value += Ed25519Point.Order;
        }

        if ((value & 2) != 0)
        {
            value += Ed25519Point.Order << 1;
        }

        if ((value & 4) != 0)
        {
            value += Ed25519Point.Order << 2;
        }

        return value;
    }

    private static BigInteger ReduceScalar(ReadOnlySpan<byte> value)
    {
        return FromLittleEndian(value) % Ed25519Point.Order;
    }

    private static BigInteger FromLittleEndian(ReadOnlySpan<byte> value)
    {
        return new BigInteger(value, isUnsigned: true, isBigEndian: false);
    }

    private readonly record struct Ed25519Point(BigInteger X, BigInteger Y)
    {
        private const string PrimeHex = "7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffed";
        private const string OrderHex = "1000000000000000000000000000000014def9dea2f79cd65812631a5cf5d3ed";
        private const string MEncodedHex = "5ada7e4bf6ddd9adb6626d32131c6b5c51a1e347a3478f53cfcf441b88eed12e";
        private const string NEncodedHex = "10e3df0ae37d8e7a99b5fe74b44672103dbddcbd06af680d71329a11693bc778";
        private static readonly BigInteger Prime = BigInteger.Parse("00" + PrimeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        public static readonly BigInteger Order = BigInteger.Parse("00" + OrderHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        private static readonly BigInteger D = Mod(-121665 * ModInverse(121666));
        private static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (Prime - 1) / 4, Prime);
        public static readonly Ed25519Point Identity = new(BigInteger.Zero, BigInteger.One);
        public static readonly Ed25519Point BasePoint = DecodeHex("5866666666666666666666666666666666666666666666666666666666666666"u8);
        public static readonly Ed25519Point M = Decode(MEncodedHex);
        public static readonly Ed25519Point N = Decode(NEncodedHex);

        public static Ed25519Point Add(Ed25519Point left, Ed25519Point right)
        {
            var x1x2 = Mod(left.X * right.X);
            var y1y2 = Mod(left.Y * right.Y);
            var dxxyy = Mod(D * x1x2 * y1y2);
            var x = Mod((left.X * right.Y + left.Y * right.X) * ModInverse(1 + dxxyy));
            var y = Mod((y1y2 + x1x2) * ModInverse(1 - dxxyy));
            return new Ed25519Point(x, y);
        }

        public static Ed25519Point Subtract(Ed25519Point left, Ed25519Point right)
        {
            return Add(left, new Ed25519Point(Mod(-right.X), right.Y));
        }

        public static Ed25519Point ScalarMultiply(Ed25519Point point, BigInteger scalar)
        {
            var result = Identity;
            var addend = point;
            while (scalar > 0)
            {
                if (!scalar.IsEven)
                {
                    result = Add(result, addend);
                }

                addend = Add(addend, addend);
                scalar >>= 1;
            }

            return result;
        }

        public static Ed25519Point Decode(ReadOnlySpan<byte> encoded)
        {
            if (encoded.Length != MessageSize)
            {
                throw new ProtocolException($"ADB pairing Ed25519 point length {encoded.Length} is invalid.");
            }

            Span<byte> yBytes = stackalloc byte[MessageSize];
            encoded.CopyTo(yBytes);
            var xSign = (yBytes[^1] & 0x80) != 0;
            yBytes[^1] &= 0x7f;
            var y = FromLittleEndian(yBytes);
            if (y >= Prime)
            {
                throw new ProtocolException("ADB pairing Ed25519 point encoding is invalid.");
            }

            var ySquared = Mod(y * y);
            var numerator = Mod(ySquared - 1);
            var denominator = Mod(D * ySquared + 1);
            var x = RecoverX(numerator, denominator);
            if (x.IsZero && xSign)
            {
                throw new ProtocolException("ADB pairing Ed25519 point sign bit is invalid.");
            }

            if (x.IsEven == xSign)
            {
                x = Mod(-x);
            }

            return new Ed25519Point(x, y);
        }

        public byte[] Encode()
        {
            var result = ToLittleEndian(Y);
            if (!X.IsEven)
            {
                result[^1] |= 0x80;
            }

            return result;
        }

        private static Ed25519Point DecodeHex(ReadOnlySpan<byte> hexAscii)
        {
            return Decode(Convert.FromHexString(Encoding.ASCII.GetString(hexAscii)));
        }

        private static Ed25519Point Decode(string hex)
        {
            return Decode(Convert.FromHexString(hex));
        }

        private static BigInteger RecoverX(BigInteger numerator, BigInteger denominator)
        {
            var value = Mod(numerator * ModInverse(denominator));
            var x = BigInteger.ModPow(value, (Prime + 3) / 8, Prime);
            var check = Mod(x * x - value);
            if (!check.IsZero)
            {
                x = Mod(x * SqrtMinusOne);
                check = Mod(x * x - value);
                if (!check.IsZero)
                {
                    throw new ProtocolException("ADB pairing Ed25519 point is not on the curve.");
                }
            }

            return x;
        }

        private static byte[] ToLittleEndian(BigInteger value)
        {
            var result = new byte[MessageSize];
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
            if (bytes.Length > result.Length)
            {
                throw new ProtocolException("ADB pairing Ed25519 coordinate does not fit in 32 bytes.");
            }

            bytes.CopyTo(result, 0);
            return result;
        }

        private static BigInteger ModInverse(BigInteger value)
        {
            return BigInteger.ModPow(Mod(value), Prime - 2, Prime);
        }

        private static BigInteger Mod(BigInteger value)
        {
            value %= Prime;
            return value.Sign < 0 ? value + Prime : value;
        }
    }
}
