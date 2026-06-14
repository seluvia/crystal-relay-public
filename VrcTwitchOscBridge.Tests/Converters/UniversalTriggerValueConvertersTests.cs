using System.Globalization;
using Xunit;
using VrcTwitchOscBridge;

namespace VrcTwitchOscBridge.Tests.Converters;

public sealed class UniversalTriggerBoolConverterTests
{
    [Theory]
    [InlineData("True", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("False", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void Convert_StringToBool_ReturnsExpected(string? input, bool expected)
    {
        var converter = new UniversalTriggerBoolConverter();
        var result = converter.Convert(input, typeof(bool), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    [InlineData(null, "False")]
    public void ConvertBack_BoolToString_ReturnsCanonicalForm(bool? input, string expected)
    {
        var converter = new UniversalTriggerBoolConverter();
        var result = converter.ConvertBack(input, typeof(string), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }
}