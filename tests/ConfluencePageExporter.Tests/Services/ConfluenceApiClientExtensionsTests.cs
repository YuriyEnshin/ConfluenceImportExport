using ConfluencePageExporter.Services;
using Shouldly;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class ConfluenceApiClientExtensionsTests
{
    [Fact]
    public async Task ResolvePageIdAsync_ShouldReturnPageId_WhenExplicitIdProvided()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);

        var result = await mock.Object.ResolvePageIdAsync("SPACE", "123", "Title");

        result.ShouldBe("123");
    }

    [Fact]
    public async Task ResolvePageIdAsync_ShouldResolveByTitle_WhenOnlyTitleProvided()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);
        mock.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Title")).ReturnsAsync("777");

        var result = await mock.Object.ResolvePageIdAsync("SPACE", null, "Title");

        result.ShouldBe("777");
        mock.VerifyAll();
    }

    [Fact]
    public async Task ResolvePageIdAsync_ShouldReturnNull_WhenNoIdAndNoTitle()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);

        var result = await mock.Object.ResolvePageIdAsync("SPACE", null, null);

        result.ShouldBeNull();
    }
}
