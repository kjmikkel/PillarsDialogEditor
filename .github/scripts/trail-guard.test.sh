#!/usr/bin/env bash
# Tests for trail-guard.sh. Run: bash .github/scripts/trail-guard.test.sh
set -u
guard="$(dirname "$0")/trail-guard.sh"
failures=0

expect() { # expect <pass|fail> <description> <author> <owner> <changed files...>
  local want=$1 desc=$2 author=$3 owner=$4; shift 4
  if printf '%s\n' "$@" | bash "$guard" "$author" "$owner" >/dev/null 2>&1; then got=pass; else got=fail; fi
  if [ "$got" = "$want" ]; then echo "ok   $desc"; else echo "FAIL $desc (wanted $want, got $got)"; failures=$((failures + 1)); fi
}

expect pass "owner may change .trail/"            kjmikkel kjmikkel .trail/learnings/a.md
expect pass "owner may change agent config"       kjmikkel kjmikkel CLAUDE.md .claude/settings.json
expect pass "others may change files outside"     someone  kjmikkel DialogEditor.Core/Models/Conversation.cs README.md
expect pass "others may touch a look-alike path"  someone  kjmikkel docs/.trail/x.md .trailer DialogEditor.Avalonia/dist/x.json
expect pass "no changed files"                    someone  kjmikkel ""
expect fail "others may not add a learning"       someone  kjmikkel README.md .trail/learnings/a.md
expect fail "others may not change the hook"      someone  kjmikkel .trail/hook.cjs
expect fail "a look-alike login is not the owner" KJMIKKEL-fake kjmikkel .trail/hook.cjs

# hook.cjs imports trail from <repo>/node_modules and, last, <repo>/dist/claude
expect fail "others may not plant a trail package" someone kjmikkel node_modules/@trailhq/trail-code/package.json
expect fail "others may not plant dist/claude"     someone kjmikkel dist/claude/hooks.js

# Windows checkouts are case-insensitive and drop trailing dots and spaces
expect fail "upper-case .TRAIL/"                  someone  kjmikkel .TRAIL/learnings/a.md
expect fail "trailing dot .trail./"               someone  kjmikkel .trail./learnings/a.md
expect fail "trailing space '.trail /'"           someone  kjmikkel ".trail /learnings/a.md"
expect fail "8.3 short name TRAIL~1/"             someone  kjmikkel TRAIL~1/learnings/a.md

# other files Claude Code loads as instructions, hooks or MCP servers
expect fail "others may not change .claude/"      someone  kjmikkel .claude/settings.json
expect fail "others may not change nested .claude/" someone kjmikkel DialogEditor.Core/.claude/settings.json
expect fail "others may not change CLAUDE.md"     someone  kjmikkel DialogEditor.Core/CLAUDE.md
expect fail "others may not change CLAUDE.local.md" someone kjmikkel CLAUDE.local.md
expect fail "others may not change AGENTS.md"     someone  kjmikkel AGENTS.md
expect fail "others may not change .mcp.json"     someone  kjmikkel .mcp.json
expect fail "others may not change an agent definition" someone kjmikkel .claude/agents/sonnet-implementer.md
expect fail "others may not change a project skill" someone kjmikkel .claude/skills/running-the-app/SKILL.md
expect pass "others may change root build.ps1"    someone  kjmikkel build.ps1

# a symlink or submodule is one path with no trailing slash; node and Claude follow it
expect fail "symlink named node_modules"          someone  kjmikkel node_modules
expect fail "symlink named .trail"                someone  kjmikkel .trail
expect fail "symlink named dist"                  someone  kjmikkel dist
expect fail "symlink named dist/claude"           someone  kjmikkel dist/claude
expect fail "symlink named .claude"               someone  kjmikkel .claude
expect fail "nested symlink named .claude"        someone  kjmikkel DialogEditor.Core/.claude
expect pass "others may change a dist/ file"      someone  kjmikkel dist/other/x.json

# case-insensitive APFS case-folds (long s to s) and HFS+ ignores zero-width characters
expect fail "long s in .mcp.json"                 someone  kjmikkel $'.mcp.j\xc5\xbfon'
expect fail "long s in agents.md"                 someone  kjmikkel $'agent\xc5\xbf.md'
expect fail "zero-width non-joiner in .trail/"    someone  kjmikkel $'.tr\xe2\x80\x8cail/learnings/a.md'
expect fail "dotless i in .trail/"                someone  kjmikkel $'.tra\xc4\xb1l/learnings/a.md'
expect fail "fullwidth C in CLAUDE.md"            someone  kjmikkel $'\xef\xbc\xa3LAUDE.md'
expect pass "others may add non-ASCII elsewhere"  someone  kjmikkel $'docs/Ondra\xe2\x80\x99s notes.md'

# .trail/hook.cjs runs npm through cmd.exe, which looks in the current directory first,
# and the hook's current directory can be any subdirectory a session was started in
expect fail "planted npm.cmd at the root"         someone  kjmikkel npm.cmd
expect fail "planted node.EXE at the root"        someone  kjmikkel node.EXE
expect fail "planted npm.bat. at the root"        someone  kjmikkel "npm.bat."
expect fail "planted git.js at the root"          someone  kjmikkel git.js
expect fail "planted npm.cmd in a subdirectory"   someone  kjmikkel tools/ui-automation/npm.cmd
expect fail "changed a nested .exe"               someone  kjmikkel DialogEditor.Avalonia/tools/vgmstream-cli.exe
expect pass "owner may change a nested .exe"      kjmikkel kjmikkel DialogEditor.Avalonia/tools/vgmstream-cli.exe
expect fail "planted npm.cpl (user PATHEXT)"      someone  kjmikkel tools/npm.cpl
expect fail "planted npm.py (any PATHEXT entry)"  someone  kjmikkel npm.py
expect fail "planted nested NODE.pyw"             someone  kjmikkel tools/NODE.pyw
expect fail "planted extensionless cmd"           someone  kjmikkel tools/cmd
expect pass "others may add npm-notes.md"         someone  kjmikkel docs/npm-notes.md
expect pass "owner may add a root script"         kjmikkel kjmikkel setup.cmd

# a submodule's files never show up in the PR's file list, so it could carry any of the above
expect fail "others may not add a submodule"      someone  kjmikkel .gitmodules
expect fail "others may not add a nested .gitmodules" someone kjmikkel tools/.gitmodules
expect pass "owner may add a submodule"           kjmikkel kjmikkel .gitmodules
expect pass "others may change a nested .ps1"     someone  kjmikkel tools/ui-automation/DriveApp.ps1

# hook.cjs resolves @trailhq/trail-code from the repo root: a root package.json with that name
# self-references, and its "exports" can point the hook at any unguarded directory
expect fail "others may not add a root package.json" someone kjmikkel package.json
expect fail "upper-case root PACKAGE.JSON"        someone  kjmikkel PACKAGE.JSON
expect pass "others may add a nested package.json" someone kjmikkel tools/web/package.json

[ "$failures" -eq 0 ] && echo "all passed" || { echo "$failures failed"; exit 1; }
