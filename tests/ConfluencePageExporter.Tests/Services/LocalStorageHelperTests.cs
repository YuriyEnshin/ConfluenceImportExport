using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class LocalStorageHelperTests
{
    [Fact]
    public void SanitizeFileName_ShouldReplaceEachInvalidCharWithUnderscore()
    {
        var invalid = Path.GetInvalidFileNameChars()[0];
        var input = $"ab{invalid}cd";

        var result = LocalStorageHelper.SanitizeFileName(input);

        result.ShouldBe("ab_cd");
    }

    [Fact]
    public void SanitizeFileName_ShouldReplaceTrailingInvalidCharConsistently()
    {
        // " is invalid only on Windows; Linux/macOS accept it as a filename char.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Quote character is a valid filename char on non-Windows filesystems.");

        var result = LocalStorageHelper.SanitizeFileName("Модуль \"Провайдеры\"");

        result.ShouldBe("Модуль _Провайдеры_");
    }

    [Fact]
    public void SanitizeFileName_ShouldReplaceMultipleAdjacentInvalidChars()
    {
        // < and > are invalid only on Windows; Linux/macOS accept them as filename chars.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Angle bracket characters are valid filename chars on non-Windows filesystems.");

        var result = LocalStorageHelper.SanitizeFileName("a<>b");

        result.ShouldBe("a__b");
    }

    [Theory]
    [InlineData("Title.", "Title")]
    [InlineData("Title...", "Title")]
    [InlineData("Title. ", "Title")]
    [InlineData("Title ", "Title")]
    [InlineData("  Title  ", "  Title")]
    [InlineData("...", "_")]
    public void SanitizeFileName_ShouldTrimTrailingDotsAndSpaces(string input, string expected)
    {
        var result = LocalStorageHelper.SanitizeFileName(input);

        result.ShouldBe(expected);
    }

    [Fact]
    public async Task ReadPageContent_ShouldReturnContent_WhenIndexExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, "index.html"), "<p>hello</p>");

        var result = await LocalStorageHelper.ReadPageContent(pageDir);

        result.ShouldBe("<p>hello</p>");
    }

    [Fact]
    public async Task ReadPageContent_ShouldThrow_WhenIndexDoesNotExist()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        var act = async () => await LocalStorageHelper.ReadPageContent(pageDir);

        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task ReadLocalPageContentOrNull_ShouldReturnNull_WhenIndexDoesNotExist()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        var result = await LocalStorageHelper.ReadLocalPageContentOrNull(pageDir);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ReadLocalPageContentOrNull_ShouldReturnContent_WhenIndexExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, "index.html"), "<p>abc</p>");

        var result = await LocalStorageHelper.ReadLocalPageContentOrNull(pageDir);

        result.ShouldBe("<p>abc</p>");
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

        result.ShouldBe(["a.txt", "b.png"], ignoreOrder: true);
    }

    [Fact]
    public void GetPageSubdirectories_ShouldReturnChildDirectories()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        Directory.CreateDirectory(Path.Combine(root, "A"));
        Directory.CreateDirectory(Path.Combine(root, "B"));

        var result = LocalStorageHelper.GetPageSubdirectories(root).Select(Path.GetFileName).ToArray();

        result.ShouldBe(["A", "B"], ignoreOrder: true);
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldThrow_WhenDirectoryDoesNotExist()
    {
        var act = () => LocalStorageHelper.ValidateSourceDirectory("X:\\does-not-exist");

        Should.Throw<DirectoryNotFoundException>(act);
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldThrow_WhenIndexDoesNotExist()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");

        var act = () => LocalStorageHelper.ValidateSourceDirectory(root);

        Should.Throw<FileNotFoundException>(act);
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldPass_WhenIndexExists()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        File.WriteAllText(Path.Combine(root, "index.html"), "<p>x</p>");

        var act = () => LocalStorageHelper.ValidateSourceDirectory(root);

        Should.NotThrow(act);
    }

    [Fact]
    public void ValidateSourceDirectory_ShouldPass_WhenPathHasTrailingSeparator()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("PageName");
        File.WriteAllText(Path.Combine(root, "index.html"), "<p>x</p>");
        var pathWithTrailing = root + Path.DirectorySeparatorChar;

        var act = () => LocalStorageHelper.ValidateSourceDirectory(pathWithTrailing);

        Should.NotThrow(act);
    }

    [Fact]
    public void GetPageTitleFromDirectory_ShouldReturnFolderName_WhenPathHasTrailingSeparator()
    {
        var path = "folder" + Path.DirectorySeparatorChar + "PageName" + Path.DirectorySeparatorChar;

        var result = LocalStorageHelper.GetPageTitleFromDirectory(path);

        result.ShouldBe("PageName");
    }

    [Fact]
    public void GetPageTitleFromDirectory_ShouldReturnFolderName_WhenPathHasNoTrailingSeparator()
    {
        var path = "folder" + Path.DirectorySeparatorChar + "PageName";

        var result = LocalStorageHelper.GetPageTitleFromDirectory(path);

        result.ShouldBe("PageName");
    }

    [Fact]
    public void NormalizeRelativePath_ShouldUseForwardSlashes()
    {
        var input = $"one{Path.DirectorySeparatorChar}two{Path.AltDirectorySeparatorChar}three";

        var result = LocalStorageHelper.NormalizeRelativePath(input);

        result.ShouldBe("one/two/three");
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

        result.ShouldContain(Path.GetFullPath(root));
        result.ShouldContain(Path.GetFullPath(childWithIndex));
        result.ShouldNotContain(Path.GetFullPath(childWithoutIndex));
    }

    [Fact]
    public void PathsEqual_ShouldIgnoreTrailingSeparatorsAndCase()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        var withTrailing = root + Path.DirectorySeparatorChar;
        var upper = root.ToUpperInvariant();

        LocalStorageHelper.PathsEqual(withTrailing, upper).ShouldBeTrue();
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

        index.ShouldContainKey("100");
        index.ShouldContainKey("200");
        index["100"].ShouldBe(Path.GetFullPath(first));
    }

    [Fact]
    public void BuildPageDirectoryIndex_ShouldParseVersionedMarkers()
    {
        using var temp = new TempDirectoryScope();
        var root = temp.CreateDirectory("Root");
        var pageA = Path.Combine(root, "A");
        Directory.CreateDirectory(pageA);
        File.WriteAllText(Path.Combine(pageA, ".id100_5"), string.Empty);

        var index = LocalStorageHelper.BuildPageDirectoryIndex(root);

        index.ShouldContainKey("100");
        index["100"].ShouldBe(Path.GetFullPath(pageA));
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

        index["1"].ShouldBe(newRoot);
        index["2"].ShouldBe(Path.Combine(newRoot, "Child"));
    }

    [Fact]
    public void GetPageTitle_ShouldReturnOriginalTitle_WhenFolderMatchesSanitized()
    {
        // Setup presumes " gets sanitized into _, which only happens on Windows.
        // On Linux/macOS the original title would produce a folder named Модуль "Провайдеры"
        // directly, so the pre-sanitized folder setup below is not a natural state.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Quote character is a valid filename char on non-Windows filesystems.");

        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Модуль _Провайдеры_");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), "Модуль \"Провайдеры\"");

        var result = LocalStorageHelper.GetPageTitle(pageDir);

        result.ShouldBe("Модуль \"Провайдеры\"");
    }

    [Fact]
    public void GetPageTitle_ShouldReturnFolderName_WhenFolderRenamed()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Новый модуль");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), "Модуль \"Провайдеры\"");

        var result = LocalStorageHelper.GetPageTitle(pageDir);

        result.ShouldBe("Новый модуль");
    }

    [Fact]
    public void GetPageTitle_ShouldReturnFolderName_WhenMarkerIsEmpty()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("SomeFolder");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), string.Empty);

        var result = LocalStorageHelper.GetPageTitle(pageDir);

        result.ShouldBe("SomeFolder");
    }

    [Fact]
    public void GetPageTitle_ShouldReturnFolderName_WhenNoMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("SomeFolder");

        var result = LocalStorageHelper.GetPageTitle(pageDir);

        result.ShouldBe("SomeFolder");
    }

    [Fact]
    public void GetPageTitle_ShouldReturnOriginalTitle_WhenTitleHasNoSpecialChars()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Simple Title");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), "Simple Title");

        var result = LocalStorageHelper.GetPageTitle(pageDir);

        result.ShouldBe("Simple Title");
    }

    [Fact]
    public void GetPageTitle_ShouldReturnFolderName_WhenMarkerContentEdited()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Модуль _Провайдеры_");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), "Другой заголовок");

        var result = LocalStorageHelper.GetPageTitle(pageDir);

        result.ShouldBe("Модуль _Провайдеры_");
    }

    [Fact]
    public void GetPageTitle_ShouldReturnOriginalTitle_FromJsonMarkerBody()
    {
        // " is sanitised to _ only on Windows, where the folder name matches the
        // sanitised title and GetPageTitle should restore the original from JSON.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Quote character is a valid filename char on non-Windows filesystems.");

        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Модуль _Провайдеры_");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), """{"title":"Модуль \"Провайдеры\"","space":"DEV"}""");

        var result = LocalStorageHelper.GetPageTitle(pageDir);

        result.ShouldBe("Модуль \"Провайдеры\"");
    }
}
