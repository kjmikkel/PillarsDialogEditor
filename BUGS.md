# Dialog Editor — Bug Tracker (pre-launch, internal)

> **Temporary file — delete before the initial public release.** This is a lightweight,
> local bug list for solo development. When the project goes public, bug tracking moves to
> GitHub Issues and this file is removed (see the **Bug Tracker** rule in `CLAUDE.md`).

Newest first. When a bug is fixed, **move** its entry to the **Fixed** section with the
fixing commit hash rather than deleting it — the record of what broke and how it was fixed
stays useful until launch.

## How to log a bug

Copy the template into **Open**. A partial entry is fine — *Repro* + *Actual* is enough to
start. IDs are a simple running counter (`B-001`, `B-002`, …) so commits can reference them
("fix B-003: …").

```
### B-NNN — <one-line summary>
- **Area:** <e.g. Diff viewer, Branches, Changelog reader>
- **Severity:** blocker | major | minor | cosmetic
- **Repro:**
  1. <step>
  2. <step>
- **Expected:** <what should happen>
- **Actual:** <what happens — include any error text or AppLog output>
- **Notes:** <hypotheses, suspect files, related entries>
```

When fixed, append to the moved entry:
```
- **Fixed:** <commit hash> — <one-line explanation of the fix + the test that now guards it>
```

---

## Open

_None yet._

---

## Fixed

_None yet._
