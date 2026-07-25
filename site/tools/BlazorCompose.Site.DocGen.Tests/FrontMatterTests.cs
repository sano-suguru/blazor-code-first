using BlazorCompose.Site.DocGen;
using Xunit;

namespace BlazorCompose.Site.DocGen.Tests;

public class FrontMatterTests
{
    private const string Valid = "---\ntitle: Getting Started\norder: 10\n---\n\n## Installation\n";

    [Fact]
    public void Split_ValidFrontMatter_ReturnsFieldsAndBody()
    {
        var (fields, body) = FrontMatter.Split(Valid, "getting-started.md");

        Assert.Equal("Getting Started", fields.Title);
        Assert.Equal(10, fields.Order);
        Assert.StartsWith("## Installation", body.TrimStart('\n'));
        Assert.DoesNotContain("title:", body);
    }

    [Fact]
    public void Split_CrLfLineEndings_IsSupported()
    {
        var (fields, body) = FrontMatter.Split(
            "---\r\ntitle: T\r\norder: 1\r\n---\r\n## Body\r\n", "a.md");

        Assert.Equal("T", fields.Title);
        Assert.Equal(1, fields.Order);
        Assert.Contains("## Body", body);
    }

    [Fact]
    public void Split_ValueContainingColon_KeepsFullValue()
    {
        var (fields, _) = FrontMatter.Split("---\ntitle: A: B\norder: 1\n---\n", "a.md");
        Assert.Equal("A: B", fields.Title);
    }

    [Theory]
    [InlineData("## No front matter\n", "front matter")]              // no front matter block at all
    [InlineData("---\norder: 1\n---\n", "title")]                     // missing title
    [InlineData("---\ntitle: T\n---\n", "order")]                     // missing order
    [InlineData("---\ntitle: T\norder: 1\nextra: x\n---\n", "extra")] // unknown key
    [InlineData("---\ntitle: T\norder: abc\n---\n", "order")]         // order is not an integer
    [InlineData("---\ntitle:   \norder: 1\n---\n", "title")]          // title is empty
    [InlineData("---\ntitle: T\norder: 1\n", "closing")]              // no closing ---
    public void Split_InvalidFrontMatter_Throws(string raw, string messageFragment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FrontMatter.Split(raw, "sample.md"));
        Assert.Contains(messageFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sample.md", ex.Message);
    }
}
