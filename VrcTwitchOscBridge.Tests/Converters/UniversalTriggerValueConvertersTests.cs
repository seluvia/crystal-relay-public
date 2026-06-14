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

public sealed class UniversalTriggerIntConverterTests
{
    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "-1")]
    [InlineData("2147483647", "2147483647")]
    [InlineData("-2147483648", "-2147483648")]
    [InlineData("1.0", "1")]
    [InlineData("abc", "abc")]
    [InlineData("1.5", "1.5")]
    public void Convert_StringToString_FormatsInt(string? input, string expected)
    {
        var converter = new UniversalTriggerIntConverter();
        var result = converter.Convert(input, typeof(string), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("-1", -1)]
    [InlineData("2147483647", 2147483647)]
    [InlineData("abc", 0)]
    [InlineData("1.5", 0)]
    public void ConvertBack_StringToInt_ReturnsExpected(string? input, int expected)
    {
        var converter = new UniversalTriggerIntConverter();
        var result = converter.ConvertBack(input, typeof(int), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertBack_InvalidInput_DoesNotThrow()
    {
        var converter = new UniversalTriggerIntConverter();
        var result = converter.ConvertBack("not a number", typeof(int), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(0, result);
    }
}

public sealed class UniversalTriggerFloatConverterTests
{
    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "-1")]
    [InlineData("1.5", "1.5")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("1.0", "1")]
    [InlineData("abc", "abc")]
    public void Convert_StringToString_FormatsFloat(string? input, string expected)
    {
        var converter = new UniversalTriggerFloatConverter();
        var result = converter.Convert(input, typeof(string), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData("", 0.0)]
    [InlineData("0", 0.0)]
    [InlineData("1.5", 1.5)]
    [InlineData("-1.5", -1.5)]
    [InlineData("0.0001", 0.0001)]
    [InlineData("abc", 0.0)]
    public void ConvertBack_StringToDouble_ReturnsExpected(string? input, double expected)
    {
        var converter = new UniversalTriggerFloatConverter();
        var result = converter.ConvertBack(input, typeof(double), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertBack_InvalidInput_DoesNotThrow()
    {
        var converter = new UniversalTriggerFloatConverter();
        var result = converter.ConvertBack("not a number", typeof(double), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result);
    }
}