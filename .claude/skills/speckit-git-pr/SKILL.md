---
name: "speckit-git-pr"
description: "after_implement hook target: commits the current phase's changes, pushes, and opens a GitHub PR bulk-closing its tracking issues"
argument-hint: "none — derives the phase key from the current branch name (story/<feature-slug>-<PhaseKey>)"
metadata:
  author: "project-custom"
user-invocable: true
disable-model-invocation: false
---

## Purpose

This skill is the `speckit.git.pr` target of the `after_implement` hook in `.specify/extensions.yml`. It
runs once `/speckit-implement` finishes, on the branch `speckit-git-branch` created
(`story/<feature-slug>-<PhaseKey>`), and turns that run's work into a PR closing every GitHub issue for
the tasks in that phase — completing the one-phase/one-branch/one-PR/N-issues invariant `speckit-git-branch`
gates on entry.

## Phase-key parsing (identical to `speckit-git-branch`)

`tasks.md` phase headers follow `## Phase N: <Title>`. Key = `US<N>` if the title contains
`User Story (\d+)`, else the title's first word. A phase's **expected task set** = every `T###` between
its header and the next `## Phase` header (or EOF), excluding any already `[X]` on `main`.

## Outline

0. **Bot identity (optional).** If `CLAUDE_GH_APP_ID`, `CLAUDE_GH_APP_INSTALLATION_ID`, and
   `CLAUDE_GH_APP_PRIVATE_KEY_PATH` are all set, run
   `export GH_TOKEN="$(.specify/scripts/bash/gh-app-token.sh)"` so every `gh` call below (and
   `CLAUDE_GH_APP_SLUG` for the commit in step 6) authenticates/attributes as the GitHub App's bot identity
   instead of the locally logged-in personal account. If any are unset, skip silently and fall back to the
   current `gh auth` session and default `git` commit identity.

1. Confirm the current branch matches `story/<feature-slug>-<PhaseKey>` (the pattern `speckit-git-branch`
   creates). If it doesn't — e.g. `speckit-git-branch` aborted and `/speckit-implement` should never have
   reached its Outline, but is somehow still running — abort loudly: report that there's no phase branch to
   commit to and stop without touching git.

2. Extract `PhaseKey` from the branch name.

3. Resolve `FEATURE_DIR`/`TASKS` via `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`
   (same as `speckit-git-branch`), then re-derive `PhaseKey`'s expected task set from `TASKS` using the
   identical parsing rules — this is the **single source of truth** for what this PR must contain.

4. **Verify what actually got done.** Run `git diff main -- <TASKS>` and collect every task ID whose
   checkbox flipped `- [ ]` → `- [X]` in this run. Compare against the expected task set from step 3:
   - **Exact match**: proceed to step 5.
   - **Subset** (phase left incomplete — some expected tasks are still `[ ]`): abort. Report which tasks
     remain and that a partial phase won't be opened as a PR:

     ```text
     speckit-git-pr: phase "Foundational" isn't finished — T007 and T008 are still unchecked. Nothing has
     been committed, pushed, or opened. Finish the rest of the phase and re-invoke, or tell me if you want
     to stop here and I'll adjust scope.
     ```

   - **Superset** (tasks outside `PhaseKey`'s expected set also flipped): abort — this needs a human
     decision, don't silently resolve it:

     ```text
     speckit-git-pr: this run also completed T009 (from phase "US1"), not just Foundational's T005-T008.
     Opening a PR here would mix phases. Nothing has been committed, pushed, or opened.

     Options:
       (a) manually split T009's changes onto its own branch/commit before I open PRs for each,
       (b) accept one PR here covering both phases as a one-off exception,
       (c) revert T009's changes on this branch and re-run it separately later.

     Tell me which you'd like, or handle it yourself and re-invoke /speckit-git-pr when ready.
     ```

   - **Zero flipped**: abort. Report that `/speckit-implement` didn't complete anything and nothing will be
     committed/pushed/opened. Leave the branch as-is for inspection.

5. Map every completed task ID to its GitHub issue number. Confirmed convention this session: issue number
   == task number (e.g. `T005` → issue `#5`) in the repo resolved via `git config --get remote.origin.url`
   (must be a GitHub remote; abort if not). Don't assume this holds for every feature without checking — if
   unsure, `gh issue list --repo <owner>/<repo> --search "T### in:title" --json number,title` per task and
   match the title's leading `T###:` token instead of assuming the number.

6. Read `TASKS` for the phase's own title (from its `## Phase N: <Title>` header) and each completed task's
   description text (used in the commit/PR title and body).

7. `git add -A && git commit -m "<PhaseKey>: <phase title>"` — if `CLAUDE_GH_APP_SLUG` and
   `CLAUDE_GH_APP_ID` are set (bot identity configured), instead commit as the bot:
   `git -c user.name="${CLAUDE_GH_APP_SLUG}[bot]" -c user.email="${CLAUDE_GH_APP_ID}+${CLAUDE_GH_APP_SLUG}[bot]@users.noreply.github.com" commit -m "<PhaseKey>: <phase title>"`.

8. `git push -u origin <branch>`.

9. `gh pr create --repo <owner>/<repo> --title "<PhaseKey>: <phase title>" --body`:

   ```text
   Implements phase "<PhaseKey>: <phase title>" per <feature-dir>/tasks.md:
   - T005: <description>
   - T006: <description>
   - T007: <description>
   - T008: <description>

   Closes #5
   Closes #6
   Closes #7
   Closes #8

   ## Verification
   <aggregated test/build command(s) run for the whole phase and their result, e.g.
   `dotnet test Duelyst.sln — N passed`>
   ```

   (One `Closes #N` line per completed task — GitHub closes all of them independently on merge.)

10. Report the PR URL back to the user, and remind them `/speckit-pr-sync` is how to check on review status
    later.

## Done When

- [ ] Either: aborted cleanly with a clear reason (wrong branch, nothing completed, phase incomplete, or
  cross-phase scope creep needing a human decision) and nothing was committed/pushed/opened —
- [ ] Or: exactly the gated phase's tasks were committed as one commit, pushed, and a PR was opened closing
  every one of that phase's GitHub issues, and the PR URL was reported.
