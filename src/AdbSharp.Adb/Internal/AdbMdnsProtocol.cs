using System.Buffers.Binary;
using System.Net;
using System.Text;
using AdbSharp.Common;

namespace AdbSharp.Adb.Internal;

internal static class AdbMdnsProtocol
{
    public const int MdnsPort = 5353;
    public const string MdnsIpv4Address = "224.0.0.251";
    public const string MdnsIpv6Address = "ff02::fb";
    private const ushort QueryClassInternetUnicastResponse = 0x8001;
    private const ushort RecordTypeA = 1;
    private const ushort RecordTypePtr = 12;
    private const ushort RecordTypeTxt = 16;
    private const ushort RecordTypeAaaa = 28;
    private const ushort RecordTypeSrv = 33;

    public static byte[] CreateQuery(IEnumerable<AdbMdnsServiceKind> serviceKinds)
    {
        var serviceTypes = serviceKinds
            .Select(GetServiceType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (serviceTypes.Length == 0)
        {
            throw new ArgumentException("At least one mDNS service kind must be requested.", nameof(serviceKinds));
        }

        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], checked((ushort)serviceTypes.Length));
        stream.Write(header);
        Span<byte> question = stackalloc byte[4];
        foreach (var serviceType in serviceTypes)
        {
            WriteName(stream, ToFqdn(serviceType));
            BinaryPrimitives.WriteUInt16BigEndian(question, RecordTypePtr);
            BinaryPrimitives.WriteUInt16BigEndian(question[2..], QueryClassInternetUnicastResponse);
            stream.Write(question);
        }

        return stream.ToArray();
    }

    public static IReadOnlyList<AdbMdnsService> ParseResponse(ReadOnlySpan<byte> message)
    {
        if (message.Length < 12)
        {
            throw new ProtocolException("mDNS message is shorter than the DNS header.");
        }

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(message[6..]);
        var authorityCount = BinaryPrimitives.ReadUInt16BigEndian(message[8..]);
        var additionalCount = BinaryPrimitives.ReadUInt16BigEndian(message[10..]);
        var offset = 12;

        for (var i = 0; i < questionCount; i++)
        {
            _ = ReadName(message, ref offset);
            Skip(message, ref offset, 4);
        }

        var records = new List<MdnsRecord>(answerCount + authorityCount + additionalCount);
        var recordCount = checked(answerCount + authorityCount + additionalCount);
        for (var i = 0; i < recordCount; i++)
        {
            records.Add(ReadRecord(message, ref offset));
        }

        return BuildServices(records);
    }

    public static string GetServiceType(AdbMdnsServiceKind kind)
    {
        return kind switch
        {
            AdbMdnsServiceKind.LegacyAdb => AdbMdnsServiceTypes.LegacyAdb,
            AdbMdnsServiceKind.Pairing => AdbMdnsServiceTypes.Pairing,
            AdbMdnsServiceKind.Connect => AdbMdnsServiceTypes.Connect,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ADB mDNS service kind.")
        };
    }

    public static AdbMdnsServiceKind? TryGetServiceKind(string serviceType)
    {
        var normalized = TrimLocal(serviceType);
        return normalized switch
        {
            AdbMdnsServiceTypes.LegacyAdb => AdbMdnsServiceKind.LegacyAdb,
            AdbMdnsServiceTypes.Pairing => AdbMdnsServiceKind.Pairing,
            AdbMdnsServiceTypes.Connect => AdbMdnsServiceKind.Connect,
            _ => null
        };
    }

    private static IReadOnlyList<AdbMdnsService> BuildServices(IReadOnlyList<MdnsRecord> records)
    {
        var ptrRecords = new List<(string ServiceType, string InstanceFqdn, AdbMdnsServiceKind Kind)>();
        var srvRecords = new Dictionary<string, SrvRecord>(StringComparer.OrdinalIgnoreCase);
        var txtRecords = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, List<IPAddress>>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            switch (record)
            {
                case PtrRecord ptr when TryGetServiceKind(ptr.Name) is { } kind:
                    ptrRecords.Add((TrimLocal(ptr.Name), ptr.InstanceName, kind));
                    break;
                case SrvRecord srv:
                    srvRecords[srv.Name] = srv;
                    break;
                case TxtRecord txt:
                    txtRecords[txt.Name] = txt.Values;
                    break;
                case AddressRecord address:
                    if (!addresses.TryGetValue(address.Name, out var list))
                    {
                        list = [];
                        addresses[address.Name] = list;
                    }

                    if (!list.Contains(address.Address))
                    {
                        list.Add(address.Address);
                    }

                    break;
            }
        }

        var services = new List<AdbMdnsService>(ptrRecords.Count);
        foreach (var (serviceType, instanceFqdn, kind) in ptrRecords)
        {
            if (!srvRecords.TryGetValue(instanceFqdn, out var srv))
            {
                continue;
            }

            addresses.TryGetValue(srv.TargetHost, out var resolvedAddresses);
            txtRecords.TryGetValue(instanceFqdn, out var txt);
            services.Add(new AdbMdnsService(
                GetInstanceName(instanceFqdn, serviceType),
                serviceType,
                kind,
                srv.TargetHost,
                srv.Port,
                resolvedAddresses ?? [],
                txt));
        }

        return services;
    }

    private static MdnsRecord ReadRecord(ReadOnlySpan<byte> message, ref int offset)
    {
        var name = ReadName(message, ref offset);
        EnsureAvailable(message, offset, 10);
        var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
        var dataLength = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..]);
        offset += 10;
        EnsureAvailable(message, offset, dataLength);
        var dataOffset = offset;
        var data = message.Slice(offset, dataLength);
        offset += dataLength;

        return type switch
        {
            RecordTypePtr => new PtrRecord(name, ReadName(message, dataOffset)),
            RecordTypeSrv => ReadSrvRecord(message, name, data, dataOffset),
            RecordTypeTxt => new TxtRecord(name, ReadTxt(data)),
            RecordTypeA when data.Length == 4 => new AddressRecord(name, new IPAddress(data)),
            RecordTypeAaaa when data.Length == 16 => new AddressRecord(name, new IPAddress(data)),
            _ => new MdnsRecord(name)
        };
    }

    private static SrvRecord ReadSrvRecord(ReadOnlySpan<byte> message, string name, ReadOnlySpan<byte> data, int dataOffset)
    {
        if (data.Length < 7)
        {
            throw new ProtocolException("mDNS SRV record is too short.");
        }

        var port = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        return new SrvRecord(name, port, ReadName(message, dataOffset + 6));
    }

    private static IReadOnlyDictionary<string, string> ReadTxt(ReadOnlySpan<byte> data)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset++];
            EnsureAvailable(data, offset, length);
            var item = Encoding.UTF8.GetString(data.Slice(offset, length));
            offset += length;
            if (item.Length == 0)
            {
                continue;
            }

            var separator = item.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                values[item] = string.Empty;
            }
            else
            {
                values[item[..separator]] = item[(separator + 1)..];
            }
        }

        return values;
    }

    private static string ReadName(ReadOnlySpan<byte> message, int offset)
    {
        return ReadName(message, ref offset);
    }

    private static string ReadName(ReadOnlySpan<byte> message, ref int offset)
    {
        var labels = new List<string>();
        var current = offset;
        var consumed = false;
        var jumps = 0;

        while (true)
        {
            EnsureAvailable(message, current, 1);
            var length = message[current++];
            if (length == 0)
            {
                if (!consumed)
                {
                    offset = current;
                }

                return string.Join('.', labels);
            }

            if ((length & 0xc0) == 0xc0)
            {
                EnsureAvailable(message, current, 1);
                var pointer = ((length & 0x3f) << 8) | message[current++];
                if (++jumps > 16)
                {
                    throw new ProtocolException("mDNS name compression pointer chain is too deep.");
                }

                if (!consumed)
                {
                    offset = current;
                    consumed = true;
                }

                current = pointer;
                continue;
            }

            if ((length & 0xc0) != 0)
            {
                throw new ProtocolException("mDNS name label uses an unsupported encoding.");
            }

            EnsureAvailable(message, current, length);
            labels.Add(Encoding.UTF8.GetString(message.Slice(current, length)));
            current += length;
            if (!consumed)
            {
                offset = current;
            }
        }
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length > 63)
            {
                throw new ArgumentException("DNS labels cannot exceed 63 bytes.", nameof(name));
            }

            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
    }

    private static string GetInstanceName(string instanceFqdn, string serviceType)
    {
        var suffix = "." + ToFqdn(serviceType).TrimEnd('.');
        return instanceFqdn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? instanceFqdn[..^suffix.Length]
            : instanceFqdn;
    }

    private static string ToFqdn(string serviceType)
    {
        var trimmed = serviceType.TrimEnd('.');
        return trimmed.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".local";
    }

    private static string TrimLocal(string name)
    {
        return name.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? name[..^6] : name.TrimEnd('.');
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        EnsureAvailable(data, offset, length);
        offset += length;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new ProtocolException("mDNS message ended unexpectedly.");
        }
    }

    private record MdnsRecord(string Name);

    private sealed record PtrRecord(string Name, string InstanceName) : MdnsRecord(Name);

    private sealed record SrvRecord(string Name, int Port, string TargetHost) : MdnsRecord(Name);

    private sealed record TxtRecord(string Name, IReadOnlyDictionary<string, string> Values) : MdnsRecord(Name);

    private sealed record AddressRecord(string Name, IPAddress Address) : MdnsRecord(Name);
}
