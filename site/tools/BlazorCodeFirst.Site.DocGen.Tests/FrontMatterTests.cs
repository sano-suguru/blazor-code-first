using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class FrontMatterTests
{
    private const string Valid = "---\ntitle: Getting Started\norder: 10\ngroup: start\n---\n\n## Installation\n";

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
            "---\r\ntitle: T\r\norder: 1\r\ngroup: write\r\n---\r\n## Body\r\n", "a.md");

        Assert.Equal("T", fields.Title);
        Assert.Equal(1, fields.Order);
        Assert.Contains("## Body", body);
    }

    [Fact]
    public void Split_ValueContainingColon_KeepsFullValue()
    {
        var (fields, _) = FrontMatter.Split("---\ntitle: A: B\norder: 1\ngroup: write\n---\n", "a.md");
        Assert.Equal("A: B", fields.Title);
    }

    [Theory]
    [InlineData(" ---\ntitle: T\norder: 1\ngroup: write\n---\n")]
    [InlineData("--- \ntitle: T\norder: 1\ngroup: write\n---\n")]
    [InlineData("----\ntitle: T\norder: 1\ngroup: write\n---\n")]
    [InlineData("---x\ntitle: T\norder: 1\ngroup: write\n---\n")]
    [InlineData("---")]
    public void Split_MalformedOpeningDelimiter_Throws(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FrontMatter.Split(raw, "sample.md"));

        Assert.Contains("opening", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("---", ex.Message, StringComparison.Ordinal);
        Assert.Contains("sample.md", ex.Message);
    }

    [Theory]
    [InlineData("---\ntitle: T\norder: 1\ngroup: write\n--- \n")]
    [InlineData("---\ntitle: T\norder: 1\ngroup: write\n ---\n")]
    [InlineData("---\ntitle: T\norder: 1\ngroup: write\n----\n")]
    [InlineData("---\ntitle: T\norder: 1\ngroup: write\n---x\n")]
    public void Split_MalformedClosingDelimiter_Throws(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FrontMatter.Split(raw, "sample.md"));

        Assert.Contains("closing", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must be exactly", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("---", ex.Message, StringComparison.Ordinal);
        Assert.Contains("sample.md", ex.Message);
    }

    [Fact]
    public void Split_DelimiterLookingTextAfterClosingLine_RemainsBody()
    {
        var (_, body) = FrontMatter.Split("---\ntitle: T\norder: 1\ngroup: write\n---\n--- \n", "sample.md");

        Assert.Equal("--- \n", body);
    }

    [Fact]
    public void Split_ReadsGroup()
    {
        var (fields, _) = FrontMatter.Split(Valid, "getting-started.md");
        Assert.Equal(DocGroup.Start, fields.Group);
    }

    [Theory]
    [InlineData(DocGroup.Start)]
    [InlineData(DocGroup.Write)]
    [InlineData(DocGroup.Reference)]
    public void Split_EveryDeclaredGroup_IsAccepted(string group)
    {
        var (fields, _) = FrontMatter.Split($"---\ntitle: T\norder: 1\ngroup: {group}\n---\n", "a.md");
        Assert.Equal(group, fields.Group);
    }

    [Theory]
    [InlineData("---\ntitle: T\norder: 1\n---\n", "group")]                       // missing group
    [InlineData("---\ntitle: T\norder: 1\ngroup: guide\n---\n", "guide")]         // not one of the three
    [InlineData("---\ntitle: T\norder: 1\ngroup: Start\n---\n", "Start")]         // right word, wrong case
    [InlineData("---\ntitle: T\norder: 1\ngroup: start\ngroup: write\n---\n", "more than once")]
    public void Split_InvalidGroup_Throws(string raw, string messageFragment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FrontMatter.Split(raw, "sample.md"));
        Assert.Contains(messageFragment, ex.Message, StringComparison.Ordinal);
        Assert.Contains("sample.md", ex.Message);
    }

    [Theory]
    [InlineData("## No front matter\n", "front matter")]              // no front matter block at all
    [InlineData("---\norder: 1\ngroup: write\n---\n", "title")]                     // missing title
    [InlineData("---\ntitle: T\n---\n", "order")]                     // missing order
    [InlineData("---\ntitle: T\norder: 1\ngroup: write\nextra: x\n---\n", "extra")] // unknown key
    [InlineData("---\ntitle: T\norder: abc\n---\n", "order")]         // order is not an integer
    [InlineData("---\ntitle:   \norder: 1\ngroup: write\n---\n", "title")]          // title is empty
    [InlineData("---\ntitle: T\norder: 1\ngroup: write\n", "closing")]              // no closing ---
    [InlineData("---\ntitle: A\norder: 1\ngroup: write\ntitle: B\n---\n", "more than once")]  // duplicate title
    [InlineData("---\ntitle: T\norder: 1\ngroup: write\norder: 2\ngroup: write\n---\n", "more than once")]  // duplicate order
    public void Split_InvalidFrontMatter_Throws(string raw, string messageFragment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FrontMatter.Split(raw, "sample.md"));
        Assert.Contains(messageFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sample.md", ex.Message);
    }
}
