---
title: "Write 'GitHub issue NNN', never '#NNN', in production code comments"
author: Mikkel
date: 2026-10-24
branch: fix/237-cli-unknown-flags
cost: { minutes: 15, tokens: 68452 }
id: l-0d9a7d11
scope: repo
status: active
touches:
  - DialogEditor.Tests/PatchCli/PatcherCommandTests.cs
  - DialogEditor.PatchCli/PatcherCommand.cs
  - DialogEditor.Tests/Theming/NoStrayHexTests.cs
---

## Watch out
- `DialogEditor.Tests/Theming/NoStrayHexTests.cs` (`NoHexLiteralsInProductionCode`) scans production .cs/.axaml for hex colour literals, and a 3- or 6-digit issue reference like `#237` matches `#RGB`. The test fails naming the comment line, which looks unrelated to theming at first.
- The existing convention in production comments is "GitHub issue 62" / "issue 76" in prose, which avoids the match. Commit messages and test files are not scanned, so `#NNN` is fine there.
