# Building for distribution

This page is for maintainers building release archives. To build and run the tests while
working on the code, see [CONTRIBUTING.md](../CONTRIBUTING.md).


The repo ships two products, versioned and released independently:

| Product | Version file | Release tag | Contains |
|---------|--------------|-------------|----------|
| Pillars Dialog Editor | `VERSION` | `v<ver>` | the editor |
| Pillars Dialog Patcher | `PATCHER_VERSION` | `patcher-v<ver>` | the Patch Manager GUI and the `dialog-patcher` CLI, for players installing mods |

The patcher only gets a new version when code that applies patches changes.
Whether it can read a mod made with a given editor depends on the file formats
it supports (see `FORMAT.md`), not on matching version numbers.

Three PowerShell scripts are provided at the repo root. Each takes
`-Product Editor|Patcher|All` (default `All`).

## build.ps1 — full release build (recommended)

Runs the test suite, then produces each selected product's binary and source
archives under `dist/`, plus `SHA256SUMS.txt` covering them all:

```powershell
.\build.ps1                          # both products, versions from the version files
.\build.ps1 -Product Patcher         # the patcher only
.\build.ps1 -Version 1.2.0           # override the editor version
.\build.ps1 -PatcherVersion 1.0.1    # override the patcher version
.\build.ps1 -SkipTests               # skip the test gate
```

Aborts immediately if any test fails. Cleans `dist/` before each run so old
files never accumulate.

## build-dist.ps1 — binary archives only

Publishes each app as a self-contained win-x64 executable (no .NET installation
required on the target machine) and zips it:

| Archive | Contents |
|---------|----------|
| `PillarsDialogEditor-<ver>.zip` | The editor |
| `PillarsDialogPatcher-<ver>.zip` | `DialogEditor.PatchManager.exe` and a player README at the top; the single-file `dialog-patcher.exe` in `cli\`; one shared .NET runtime in `runtime\` |

The two patcher apps share one bundled runtime rather than each carrying its
own: they are published framework-dependent with `AppHostDotNetSearch=AppRelative`,
so each exe looks for .NET *only* in `runtime\` next to it (never a system
install), and `build-dist.ps1` copies that runtime from the build machine's .NET
install and smoke-tests the staged CLI against it. The patcher zip keeps the
console tool in `cli\` so the only `.exe` a player sees at the top is the GUI. Double-clicking `dialog-patcher.exe` anyway shows a
note pointing to the Patch Manager and waits for a key; runs from a terminal or
script are unaffected. The player README is `docs/patcher/README.md`.

```powershell
.\build-dist.ps1
.\build-dist.ps1 -Product Patcher -PatcherVersion 1.0.1
```

## build-source.ps1 — source archives only

Copies the project folders needed to build each app (excluding `bin/`, `obj/`,
and editor artefacts), writes a trimmed `.slnx` solution file, and zips:

| Archive | Contents |
|---------|----------|
| `PillarsDialogEditor-<ver>-src.zip` | Editor source |
| `PillarsDialogPatcher-<ver>-src.zip` | Patch Manager and CLI source, one solution |

Each source archive also includes `README.md`, `CONTRIBUTING.md`, `docs/building.md`, `VERSION`, `PATCHER_VERSION`, `LICENSE`,
`THIRD_PARTY_LICENSES.md`, `Directory.Packages.props`, and any repo file its
projects pull in from outside their own folders (the editor's `CHANGELOG.md`
and `docs/walkthrough.md`), so `dotnet build` on the extracted `.slnx` works
with no extra setup. CI checks this with `tools/ci/Test-SourceArchives.ps1`.

The archives do not include `DialogEditor.Tests`: it tests the whole repository
(it references the editor and the UI-automation tooling, and scans every
project's source), so no single-app subset can build or run it. Clone the
repository to run the test suite.

```powershell
.\build-source.ps1
.\build-source.ps1 -Product Editor -Version 1.2.0
```

## Versioning and releases

`VERSION` holds the editor's version and `PATCHER_VERSION` the patcher's. The
scripts read them when `-Version` / `-PatcherVersion` is not supplied, and stamp
them into the binaries: the editor's and Patch Manager's About windows and
`dialog-patcher --version` all report their own product's version.

Pushing a tag builds one product and attaches its zips and `SHA256SUMS.txt` to a
**draft** GitHub Release (`.github/workflows/release.yml`). The tag must match
that product's version file, or the workflow fails before building:

```powershell
git tag v1.2.0         ; git push origin v1.2.0           # editor
git tag patcher-v1.0.1 ; git push origin patcher-v1.0.1   # patcher
```
