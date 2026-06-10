using Pena_e_Arte.Domain.Utilities;

namespace Pena_e_Arte.UnitTests.Utilities;

public class SlugHelperTests
{
    [Theory]
    [InlineData("João Silva",        "joo-silva")]
    [InlineData("Hello World",       "hello-world")]
    [InlineData("  Trim Me  ",       "trim-me")]
    [InlineData("Foo---Bar",         "foo-bar")]
    [InlineData("Foo   Bar",         "foo-bar")]
    [InlineData("ALL CAPS",          "all-caps")]
    [InlineData("special!@#chars",   "specialchars")]
    [InlineData("a",                 "a")]
    public void GenerateSlug_ReturnsExpectedSlug(string input, string expected)
    {
        string result = SlugHelper.GenerateSlug(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GenerateSlug_LongerThan60Chars_TruncatesAt60()
    {
        string input  = new string('a', 80);
        string result = SlugHelper.GenerateSlug(input);
        Assert.Equal(60, result.Length);
    }

    [Fact]
    public void GenerateSlug_TruncationDoesNotEndWithDash()
    {
        string input  = string.Join("-", Enumerable.Repeat("word", 20));
        string result = SlugHelper.GenerateSlug(input);
        Assert.True(result.Length <= 60);
        Assert.DoesNotContain("-", result[^1..]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!@#$%^")]
    public void GenerateSlug_EmptyOrPunctuation_ReturnsFallback(string input)
    {
        string result = SlugHelper.GenerateSlug(input);
        Assert.Equal("studio", result);
    }
}
