---
title: UI-automation screenshots and logs leak the scratch path under the user profile
author: Mikkel
date: 2026-10-24
branch: docs/ui-ux-pass-2026-10-24
cost: { minutes: 4, tokens: 29857 }
id: l-b1e55e8f
scope: repo
status: active
touches:
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/matrix-log.txt
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/walk/walk-log.txt
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/narrator/walk4-log.txt
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/walk2/walk2-log.txt
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/dark-1.00x-settings.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/dark-1.00x-patch-manager.png
---

## Watch out
- Capture-Windows.ps1, CaptureSurfaces.ps1 and hand-written DriveApp walks point the editor at a scratch game folder under %TEMP%, which lives under the Windows user profile. That path is then visible in screenshots (Settings backup directory, Patch Manager game folder, the main-window status bar) and is written verbatim into walk logs. The repo is public, so these must not be committed as-is.
- Before committing captures: redact paths in text logs (replace with %TEMP% / <repo>) and either paint over the path fields in images or point the scratch folder at a neutral location (e.g. a short folder like C:\uxscratch) before capturing.
- Window screenshots (Save-WindowScreenshot of a non-maximised window or a dialog) include the invisible ~8 px resize border, where the desktop shows through: icon labels and wallpaper along the left, right and bottom edges. Paint that border too.
- `git diff --check` in the Whitespace hygiene CI job also fails on raw walk logs, because the scripts write lines with trailing spaces.
- Run Windows-path redaction regexes from a script file, not inline in the Bash tool.
## Tried and ruled out
- Redacting with sed or a python heredoc in the Bash tool: the shell collapsed the doubled backslashes in Windows-path regexes, so nothing matched while the grep check reported "clean".
- WinRT OCR (Windows.Media.Ocr) as a detector for paths in screenshots: it skips the text inside TextBoxes even upscaled 3x and colour-inverted, which is exactly where the paths are. It found 4 of 25 affected images. Use the XAML instead: grep for bindings that show a path (BackupDirectory, GameFolder, Status_FolderLoaded) and check those surfaces.
