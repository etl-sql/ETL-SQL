using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Host-literal normalization and address-range classification used by the connector destination
/// authorizer. Normalizes obfuscated IP literals (decimal, hex, octal, IPv4-mapped IPv6) to a
/// canonical form so allow/deny rules and range checks cannot be bypassed by encoding, and
/// classifies loopback / link-local / private / unique-local addresses for SSRF protection.
/// </summary>
public static class NetworkDestinationRules
{
    /// <summary>
    /// Returns the canonical textual form of an IP literal, or the original host unchanged when it
    /// is a DNS name. Handles bracketed IPv6 (<c>[::1]</c>), 32-bit decimal/hex IPv4 (<c>2130706433</c>,
    /// <c>0x7f000001</c>), dotted hex/octal octets, and IPv4-mapped IPv6 (<c>::ffff:127.0.0.1</c>).
    /// </summary>
    public static string Normalize(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return host;
        var trimmed = host.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            trimmed = trimmed[1..^1];

        if (TryParseAddress(trimmed, out var address))
        {
            // Collapse IPv4-mapped IPv6 to the IPv4 form so range checks see the real target.
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            return address.ToString();
        }
        return trimmed;
    }

    /// <summary>
    /// True when the host resolves to a loopback, link-local, private (RFC 1918), carrier-grade NAT,
    /// or IPv6 unique-local / loopback address — the ranges an SSRF attempt targets.
    /// </summary>
    public static bool IsRestrictedRange(string host)
    {
        if (!TryParseAddress(Normalize(host), out var address)) return false;
        return IsRestricted(address);
    }

    /// <summary>
    /// Parses obfuscated IPv4 literals that <see cref="IPAddress.TryParse"/> rejects — a bare 32-bit
    /// decimal or hex value, and dotted octets in hex/octal — in addition to standard forms.
    /// </summary>
    public static bool TryParseAddress(string value, out IPAddress address)
    {
        if (IPAddress.TryParse(value, out var parsed))
        {
            address = parsed;
            return true;
        }

        // Bare 32-bit integer (decimal, or 0x/0-prefixed) — e.g. 2130706433 or 0x7f000001.
        if (TryParseUInt32(value, out var packed))
        {
            address = new IPAddress(new[]
            {
                (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed
            });
            return true;
        }

        // Dotted octets where any octet uses hex/octal notation — e.g. 0x7f.0.0.1 or 0177.0.0.1.
        var parts = value.Split('.');
        if (parts.Length == 4)
        {
            var octets = new byte[4];
            var ok = true;
            for (var i = 0; i < 4 && ok; i++)
                ok = TryParseOctet(parts[i], out octets[i]);
            if (ok)
            {
                address = new IPAddress(octets);
                return true;
            }
        }

        address = IPAddress.None;
        return false;
    }

    private static bool IsRestricted(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10                                   // 10.0.0.0/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 link-local
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // 100.64.0.0/10 carrier-grade NAT
                || b[0] == 127;                                 // 127.0.0.0/8
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal) return true;           // fe80::/10
            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;                       // fc00::/7 unique-local
        }

        return false;
    }

    private static bool TryParseUInt32(string value, out uint result)
    {
        result = 0;
        var text = value.Trim();
        if (text.Length == 0 || text.Contains('.')) return false;
        try
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                result = Convert.ToUInt32(text[2..], 16);
            else if (text.Length > 1 && text[0] == '0')
                result = Convert.ToUInt32(text, 8);
            else if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
                result = dec;
            else
                return false;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseOctet(string part, out byte value)
    {
        value = 0;
        if (!TryParseUInt32(part, out var parsed) || parsed > 255) return false;
        value = (byte)parsed;
        return true;
    }
}
