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

    private static byte[] BuildPacket(string address, OscParameterType parameterType, string rawValue)
    {
        var bytes = new List<byte>(128);
        AppendPaddedString(bytes, address);

        switch (parameterType)
        {
            case OscParameterType.Bool:
            {
                var booleanValue = ParseBoolean(rawValue);
                AppendPaddedString(bytes, booleanValue ? ",T" : ",F");
                break;
            }
            case OscParameterType.Int:
            {
                AppendPaddedString(bytes, ",i");
                var intValue = int.Parse(rawValue, CultureInfo.InvariantCulture);
                var intBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(intBytes, intValue);
                bytes.AddRange(intBytes);
                break;
            }
            case OscParameterType.Float:
            {
                AppendPaddedString(bytes, ",f");
                var floatValue = float.Parse(rawValue, CultureInfo.InvariantCulture);
                var floatBytes = BitConverter.GetBytes(floatValue);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(floatBytes);
                }

                bytes.AddRange(floatBytes);
                break;
            }
            case OscParameterType.String:
            {
                AppendPaddedString(bytes, ",s");
                AppendPaddedString(bytes, rawValue);
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported OSC parameter type: {parameterType}");
        }

        return [.. bytes];
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
