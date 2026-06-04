using System.Runtime.InteropServices;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class PathSandboxTests
{
    [Fact]
    public void Constructor_ShouldThrow_WhenRootDirIsNull()
    {
        Action act = () => _ = new PathSandbox(null!);
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRootDirIsEmpty()
    {
        Action act = () => _ = new PathSandbox("");
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void Resolve_ShouldAnchorRelativePath_AgainstRootDir()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        var resolved = sandbox.Resolve("sub/dir");

        resolved.ShouldStartWith(scope.RootPath);
        resolved.ShouldEndWith($"sub{Path.DirectorySeparatorChar}dir");
    }

    [Fact]
    public void Resolve_ShouldAcceptAbsolutePathInsideRoot()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);
        var inside = Path.Combine(scope.RootPath, "child");

        var resolved = sandbox.Resolve(inside);

        resolved.ShouldBe(Path.GetFullPath(inside));
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenAbsolutePathEscapesRootViaParentTraversal()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);
        var outside = Path.Combine(scope.RootPath, "..", "evil");

        Action act = () => sandbox.Resolve(outside);

        Should.Throw<OutOfSandboxException>(act);
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenRelativePathEscapesRootViaParentTraversal()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        Action act = () => sandbox.Resolve("../../../etc/passwd");

        Should.Throw<OutOfSandboxException>(act);
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenAbsolutePathOutsideRoot()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);
        var unrelated = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\Windows\System32"
            : "/etc";

        Action act = () => sandbox.Resolve(unrelated);

        Should.Throw<OutOfSandboxException>(act);
    }

    [Fact]
    public void Resolve_ShouldAcceptRootDirItself()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        var resolved = sandbox.Resolve(scope.RootPath);

        resolved.ShouldBe(Path.GetFullPath(scope.RootPath));
    }

    [Fact]
    public void Resolve_ShouldNotConfuseSiblingPrefix()
    {
        // A sibling directory whose name starts with the root's name must
        // not be accepted: "/tmp/sandbox-x" should not match "/tmp/sandbox".
        using var scope = new TempDirectoryScope();
        var sibling = scope.RootPath + "-evil";
        Directory.CreateDirectory(sibling);
        try
        {
            var sandbox = new PathSandbox(scope.RootPath);
            Action act = () => sandbox.Resolve(sibling);
            Should.Throw<OutOfSandboxException>(act);
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenUserPathIsEmpty()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        Action act = () => sandbox.Resolve("");

        Should.Throw<ArgumentException>(act);
    }

    // ── Trailing-separator robustness (regression: in v2.7.0 the agent
    // reported OUT_OF_SANDBOX for outputDir="." when the operator started
    // the server with `--root-dir D:\…\Confluence\` because Path.GetFullPath
    // preserved the trailing slash on the stored root, while resolving "."
    // against that root produced a path without the trailing slash.) ────

    [Fact]
    public void Constructor_ShouldNormaliseRoot_WhenTrailingSeparatorPresent()
    {
        using var scope = new TempDirectoryScope();
        var rootWithSlash = scope.RootPath + Path.DirectorySeparatorChar;

        var sandbox = new PathSandbox(rootWithSlash);

        sandbox.RootDir.ShouldNotEndWith(Path.DirectorySeparatorChar.ToString());
        sandbox.RootDir.ShouldBe(Path.GetFullPath(scope.RootPath).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Resolve_ShouldAcceptDotPath_WhenRootHasTrailingSeparator()
    {
        using var scope = new TempDirectoryScope();
        var rootWithSlash = scope.RootPath + Path.DirectorySeparatorChar;
        var sandbox = new PathSandbox(rootWithSlash);

        var resolved = sandbox.Resolve(".");

        resolved.ShouldBe(Path.GetFullPath(scope.RootPath));
    }

    [Fact]
    public void Resolve_ShouldAcceptDotPath_WhenRootHasNoTrailingSeparator()
    {
        // Sanity: the no-trailing form must keep working — the fix shouldn't
        // shift behaviour for the previously-working case.
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        var resolved = sandbox.Resolve(".");

        resolved.ShouldBe(Path.GetFullPath(scope.RootPath));
    }

    [Fact]
    public void Resolve_ShouldAcceptDotSlashSubdir_WhenRootHasTrailingSeparator()
    {
        using var scope = new TempDirectoryScope();
        var rootWithSlash = scope.RootPath + Path.DirectorySeparatorChar;
        var sandbox = new PathSandbox(rootWithSlash);

        var resolved = sandbox.Resolve("./child");

        resolved.ShouldBe(Path.Combine(Path.GetFullPath(scope.RootPath), "child"));
    }
}
