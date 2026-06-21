using DialogEditor.Core.Editing;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class DialogProjectAnnotationTests
{
    private static AnnotationSnapshot MakeSnap(string id, string title = "Note") =>
        new(id, title, "body", "Yellow", 100, 200, 300, 150);

    [Fact]
    public void GetAnnotations_NewProject_ReturnsNull()
    {
        var project = DialogProject.Empty("test");
        Assert.Null(project.GetAnnotations("conv1"));
    }

    [Fact]
    public void WithAnnotations_Then_GetAnnotations_ReturnsSameList()
    {
        var snaps   = new[] { MakeSnap("a1"), MakeSnap("a2") };
        var project = DialogProject.Empty("test").WithAnnotations("conv1", snaps);

        var result = project.GetAnnotations("conv1");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Contains(result, s => s.Id == "a1");
        Assert.Contains(result, s => s.Id == "a2");
    }

    [Fact]
    public void WithAnnotations_OtherConversation_ReturnsNull()
    {
        var project = DialogProject.Empty("test")
            .WithAnnotations("conv1", [MakeSnap("a1")]);

        Assert.Null(project.GetAnnotations("conv2"));
    }

    [Fact]
    public void WithAnnotations_EmptyList_RemovesKey()
    {
        var project = DialogProject.Empty("test")
            .WithAnnotations("conv1", [MakeSnap("a1")])
            .WithAnnotations("conv1", []);

        var result = project.GetAnnotations("conv1");
        Assert.True(result is null || result.Count == 0);
    }

    [Fact]
    public void MergeWith_BothHaveAnnotations_IncomingWinsOnIdCollision()
    {
        var mine   = MakeSnap("a1", "mine");
        var theirs = MakeSnap("a1", "theirs");
        var only   = MakeSnap("a2", "only-theirs");

        var mine_project   = DialogProject.Empty("p").WithAnnotations("conv1", [mine]);
        var theirs_project = DialogProject.Empty("p").WithAnnotations("conv1", [theirs, only]);

        var merged = mine_project.MergeWith(theirs_project);
        var result = merged.GetAnnotations("conv1")!;

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Id == "a1" && s.Title == "theirs");
        Assert.Contains(result, s => s.Id == "a2");
    }

    [Fact]
    public void MergeWith_OnlyMineHasAnnotations_PreservesMine()
    {
        var project = DialogProject.Empty("p").WithAnnotations("conv1", [MakeSnap("a1")]);
        var other   = DialogProject.Empty("p");

        var merged = project.MergeWith(other);

        Assert.NotNull(merged.GetAnnotations("conv1"));
    }

    [Fact]
    public void MergeWith_OnlyTheirsHasAnnotations_AddsTheirs()
    {
        var project = DialogProject.Empty("p");
        var other   = DialogProject.Empty("p").WithAnnotations("conv1", [MakeSnap("a1")]);

        var merged = project.MergeWith(other);

        Assert.NotNull(merged.GetAnnotations("conv1"));
    }

    [Fact]
    public void WithAnnotations_PreservesOtherConversations()
    {
        var project = DialogProject.Empty("p")
            .WithAnnotations("conv1", [MakeSnap("a1")])
            .WithAnnotations("conv2", [MakeSnap("b1")]);

        Assert.NotNull(project.GetAnnotations("conv1"));
        Assert.NotNull(project.GetAnnotations("conv2"));
    }
}
