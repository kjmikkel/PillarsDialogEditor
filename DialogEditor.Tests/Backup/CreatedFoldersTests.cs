using DialogEditor.Core.Backup;

namespace DialogEditor.Tests.Backup;

/// Issue 125: a restore must take away the folders its write created, and only those.
public class CreatedFoldersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("createdfolders_").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    [Fact]
    public void MissingAncestors_ListsEveryFolderAWriteWouldCreate_DeepestFirst()
    {
        var file = Path.Combine(_root, "a", "b", "f.wem");

        Assert.Equal(
            [Path.Combine(_root, "a", "b"), Path.Combine(_root, "a")],
            CreatedFolders.MissingAncestors(file));
    }

    [Fact]
    public void MissingAncestors_IsEmptyWhenTheFolderExists()
    {
        Assert.Empty(CreatedFolders.MissingAncestors(Path.Combine(_root, "f.wem")));
    }

    [Fact]
    public void RemoveIfEmpty_RemovesNestedEmptyFolders_WhateverTheOrderGiven()
    {
        var outer = Path.Combine(_root, "a");
        var inner = Path.Combine(outer, "b");
        Directory.CreateDirectory(inner);

        CreatedFolders.RemoveIfEmpty([outer, inner]);   // parent listed first on purpose

        Assert.False(Directory.Exists(outer));
    }

    [Fact]
    public void RemoveIfEmpty_KeepsAFolderThatStillHoldsSomething()
    {
        var outer = Path.Combine(_root, "a");
        var inner = Path.Combine(outer, "b");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(outer, "keep.txt"), "x");

        CreatedFolders.RemoveIfEmpty([inner, outer]);

        Assert.False(Directory.Exists(inner));
        Assert.True(File.Exists(Path.Combine(outer, "keep.txt")));
    }

    [Fact]
    public void RemoveIfEmpty_IgnoresFoldersThatAreAlreadyGone()
    {
        CreatedFolders.RemoveIfEmpty([Path.Combine(_root, "never_made")]);   // must not throw
    }
}
