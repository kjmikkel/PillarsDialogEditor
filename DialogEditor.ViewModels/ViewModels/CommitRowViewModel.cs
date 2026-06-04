using DialogEditor.Patch.Diff;

namespace DialogEditor.ViewModels;

/// One commit row. Presentation-free Date (DateTimeOffset); the view formats it.
public class CommitRowViewModel(CommitInfo commit)
{
    public string         Sha      => commit.Sha;
    public string         ShortSha => commit.ShortSha;
    public string         Author   => commit.Author;
    public DateTimeOffset Date     => commit.Date;
    public string         Subject  => commit.Subject;
}
