using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class LocalStorageHelperTests
{
    [Fact]
    public void SanitizeFileName_ShouldRemoveInvalidCharacters()
    {
        var invalid = Path.GetInvalidFileNameChars()[0];
        var input = $"ab{invalid}cd";

        var result = LocalStorageHelper.SanitizeFileName(input);

        result.Should().NotContain(invalid.ToString());
        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ReadPageIdFromMarker_ShouldReturnNull_WhenDirectoryDoesNotExist()
    {
        var result = LocalStorageHelper.ReadPageIdFromMarker("X:\\not-existing-dir");

        result.Should().BeNull();
    }

    [Fact]
    public void ReadPageIdFromMarker_ShouldReturnPageId_WhenMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), string.Empty);

        var result = LocalStorageHelper.ReadPageIdFromMarker(pageDir);

        result.Should().Be("123");
    }

    [Fact]
    public async Task ReadPageContent_ShouldReturnContent_WhenIndexExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, "index.html"), "<p>hello</p>");

        var result = await LocalStorageHelper.ReadPageContent(pageDir);

        result.Should().Be("<p>hello</p>");
    }

    [Fact]
    public async Task ReadPageContent_ShouldThrow_WhenIndexDoesNotExist()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        var act = async () => await LocalStorageHelper.ReadPageContent(pageDir);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReadLocalPageContentOrNull_ShouldReturnNull_WhenIndexDoesNotExist()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        var result = await LocalStorageHelper.ReadLocalPageContentOrNull(pageDir);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadLocalPageContentOrNull_ShouldReturnContent_WhenIndexExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, "index.html"), "<p>abc</p>");

        var result = await LocalStorageHelper.ReadLocalPageContentOrNull(pageDir);

        result.Should().Be("<p>abc</p>");
    }

    [Fact]
    public void GetAttachmentFiles_ShouldExcludeIndexAndIdMarkers()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, "index.html"), "x");
        File.WriteAllText(Path.Combine(pageDir, ".id11"), string.Empty);
        File.WriteAllText(Path.Combine(pageDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(pageDir, "b.png"), "b");

        var result = LocalStorageHelper.GetAttachmentFiles(pageDir).Select(Path.GetFileName).ToArray();

        result.Should().BeEquivalentTo(["a.txt", "b.png"]);
    }

    [Fact]
    public void GetPageSubdirectories_ShouldReturnChildDirectories()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        Directory.CreateDirectory(Path.Combine(root, "A"));
        Directory.CreateDirectory(Path.Combine(root, "B"));

        var result = LocalStorageHelper.GetPageSubdirectories(root).Select(Path.GetFileName).ToArray();

        result.Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldThrow_WhenDirectoryDoesNotExist()
    {
        var act = () => LocalStorageHelper.ValidateSourceDirectory("X:\\does-not-exist");

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldThrow_WhenIndexDoesNotExist()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");

        var act = () => LocalStorageHelper.ValidateSourceDirectory(root);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldPass_WhenIndexExists()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        File.WriteAllText(Path.Combine(root, "index.html"), "<p>x</p>");

        var act = () => LocalStorageHelper.ValidateSourceDirectory(root);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldPass_WhenPathHasTrailingSeparator()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("PageName");
        File.WriteAllText(Path.Combine(root, "index.html"), "<p>x</p>");
        var pathWithTrailing = root + Path.DirectorySeparatorChar;

        var act = () => LocalStorageHelper.ValidateSourceDirectory(pathWithTrailing);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetPageTitleFromDirectory_ShouldReturnFolderName_WhenPathHasTrailingSeparator()
    {
        var path = "folder" + Path.DirectorySeparatorChar + "PageName" + Path.DirectorySeparatorChar;

        var result = LocalStorageHelper.GetPageTitleFromDirectory(path);

        result.Should().Be("PageName");
    }

    [Fact]
    public void GetPageTitleFromDirectory_ShouldReturnFolderName_WhenPathHasNoTrailingSeparator()
    {
        var path = "folder" + Path.DirectorySeparatorChar + "PageName";

        var result = LocalStorageHelper.GetPageTitleFromDirectory(path);

        result.Should().Be("PageName");
    }

    [Fact]
    public void NormalizeRelativePath_ShouldUseForwardSlashes()
    {
        var input = $"one{Path.DirectorySeparatorChar}two{Path.AltDirectorySeparatorChar}three";

        var result = LocalStorageHelper.NormalizeRelativePath(input);

        result.Should().Be("one/two/three");
    }

    [Fact]
    public void EnumeratePageDirectories_ShouldReturnOnlyDirectoriesWithIndex()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        File.WriteAllText(Path.Combine(root, "index.html"), "root");

        var childWithIndex = Path.Combine(root, "Child1");
        Directory.CreateDirectory(childWithIndex);
        File.WriteAllText(Path.Combine(childWithIndex, "index.html"), "child");

        var childWithoutIndex = Path.Combine(root, "Child2");
        Directory.CreateDirectory(childWithoutIndex);

        var result = LocalStorageHelper.EnumeratePageDirectories(root)
            .Select(Path.GetFullPath)
            .ToArray();

        result.Should().Contain(Path.GetFullPath(root));
        result.Should().Contain(Path.GetFullPath(childWithIndex));
        result.Should().NotContain(Path.GetFullPath(childWithoutIndex));
    }

    [Fact]
    public void PathsEqual_ShouldIgnoreTrailingSeparatorsAndCase()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        var withTrailing = root + Path.DirectorySeparatorChar;
        var upper = root.ToUpperInvariant();

        LocalStorageHelper.PathsEqual(withTrailing, upper).Should().BeTrue();
    }

    [Fact]
    public void BuildPageDirectoryIndex_ShouldCollectMarkersAndIgnoreDuplicates()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        var first = Path.Combine(root, "A");
        var second = Path.Combine(root, "B");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, ".id100"), string.Empty);
        File.WriteAllText(Path.Combine(second, ".id100"), string.Empty);
        File.WriteAllText(Path.Combine(second, ".id200"), string.Empty);

        var index = LocalStorageHelper.BuildPageDirectoryIndex(root);

        index.Should().ContainKey("100");
        index.Should().ContainKey("200");
        index["100"].Should().Be(Path.GetFullPath(first));
    }

    [Fact]
    public void UpdateDirectoryIndexPaths_ShouldRewriteRootAndChildren()
    {
        using var temp = new TempDirectoryScope();
        var oldRoot = Path.GetFullPath(temp.CreateDirectory("OldRoot"));
        var child = Path.Combine(oldRoot, "Child");
        var newRoot = Path.GetFullPath(Path.Combine(temp.RootPath, "NewRoot"));

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = oldRoot,
            ["2"] = child
        };

        LocalStorageHelper.UpdateDirectoryIndexPaths(index, oldRoot, newRoot);

        index["1"].Should().Be(newRoot);
        index["2"].Should().Be(Path.Combine(newRoot, "Child"));
    }

    [Fact]
    public async Task ResolvePageIdAsync_ShouldReturnPageId_WhenExplicitIdProvided()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);

        var result = await LocalStorageHelper.ResolvePageIdAsync(mock.Object, "SPACE", "123", "Title");

        result.Should().Be("123");
    }

    [Fact]
    public async Task ResolvePageIdAsync_ShouldResolveByTitle_WhenOnlyTitleProvided()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);
        mock.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Title")).ReturnsAsync("777");

        var result = await LocalStorageHelper.ResolvePageIdAsync(mock.Object, "SPACE", null, "Title");

        result.Should().Be("777");
        mock.VerifyAll();
    }

    [Fact]
    public async Task ResolvePageIdAsync_ShouldReturnNull_WhenNoIdAndNoTitle()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);

        var result = await LocalStorageHelper.ResolvePageIdAsync(mock.Object, "SPACE", null, null);

        result.Should().BeNull();
    }
}
