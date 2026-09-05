---
name: update-docs-and-versions
description: |
  After completing a feature or fix in the namorix-scout addon, update the memory bank
  (.claude/memory/progress.md, activeContext.md) and bump versions. Single-addon: version
  lives in addon.json + frontend/package.json only — no monorepo, no version.ts, no READMEs.
  Scans git first to only touch files affected by recent changes.
---

# Update Documentation & Versions

After a coding session, update memory bank docs and bump the addon version following
CLAUDE.md Meta Rules (SemVer `MAJOR.MINOR.PATCH`, no leading `v`). Always scan git first —
never read unchanged files.

## Workflow

### Step 1: Scan Git Changes

```bash
git status --short          # staged + unstaged + untracked
git diff --stat             # unstaged changes
git diff --cached --stat    # staged changes
git log --oneline -5        # recent commits for context
```

If working tree is clean and no recent commits → output "Nothing to update" and stop.

### Step 2: Categorize Changed Files by Scope

Map each changed file to a scope (CLAUDE.md Rule 8) and determine impact:

| Changed Files | Scope | Impact |
|--------------|-------|--------|
| `frontend/src/` | frontend | New view, hook, i18n key, or bug fix |
| `frontend/package.json`, `frontend/vite.config.ts` | frontend | Dep, build, or config change |
| `backend/src/` | backend | New service, endpoint, or bug fix (C#) |
| `backend/Makefile`, `backend/Namorix.Scout.csproj` | backend | Build/tooling change |
| `addon.json`, `assets/` | addon | Addon manifest change → bump addon version |
| `frontend/Dockerfile`, `frontend/docker-compose.yml` | docker | Container config change |
| Root config files | chore | `.gitignore`, tooling |

### Step 3: Determine Version Bumps

This is a **single addon** — there is exactly one version to bump, and it is tracked in
two places that must stay in sync:

| Version location | Notes |
|------------------|-------|
| `addon.json` → `version` | Canonical addon version (Desktop installs/updates against this) |
| `frontend/package.json` → `version` | Mirror of the addon version |

Bump triggers:

| Change | Bump |
|--------|------|
| Bug fix / tweak nhỏ | PATCH (vd `0.1.0` → `0.1.1`) |
| Feature mới / phase mới | MINOR (vd `0.1.0` → `0.2.0`) |
| Breaking addon / scaffold → hoàn thiện | MAJOR (vd `0.1.0` → `1.0.0`) |

**Rule:** Only bump if the addon's behavior shipped to Desktop changed. Backend-only
internal changes with no addon surface impact → note in progress.md, no bump.

**No monorepo dependents:** this addon has no `packages/*` and no `version.ts`/`NmxAddonVersions` —
do not look for them. `@namorix/core/ui/styles` and `Namorix.Core` are sibling-repo `link:`
dependencies; their versions are managed in the `namorix` repo, not here.

### Step 4: Read Only What's Needed

Read ONLY these files (skip if unchanged):
- `.claude/memory/progress.md` — always read (current version + history)
- `.claude/memory/activeContext.md` — always read (current focus)
- `.claude/memory/systemPatterns.md` — only if architecture changed
- `.claude/memory/techContext.md` — only if tech/deps changed
- `.claude/memory/productContext.md` — only if UX changed
- `.claude/memory/projectbrief.md` — rarely changes
- `addon.json` — only if bumping version
- `frontend/package.json` — only if bumping version

**Never read:**
- `LICENSE`
- `README.md` (none exist in this repo — do not create or hunt for them)
- `node_modules/`, `bin/`, `obj/`, `backend/src/data/`
- `*.g.cs`, `*.Designer.cs`, EF migration auto-generated files

### Step 5: Update Documentation

#### progress.md — always update if version changed
1. Update the "Current Version" with the new version
2. Add a new entry in "Version History" under today's date

#### activeContext.md — update if needed
1. Update "Recent Changes" with summary of what was done
2. Update "Next Steps" if milestone progress changed

#### Other memory files — only if relevant
- `systemPatterns.md`: new patterns, architecture decisions
- `techContext.md`: new deps, new config, new key files
- `productContext.md`: UX changes

### Step 6: Update Version References

Update the `"version"` field in **both** `addon.json` and `frontend/package.json`.

Then search the entire project for other references to the old version string:

```bash
grep -rn '0\.1\.0' --include='*.{md,json,ts,tsx,cs,csproj,yml,yaml}' . \
  --exclude-dir=node_modules --exclude-dir=bin --exclude-dir=obj
```

Update all occurrences found in:
- `addon.json` — manifest version (also check any `minCoreVersion`/`minServerVersion` only if the addon now requires newer Namorix)
- `.claude/memory/*.md` — version mentions
- `.claude/plans/*.plan.md` — version numbers in plans
- `frontend/src/` — any version constant or display string

**Rule:** A version bump is not complete until all references across the project are
updated. Incomplete updates cause confusion and integration bugs.

### Step 7: Present Changes for Approval

Show a table of all planned changes:

```
| File | Change |
|------|--------|
| addon.json | version 0.1.0 → 0.1.1 |
| frontend/package.json | version 0.1.0 → 0.1.1 |
| .claude/memory/progress.md | Update version + history |
| .claude/memory/activeContext.md | Add recent changes entry |
| ... | ... |
```

Ask user to confirm the full plan before writing any file.
If user rejects specific files, skip those and apply the rest.

## Important Rules

1. **Scan git first** — never guess what changed
2. **Only read changed files + required doc files** — skip everything else
3. **Don't bump if nothing shipped** — only bump the addon version when the Desktop-visible addon changed
4. **Version notation:** `MAJOR.MINOR.PATCH`, no leading `v` (CLAUDE.md Meta Rules)
5. **Ask before writing** — present the full plan, wait for approval; respect partial rejection
6. **Follow existing format** in progress.md — match the table style exactly
7. **Single-addon version sync** — update BOTH `addon.json` and `frontend/package.json`; they must never drift apart
8. **Search the whole project** — dùng `grep` không có quotes để tìm tất cả references đến version cũ. Update tất cả để tránh inconsistency.
9. **No README / FLOW.md / version.ts** — repo này không có; đừng tạo hoặc sửa chúng
10. **Early exit** — nếu không có changes, dừng ngay và thông báo
