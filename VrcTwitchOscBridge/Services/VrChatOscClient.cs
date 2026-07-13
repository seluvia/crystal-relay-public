using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class VrChatOscClient
{
    public byte[] BuildAvatarParameterPacket(
        string parameterName,
        OscParameterType parameterType,
        string rawValue)
    {
        var address = NormalizeAvatarParameterAddress(parameterName);
        return BuildPacket(address, parameterType, rawValue);
    }

    public byte[] BuildPacketForAddress(
        string address,
        OscParameterType parameterType,
        string rawValue)
    {
        var normalizedAddress = NormalizeOscAddress(address);
        return BuildPacket(normalizedAddress, parameterType, rawValue);
    }

    public byte[] BuildAvatarChangePacket(string avatarId)
    {
        return BuildPacket("/avatar/change", OscParameterType.String, avatarId.Trim());
    }

    public byte[] BuildInputButtonPacket(string inputName, bool isPressed)
    {
        var address = NormalizeInputAddress(inputName);
        return BuildPacket(address, OscParameterType.Int, isPressed ? "1" : "0");
    }

    public byte[] BuildInputAxisPacket(string inputName, float value)
    {
        var address = NormalizeInputAddress(inputName);
        return BuildPacket(address, OscParameterType.Float, value.ToString(CultureInfo.InvariantCulture));
    }

    public byte[] BuildChatboxInputPacket(string message, bool sendImmediately = true, bool playNotification = false)
    {
        var normalizedMessage = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new InvalidOperationException("Enter a chatbox message before sending it to VRChat.");
        }

        var bytes = new List<byte>(256);
        AppendPaddedString(bytes, "/chatbox/input");
        AppendPaddedString(
            bytes,
            $",s{(sendImmediately ? 'T' : 'F')}{(playNotification ? 'T' : 'F')}");
        AppendPaddedString(bytes, normalizedMessage);
        return [.. bytes];
    }

    public static string NormalizeAvatarParameterAddress(string parameterName)
    {
        var trimmed = parameterName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Enter an avatar parameter path before saving this rule.");
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal)
            ? trimmed
            : $"/avatar/parameters/{trimmed.TrimStart('/')}";
    }

    public static string NormalizeInputAddress(string inputName)
    {
        var trimmed = inputName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Choose a VRChat movement input before saving this rule.");
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal)
            ? trimmed
            : $"/input/{trimmed.TrimStart('/')}";
    }

    public static string NormalizeOscAddress(string address)
    {
        var trimmed = address.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Enter an OSC address before sending this action.");
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal)
            ? trimmed
            : $"/{trimmed.TrimStart('/')}";
    }

    private static byte[] BuildPacket(string address, OscParameterType parameterType, string rawValue)
    {
        var addressByteCount = Encoding.UTF8.GetByteCount(address);
        var addressSegmentLen = (addressByteCount + 1 + 3) & ~3;

        var typeTag = parameterType switch
        {
            OscParameterType.Bool => ParseBoolean(rawValue) ? ",T" : ",F",
            OscParameterType.Int => ",i",
            OscParameterType.Float => ",f",
            OscParameterType.String => ",s",
            _ => throw new InvalidOperationException($"Unsupported OSC parameter type: {parameterType}")
        };
        var typeTagByteCount = typeTag.Length;
        var typeSegmentLen = (typeTagByteCount + 1 + 3) & ~3;

        var valueSegmentLen = parameterType switch
        {
            OscParameterType.Bool => 0,
            OscParameterType.Int => 4,
            OscParameterType.Float => 4,
            OscParameterType.String => (Encoding.UTF8.GetByteCount(rawValue) + 1 + 3) & ~3,
            _ => 0
        };

        var totalLen = addressSegmentLen + typeSegmentLen + valueSegmentLen;
        var buffer = ArrayPool<byte>.Shared.Rent(totalLen);

        try
        {
            var offset = 0;

            offset += Encoding.UTF8.GetBytes(address.AsSpan(), buffer.AsSpan(offset));
            buffer[offset++] = 0;
            while (offset % 4 != 0) buffer[offset++] = 0;

            offset += Encoding.UTF8.GetBytes(typeTag.AsSpan(), buffer.AsSpan(offset));
            buffer[offset++] = 0;
            while (offset % 4 != 0) buffer[offset++] = 0;

            switch (parameterType)
            {
                case OscParameterType.Int:
                {
                    var intValue = int.Parse(rawValue, CultureInfo.InvariantCulture);
                    BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, 4), intValue);
                    break;
                }
                case OscParameterType.Float:
                {
                    var floatValue = float.Parse(rawValue, CultureInfo.InvariantCulture);
                    BinaryPrimitives.WriteSingleBigEndian(buffer.AsSpan(offset, 4), floatValue);
                    break;
                }
                case OscParameterType.String:
                {
                    offset += Encoding.UTF8.GetBytes(rawValue.AsSpan(), buffer.AsSpan(offset));
                    buffer[offset++] = 0;
                    while (offset % 4 != 0) buffer[offset++] = 0;
                    break;
                }
            }

            var result = new byte[totalLen];
            Array.Copy(buffer, result, totalLen);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendPaddedString(List<byte> buffer, string value)
    {
        buffer.AddRange(Encoding.UTF8.GetBytes(value));
        buffer.Add(0);

        while (buffer.Count % 4 != 0)
        {
            buffer.Add(0);
        }
    }

    private static bool ParseBoolean(string rawValue)
    {
        if (bool.TryParse(rawValue, out var boolValue))
        {
            return boolValue;
        }

        if (rawValue == "1")
        {
            return true;
        }

        if (rawValue == "0")
        {
            return false;
        }

        throw new FormatException($"'{rawValue}' is not a valid boolean OSC value.");
    }
}
