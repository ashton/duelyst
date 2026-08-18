---
name: "speckit-git-branch"
description: "before_implement hook target: hard-gates /speckit-implement on a single explicit tasks.md phase key and creates its branch off latest main"
argument-hint: "none — reads the phase key (e.g. US1, Foundational) from the /speckit-implement invocation that triggered this hook"
metadata:
  author: "project-custom"
user-invocable: true
disable-model-invocation: false
---

## Purpose

This skill is the `speckit.git.branch` target of the `before_implement` hook in `.specify/extensions.yml`.
It enforces the invariant the rest of the branch → PR → review-loop → merge workflow depends on: **one
`/speckit-implement` invocation = one `tasks.md` phase = one branch = one PR = one or more closed GitHub
issues (one per task in that phase).** `/speckit-implement` itself has no logic that scopes a run to a
single phase even when given a filter argument (confirmed by reading its SKILL.md — the `argument-hint` is
descriptive only, and its own Outline says "Complete each phase before moving to the next," which is
already phase-shaped), so this hook is the only place the invariant can actually be enforced — by refusing
to create a branch, and therefore blocking `/speckit-implement` from proceeding to its Outline at all,
unless exactly one valid phase key was explicitly given.

## Phase-key parsing (shared logic — `speckit-git-pr` re-derives this identically)

`tasks.md` phase headers follow `## Phase N: <Title>`. For each header, derive its short invocable key:
- If `<Title>` contains `User Story (\d+)` → key is `US<N>` (e.g. "Phase 3: User Story 1 - ..." → `US1`).
- Otherwise → key is `<Title>`'s first word (e.g. "Phase 1: Setup (...)" → `Setup`; "Phase 2: Foundational
  (...)" → `Foundational`; "Phase 6: Polish & ..." → `Polish`).

A phase's **expected task set** = every `T###` token appearing between its `## Phase N:` header and the
next `## Phase` header (or end of file), **excluding any already `[X]` on `main`** — this naturally
handles a phase that's partially done already (e.g. one of its tasks merged individually under the old
per-task scheme) without special-casing it.

## Outline

0. **Bot identity (optional).** If `CLAUDE_GH_APP_ID`, `CLAUDE_GH_APP_INSTALLATION_ID`, and
   `CLAUDE_GH_APP_PRIVATE_KEY_PATH` are all set, run
   `export GH_TOKEN="$(.specify/scripts/bash/gh-app-token.sh)"` so every `gh` call below authenticates as
   the GitHub App's bot identity instead of the locally logged-in personal account. If any of those three
   env vars are unset, skip this silently — `gh` falls back to the existing `gh auth` session.

1. Run `.specify/scripts/bash/check-prerequisites.sh --json --paths-only` and parse `FEATURE_DIR`/`TASKS`
   (absolute paths) — the same resolution `/speckit-implement` itself uses via `common.sh`'s
   `get_feature_paths`. Do not reimplement feature-dir resolution.

2. Parse `TASKS` for every `## Phase N: <Title>` header and derive each phase's key and expected task set
   per the parsing rules above.

3. **Determine the target phase key.** Inspect the arguments passed to the `/speckit-implement` invocation
   currently in progress (the text after `/speckit-implement`).
   - **Matches a known phase key exactly** (case-sensitive, e.g. `US1`, `Foundational`): this is the
     target. Continue to step 4.
   - **Matches a bare task ID** (`T\d{3}`) instead of a phase key: look up which parsed phase contains that
     task ID and **abort with a redirect**, e.g.:

     ```text
     speckit-git-branch: T005 belongs to phase "Foundational" — invoke as `/speckit-implement Foundational`,
     which will complete all of T005-T008 in one PR. Aborting; no branch created.
     ```

     If the task ID isn't found in any phase, fall through to the next case instead.
   - **No match / ambiguous**: abort. Find the first phase (in file order) with a non-empty expected task
     set and suggest it:

     ```text
     speckit-git-branch: no valid phase key given to /speckit-implement — aborting. No branch created, no
     implementation should proceed.
     If you want the next phase implemented, call `/speckit-implement <PhaseKey>` (next up: Foundational,
     tasks T005-T008).
     ```

     (Omit the suggestion entirely if every phase's expected task set is empty — nothing left.) This is a
     hard gate, not a fallback: never auto-pick a phase and continue.

4. If the target phase's expected task set is empty (everything in it already `[X]` on `main`), abort:
   "phase `<PhaseKey>` is already complete." Stop.

5. **Duplicate-work guard.** Let `feature-slug` = `basename "$FEATURE_DIR"` and `branch` =
   `story/<feature-slug>-<PhaseKey>`. Determine the GitHub repo via `git config --get remote.origin.url`
   (must be a GitHub remote — same check `speckit-taskstoissues` does; abort if it isn't). Run
   `gh pr list --repo <owner>/<repo> --head <branch> --state open --json number,url`.
   - If a PR is already open for this branch, abort: report its URL and suggest `/speckit-pr-sync
     <PhaseKey>` instead of starting duplicate work. Stop.

6. `git status --porcelain` — if there are uncommitted changes, stop and report them rather than silently
   carrying unrelated working-tree state onto the new branch (ask the user to commit/stash first).

7. `git fetch origin && git checkout main && git pull --ff-only origin main` — start from the latest
   merged state.

8. `git checkout -b <branch>` (e.g. `story/002-core-rules-engine-Foundational`).

9. Report the branch name, phase key, and its expected task set, and that `/speckit-implement` may now
   proceed.

## Done When

- [ ] Either: aborted cleanly (no branch created, no implementation proceeds; a redirect or next-phase
  suggestion given where possible) because the phase key wasn't valid/unambiguous, the phase was already
  complete, or it already has an open PR —
- [ ] Or: exactly one valid, not-yet-complete, not-already-open-PR phase was resolved and its branch was
  created off latest `main`.
