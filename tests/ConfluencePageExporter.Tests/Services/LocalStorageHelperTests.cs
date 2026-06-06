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
    public void ReadPageIdFromMarker_ShouldReturnNull_WhenDirectoryDoesNotExist()
    {
        var result = LocalStorageHelper.ReadPageIdFromMarker("X:\\not-existing-dir");

        result.ShouldBeNull();
    }

    [Fact]
    public void ReadPageIdFromMarker_ShouldReturnPageId_WhenMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), string.Empty);

        var result = LocalStorageHelper.ReadPageIdFromMarker(pageDir);

        result.ShouldBe("123");
    }

    [Fact]
    public void ReadPageIdFromMarker_ShouldReturnPageId_WhenVersionedMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), string.Empty);

        var result = LocalStorageHelper.ReadPageIdFromMarker(pageDir);

        result.ShouldBe("123");
    }

    [Fact]
    public void ReadPageMarkerInfo_ShouldReturnIdAndVersion_WhenVersionedMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id456_7"), string.Empty);

        var result = LocalStorageHelper.ReadPageMarkerInfo(pageDir);

        result.ShouldNotBeNull();
        result!.PageId.ShouldBe("456");
        result.Version.ShouldBe(7);
    }

    [Fact]
    public void ReadPageMarkerInfo_ShouldReturnNullVersion_WhenOldFormatMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id456"), string.Empty);

        var result = LocalStorageHelper.ReadPageMarkerInfo(pageDir);

        result.ShouldNotBeNull();
        result!.PageId.ShouldBe("456");
        result.Version.ShouldBeNull();
    }

    [Fact]
    public void ParseMarkerFileName_ShouldParseVersionedFormat()
    {
        var result = LocalStorageHelper.ParseMarkerFileName(".id12345_42");

        result.PageId.ShouldBe("12345");
        result.Version.ShouldBe(42);
    }

    [Fact]
    public void ParseMarkerFileName_ShouldParseOldFormat()
    {
        var result = LocalStorageHelper.ParseMarkerFileName(".id12345");

        result.PageId.ShouldBe("12345");
        result.Version.ShouldBeNull();
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
    public async Task WritePageIdMarkerAsync_ShouldWriteVersionedMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, "123", 5);

        File.Exists(Path.Combine(pageDir, ".id123_5")).ShouldBeTrue();
    }

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldReplaceOldMarker_WithVersionedMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), string.Empty);

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, "123", 3);

        File.Exists(Path.Combine(pageDir, ".id123")).ShouldBeFalse();
        File.Exists(Path.Combine(pageDir, ".id123_3")).ShouldBeTrue();
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
    public async Task ResolvePageIdAsync_ShouldReturnPageId_WhenExplicitIdProvided()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);

        var result = await LocalStorageHelper.ResolvePageIdAsync(mock.Object, "SPACE", "123", "Title");

        result.ShouldBe("123");
    }

    [Fact]
    public async Task ResolvePageIdAsync_ShouldResolveByTitle_WhenOnlyTitleProvided()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);
        mock.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Title")).ReturnsAsync("777");

        var result = await LocalStorageHelper.ResolvePageIdAsync(mock.Object, "SPACE", null, "Title");

        result.ShouldBe("777");
        mock.VerifyAll();
    }

    [Fact]
    public async Task ResolvePageIdAsync_ShouldReturnNull_WhenNoIdAndNoTitle()
    {
        var mock = new Mock<IConfluenceApiClient>(MockBehavior.Strict);

        var result = await LocalStorageHelper.ResolvePageIdAsync(mock.Object, "SPACE", null, null);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldStoreOriginalTitle()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, "123", 5, "Модуль \"Провайдеры\"");

        var markerPath = Path.Combine(pageDir, ".id123_5");
        File.Exists(markerPath).ShouldBeTrue();
        (await File.ReadAllTextAsync(markerPath, TestContext.Current.CancellationToken)).ShouldBe("Модуль \"Провайдеры\"");
    }

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldWriteEmptyContent_WhenNoTitle()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, "123", 5);

        var content = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id123_5"), TestContext.Current.CancellationToken);
        content.ShouldBeEmpty();
    }

    [Fact]
    public void ReadOriginalTitle_ShouldReturnTitle_WhenMarkerHasContent()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), "Original Title");

        var result = LocalStorageHelper.ReadOriginalTitle(pageDir);

        result.ShouldBe("Original Title");
    }

    [Fact]
    public void ReadOriginalTitle_ShouldReturnNull_WhenMarkerIsEmpty()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), string.Empty);

        var result = LocalStorageHelper.ReadOriginalTitle(pageDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void ReadOriginalTitle_ShouldReturnNull_WhenNoMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        var result = LocalStorageHelper.ReadOriginalTitle(pageDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void ReadOriginalTitle_ShouldReturnNull_WhenDirectoryDoesNotExist()
    {
        var result = LocalStorageHelper.ReadOriginalTitle("X:\\not-existing-dir");

        result.ShouldBeNull();
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

    // ── marker body: space (JSON) + legacy fallback ───────────────────────

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldWriteJsonBody_WhenSpaceProvided()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, "123", 5, "Модуль \"Провайдеры\"", "DEV");

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id123_5"), TestContext.Current.CancellationToken);
        body.ShouldContain("\"space\":\"DEV\"");
        body.ShouldContain("Модуль"); // readable Cyrillic, not \uXXXX-escaped
        LocalStorageHelper.ReadOriginalTitle(pageDir).ShouldBe("Модуль \"Провайдеры\"");
        LocalStorageHelper.ReadSpaceKey(pageDir).ShouldBe("DEV");
    }

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldWriteLegacyPlainTitle_WhenNoSpace()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, "123", 5, "Plain Title");

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id123_5"), TestContext.Current.CancellationToken);
        body.ShouldBe("Plain Title"); // unchanged on-disk format — zero churn until space is captured
        LocalStorageHelper.ReadSpaceKey(pageDir).ShouldBeNull();
    }

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldRoundTripJsonBody_WithSpecialCharsInTitle()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, "777", 2, "A \"quoted\" & <odd> title", "TEAM");

        var content = LocalStorageHelper.ReadMarkerContent(pageDir);
        content.Title.ShouldBe("A \"quoted\" & <odd> title");
        content.SpaceKey.ShouldBe("TEAM");
    }

    // ── marker body: content hash + normalization epoch ───────────────────

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldRoundTripHashAndEpoch()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(
            pageDir, "555", 7, "Title", "DEV", "sha256:abc123", 1);

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id555_7"), TestContext.Current.CancellationToken);
        body.ShouldContain("\"h\":\"sha256:abc123\"");
        body.ShouldContain("\"ne\":1");

        var content = LocalStorageHelper.ReadMarkerContent(pageDir);
        content.ContentHash.ShouldBe("sha256:abc123");
        content.NormalizationEpoch.ShouldBe(1);
        content.SpaceKey.ShouldBe("DEV");
    }

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldWriteJsonWithHash_EvenWithoutSpace()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await LocalStorageHelper.WritePageIdMarkerAsync(
            pageDir, "555", 7, "Title", spaceKey: null, contentHash: "sha256:deadbeef", normalizationEpoch: 1);

        var content = LocalStorageHelper.ReadMarkerContent(pageDir);
        content.ContentHash.ShouldBe("sha256:deadbeef");
        content.NormalizationEpoch.ShouldBe(1);
        content.SpaceKey.ShouldBeNull();
        content.Title.ShouldBe("Title");
    }

    [Fact]
    public async Task WritePageIdMarkerAsync_ShouldOmitEpochAndHash_WhenNoHash()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        // Epoch without a hash is meaningless and must not be persisted.
        await LocalStorageHelper.WritePageIdMarkerAsync(
            pageDir, "555", 7, "Title", "DEV", contentHash: null, normalizationEpoch: 1);

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id555_7"), TestContext.Current.CancellationToken);
        body.ShouldNotContain("\"ne\"");
        body.ShouldNotContain("\"h\"");
        LocalStorageHelper.ReadMarkerContent(pageDir).NormalizationEpoch.ShouldBeNull();
    }

    [Fact]
    public void ReadMarkerContent_ShouldParseHashAndEpoch()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"),
            """{"title":"My Page","space":"DOCS","h":"sha256:ff00","ne":1}""");

        var content = LocalStorageHelper.ReadMarkerContent(pageDir);

        content.ContentHash.ShouldBe("sha256:ff00");
        content.NormalizationEpoch.ShouldBe(1);
    }

    [Fact]
    public void ReadMarkerContent_ShouldReturnNullHash_ForLegacyPlainTitleMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), "Legacy Title");

        var content = LocalStorageHelper.ReadMarkerContent(pageDir);
        content.ContentHash.ShouldBeNull();
        content.NormalizationEpoch.ShouldBeNull();
        content.Title.ShouldBe("Legacy Title");
    }

    [Fact]
    public void ReadSpaceKey_ShouldReturnNull_ForLegacyPlainTitleMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), "Legacy Title");

        LocalStorageHelper.ReadSpaceKey(pageDir).ShouldBeNull();
        LocalStorageHelper.ReadOriginalTitle(pageDir).ShouldBe("Legacy Title");
    }

    [Fact]
    public void ReadMarkerContent_ShouldParseJsonBody()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), """{"title":"My Page","space":"DOCS"}""");

        var content = LocalStorageHelper.ReadMarkerContent(pageDir);

        content.Title.ShouldBe("My Page");
        content.SpaceKey.ShouldBe("DOCS");
    }

    [Fact]
    public void ReadMarkerContent_ShouldReturnEmpty_WhenNoMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        var content = LocalStorageHelper.ReadMarkerContent(pageDir);

        content.Title.ShouldBeNull();
        content.SpaceKey.ShouldBeNull();
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
