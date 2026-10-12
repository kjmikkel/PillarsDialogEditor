---
name: issue-screenshots
description: Attach screenshots or other images from this computer to a GitHub issue or PR, either as a comment or embedded in a new issue's body, using only the `gh` CLI. Use whenever the user asks to add, attach, upload, or include a screenshot/image in an issue or PR, says "put my latest screenshot on #NN", "attach what's on my clipboard", or when filing an issue where a screenshot taken during verification (e.g. by the running-the-app skill) would show the problem.
---

# Attaching screenshots to GitHub issues

`gh` cannot upload issue attachments (the drag-and-drop `user-attachments` endpoint
is not public API). `Add-IssueScreenshot.ps1` (next to this file) works around it:
it commits the image to an orphan `issue-assets` branch via the Contents API and
links it as `https://github.com/<repo>/raw/issue-assets/<path>`. The branch is
created server-side on first use; the local checkout is never touched.

## Before uploading: look at the image

Uploads land in git history, and **in a public repo they are world-readable and
effectively permanent** (deleting the file does not remove it from history).
Read every image with the Read tool first and check for:

- absolute local paths, usernames, or machine names (title bars, file dialogs, terminals)
- anything from the user's game installs or personal files, email addresses, tokens

If anything is present, stop and tell the user what you saw; do not crop or
upload without their go-ahead. Check visibility with
`gh repo view --json visibility -q .visibility` when unsure.

## Usage (pwsh 7, run from inside the repo)

```powershell
$s = "<this skill's directory>/Add-IssueScreenshot.ps1"
& $s -Issue 42                                    # newest image in Pictures\Screenshots
& $s -Issue 42 -Clipboard                         # image on the clipboard (Win+Shift+S)
& $s -Issue 42 -Path a.png, b.png -Caption 'Before / after'
& $s -Path a.png                                  # no issue: prints markdown only
```

- With `-Issue`, posts one comment containing the caption and all images.
- Without `-Issue` (or with `-NoComment`), prints the markdown, so you can embed it in
  `gh issue create --body` or `gh issue edit --body`. For a new issue, upload
  first, then create the issue with the markdown in its body.
- When the user says "my screenshot" without a path, the default (newest file in
  `Pictures\Screenshots`) is usually right, but say which file you picked.
- Screenshots you capture yourself (e.g. via `running-the-app`) go in with `-Path`.

## Notes

- Repo is detected from the current directory; override with `-Repo owner/name`.
- Branch name is configurable with `-Branch`; keep the default unless the repo already uses it for something else.
- Files are stored as `issue-<N>/<timestamp>-<name>` (or `unfiled/...`), so re-running never collides.
