---
name: scan-bugs-and-typos
description: |
  Scan files changed in git for bugs and spelling errors. Reads only changed files
  (staged + unstaged) to avoid checking the entire codebase. Covers TS/React
  frontend and C# backend (CLAUDE.md Rule 11 C# naming). Separates findings
  into bugs (logic errors) and typos (spelling, naming).
---

# Scan Bugs & Typos in Changed Files

Read only files that changed in git and check for bugs and spelling errors.

## Workflow

### Step 1: Get Changed Files

```bash
git diff --name-only          # unstaged changes
git diff --cached --name-only # staged changes
```

Merge both lists, de-duplicate, skip:
- `node_modules/`
- `dist/`, `bin/`, `obj/`, `backend/src/data/`
- `*.lock`, `*.log`
- `LICENSE`
- `.idea/`
- Binary files
- `*.g.cs`, `*.Designer.cs`, EF migration auto-generated files

If no changed files found → output "Nothing to scan" and stop.

### Step 2: Read Each File

Read every changed file in full. For each, check two categories:

### Bug Scan

Look for these patterns:

| Bug Pattern | What to Flag | Language |
|-------------|--------------|----------|
| Missing `await` | async function called without await in non-return context | TS/C# |
| Unused variables | `const x = ...` never referenced | TS |
| Wrong error handling | `catch (err)` without typing `unknown`, missing re-throw | TS |
| Race conditions | setState after await without checking mounted/cancelled | TS |
| Null safety | Accessing `.property` on possibly null value without guard | TS/C# |
| Logic errors | `if (x = y)` assignment in condition, inverted boolean | TS/C# |
| Missing return | Function expects return but path misses it | TS/C# |
| Import issues | Import from wrong package (crossing CLAUDE.md Rule 3 — Package Boundary) | TS |
| Auth bypass risk | Route missing auth middleware, token not verified | TS/C# |
| CSRF gap | Mutating endpoint without CSRF check | C# |
| Cookie security | `httpOnly: false` on auth cookies, missing `sameSite` | C# |
| Hardcoded secrets | API keys, passwords in source | TS/C# |
| Debug code left | `console.log`, `debugger`, `Console.WriteLine` statements | TS/C# |
| Missing ConfigureAwait | async without `.ConfigureAwait(false)` in C# library code | C# |

### Spelling & Naming Scan

| Check | Flag | Language |
|-------|------|----------|
| Typos in comments | Common misspellings (recieve→receive, etc.) | TS/C# |
| Typos in string literals | UI strings, error messages visible to user | TS/C# |
| Inconsistent casing | `userId` vs `UserID`, `API` vs `Api` | TS/C# |
| Wrong variable naming | camelCase violations (TS, Rule 10), PascalCase violations (C#, Rule 11) | TS/C# |
| String quotes | TS/JS strings should use double quotes (CLAUDE.md Rule 10) | TS |
| Missing Nmx prefix | New shared UI component without `Nmx` prefix (CLAUDE.md Rule 6) | TS |
| BEM class errors | CSS class not following `nmx-kebab-case` | CSS/SCSS |

### Step 3: Categorize Findings

Separate into two groups:

```
## 🐛 Bugs
- file.ts:42 — Missing await on async function call
- PlaybackController.cs:120 — Possible null reference on segment (no guard)

## 📝 Typos & Naming
- file.ts:15 — "recieve" should be "receive"
- RtspIngestService.cs:30 — Local variable `cameraID` should be `cameraId` (Rule 11)
```

### Step 4: Severity

| Severity | When |
|----------|------|
| 🔴 Critical | Auth bypass, security, crash risk |
| 🟡 Warning | Logic error, race condition |
| 🔵 Info | Style, naming, comment typo |

### Step 5: Output Format

```markdown
## Scan Results — N files checked

### 🔴 Critical (X)
| File:Line | Issue |
|-----------|-------|
| ... | ... |

### 🟡 Warnings (X)
| File:Line | Issue |
|-----------|-------|
| ... | ... |

### 🔵 Info (X)
| File:Line | Issue |
|-----------|-------|
| ... | ... |

---
✅ No issues found — or — ⚠️ X issues found across Y files
```

## Important

1. **Only scan changed files** — skip everything else
2. **Read the rules first** — check CLAUDE.md for project-specific conventions to validate against (Rule 3 boundary, Rule 10 TS naming, Rule 11 C# naming)
3. **Don't auto-fix** — only report findings
4. **Use file:line references** — so user can jump to each issue
5. **Skip false positives** — if unsure, mention as "possible" issue
6. **Early exit** — if no changed files, say so and stop
7. **Language-aware** — apply naming rules per language (camelCase for TS per Rule 10, PascalCase for C# per Rule 11)
