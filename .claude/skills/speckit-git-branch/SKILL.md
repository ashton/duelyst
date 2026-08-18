---
name: "speckit-git-branch"
description: "before_implement hook target: hard-gates /speckit-implement on a single explicit task ID and creates its branch off latest main"
argument-hint: "none — reads the task ID from the /speckit-implement invocation that triggered this hook"
metadata:
  author: "project-custom"
user-invocable: true
disable-model-invocation: false
---

## Purpose

This skill is the `speckit.git.branch` target of the `before_implement` hook in `.specify/extensions.yml`.
It exists to enforce a hard invariant the rest of the branch → PR → review-loop → merge workflow depends
on: **one `/speckit-implement` invocation = one task = one branch = one PR = one existing GitHub issue.**
`/speckit-implement` itself has no logic that scopes a run to a single task even when given a task-filter
argument (confirmed by reading its SKILL.md — the `argument-hint` is descriptive only), so this hook is the
only place that invariant can actually be enforced, and it does so by refusing to create a branch — and
therefore blocking `/speckit-implement` from proceeding to its Outline at all — unless exactly one task ID
was explicitly given.

## Outline

0. **Bot identity (optional).** If `CLAUDE_GH_APP_ID`, `CLAUDE_GH_APP_INSTALLATION_ID`, and `CLAUDE_GH_APP_PRIVATE_KEY_PATH` are
   all set, run `export GH_TOKEN="$(.specify/scripts/bash/gh-app-token.sh)"` so every `gh` call below
   authenticates as the GitHub App's bot identity instead of the locally logged-in personal account. If any
   of those three env vars are unset, skip this silently — `gh` falls back to the existing `gh auth` session
   (your personal account) so the workflow keeps working before the App is configured.

1. **Determine the task ID.** Inspect the arguments the user passed to the `/speckit-implement` invocation
   currently in progress in this conversation (the text after `/speckit-implement`). Extract every distinct
   token matching `T\d{3}` from it.
   - **Exactly one match**: this is the target task ID. Continue to step 2.
   - **Zero or more than one match**: this is an abort. Before aborting, resolve the active feature's
     `tasks.md` (via `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`, which prints
     `FEATURE_DIR`/`TASKS`) and parse it for the first unchecked `- [ ] T###` line, purely to surface as a
     suggestion. Report exactly:

     ```text
     speckit-git-branch: no single task ID given to /speckit-implement — aborting. No branch created, no
     implementation should proceed.
     If you want the next task implemented, call `/speckit-implement T<parsed_id>`.
     ```

     (Substitute the parsed ID, or omit the suggestion line entirely if `tasks.md` has no unchecked tasks
     left.) Then **stop** — do not create a branch, do not let `/speckit-implement` proceed to its Outline.
     This is a hard gate, not a fallback: never auto-pick a task and continue.

2. Run `.specify/scripts/bash/check-prerequisites.sh --json --paths-only` from the repo root and parse
   `FEATURE_DIR` and `TASKS` (absolute paths) — the same resolution `/speckit-implement` itself uses, via
   `common.sh`'s `get_feature_paths`. Do not reimplement feature-dir resolution.

3. Read `TASKS` and confirm the task ID from step 1 appears as an **unchecked** `- [ ] T###` line.
   - If the line is already `- [X] T###` (already done) or the ID doesn't appear in the file at all,
     abort with a clear message naming the problem (already completed / unknown task ID) and stop, same as
     step 1's hard-stop behavior.

4. **Duplicate-work guard.** Let `feature-slug` = `basename "$FEATURE_DIR"` (e.g.
   `002-core-rules-engine`) and `branch` = `task/<feature-slug>-T###`. Determine the GitHub repo via
   `git config --get remote.origin.url` (must be a GitHub remote — same check `speckit-taskstoissues` does;
   abort if it isn't). Run `gh pr list --repo <owner>/<repo> --head <branch> --state open --json number,url`.
   - If a PR is already open for this exact branch, abort: report that this task already has an open PR
     (include its URL) and suggest running `/speckit-pr-sync T###` instead of starting duplicate work. Stop.

5. `git status --porcelain` — if there are uncommitted changes, stop and report them rather than silently
   carrying unrelated working-tree state onto the new branch (ask the user to commit/stash first).

6. `git fetch origin && git checkout main && git pull --ff-only origin main` — start from the latest merged
   state.

7. `git checkout -b <branch>` (e.g. `task/002-core-rules-engine-T004`).

8. Report the branch name and task ID, and that `/speckit-implement` may now proceed.

## Done When

- [ ] Either: aborted cleanly (no branch created, no implementation proceeds, suggestion given if
  possible) because the task ID wasn't unambiguous, already done, unknown, or already has an open PR —
- [ ] Or: exactly one valid, not-yet-done, not-already-open-PR task ID was resolved and its branch was
  created off latest `main`.
