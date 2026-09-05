---
name: scan-project-docs
description: |
  Scan namorix-scout documentation selectively based on the task at hand. Reads
  CLAUDE.md first to understand rules and doc structure, then selectively reads only
  the relevant .claude/memory/ files, .claude/plans/ (active), and .claude/archive/
  (completed plans). Avoids reading everything —
  focuses on what matters.
---

# Scan Project Documentation

Read project docs selectively based on the current task. Start from CLAUDE.md and
`.claude/memory/MEMORY.md`, then branch to relevant files only.

## Workflow

### Step 1: Read the Index

Always start by reading `CLAUDE.md` — it contains all coding rules (Rule 0–11) and the
memory bank index (`.claude/memory/MEMORY.md`):
- Memory bank files to check
- Package boundaries (Rule 3)
- Naming conventions (Rule 10 TS, Rule 11 C#)
- Git conventions (Rule 8 scopes)
- All rule definitions (no separate rule files — everything is in CLAUDE.md)

If `CLAUDE.md` does not exist → read `.claude/memory/MEMORY.md` directly as fallback.
If neither exists → skip to Step 3 with no rules context, note the gap in the summary.

### Step 2: Map Task to Relevant Docs

| User Task | Read These Files |
|-----------|-----------------|
| Frontend/React work | CLAUDE.md Rule 1 (TS config), Rule 4 (Imports), Rule 5 (React), Rule 6 (UI Primitives), Rule 10 (Naming) |
| Backend/API work | CLAUDE.md Rule 3 (Package Boundary), Rule 7 (Error Handling), Rule 11 (C# naming) |
| Full-stack feature | CLAUDE.md Rule 3, Rule 5, Rule 7 + `.claude/memory/systemPatterns.md` |
| New feature | `.claude/memory/progress.md` (current version), `.claude/memory/activeContext.md` (current focus), `.claude/archive/` (completed plan for the feature) |
| Bug fix | CLAUDE.md Rule 7 (Error Handling), `.claude/memory/progress.md` (known issues) |
| Git/commit | CLAUDE.md Rule 8 (Git Conventions) |
| New file/component | CLAUDE.md Rule 9 (File Structure), Rule 10 (Naming) |
| Documentation update | CLAUDE.md Meta Rules (version notation) |
| Auth changes | `.claude/memory/systemPatterns.md` (auth flow section) |
| Version bump | `.claude/memory/progress.md` + `.claude/memory/techContext.md` (version notes) |
| Any task | `.claude/memory/activeContext.md` (always — know current state) |

### Step 3: Read Selectively

1. Read CLAUDE.md first (already in context on session start)
2. Read `.claude/memory/activeContext.md` (always)
3. Based on the task, read the relevant memory files and plans from the table above
4. Skip everything else — don't pre-read unnecessary files

### Step 4: Summarize

Output a concise summary:
```
## Relevant Context
- Current version: <from progress.md if read, else "unknown">
- Active work: <from activeContext.md>
- Active plan: <from .claude/plans/ or .claude/archive/ if read, else "none">

## Rules Applied
- CLAUDE.md Rule N: <key rule summary>
- CLAUDE.md Rule M: <key rule summary>

## Missing Context (if any)
- CLAUDE.md not found — rules context unavailable
- <other gaps>

## Next Steps
- <what to do>
```

## Important

- **Never read all docs** — only what's relevant to the task
- **Always read activeContext.md** — know current state before any task
- **Skip LICENSE** — per CLAUDE.md Meta Rules
- **Skip unchanged scopes** — only read files in scope (frontend/backend/addon/docker)
- **Plans are living docs** — if a `.claude/plans/*.plan.md` (active) or `.claude/archive/*.plan.md` (completed) matches the task, read it for the implementation checklist
- **Use git status** to check what files were recently changed before reading
- **Fallback gracefully** — if key files missing, note the gap and continue
- **Full-stack tasks read both sides** — frontend + backend rules apply together
- **Memory bank is gitignored** (`.claude/memory/`) — it reflects local session state, not committed code; treat as session context
