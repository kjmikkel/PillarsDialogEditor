# Security policy

## Reporting a vulnerability

Please **don't open a public issue** for a security problem. Report it privately through
GitHub instead:

1. Go to the repository's [**Security** tab](https://github.com/kjmikkel/PillarsDialogEditor/security).
2. Choose **Report a vulnerability**.
3. Describe the problem, how to reproduce it, and what an attacker could do with it.

Only I can see the report. I'll reply there, and once a fix is released I'll publish an
advisory that credits you, unless you'd rather not be named.

## What counts

Pillars Dialog Editor and Pillars Dialog Patcher are offline desktop tools that read and write
files in your game folder. The security problems that matter most are:

- a mod (`.dialogpack`, `.dialogproject` or `.patchlist`) that makes the editor, the Patch
  Manager or `dialog-patcher` write outside the game folder, run code, or damage files it
  shouldn't touch;
- a crash report or log that leaks more than it says it does;
- anything in the release archives that isn't what the source and `SHA256SUMS.txt` say it is.

Bugs that only lose your own unsaved work, or that need someone to already control your
computer, are ordinary bugs. Please use the
[bug report form](https://github.com/kjmikkel/PillarsDialogEditor/issues/new/choose) for those.

## Supported versions

Fixes go into the latest release of each tool. There are no backports to older versions.
