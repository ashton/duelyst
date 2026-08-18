---
name: "speckit-git-pr"
description: "after_implement hook target: commits the current task's changes, pushes, and opens a GitHub PR closing its tracking issue"
argument-hint: "none — derives the task ID from the current branch name (task/<feature-slug>-T###)"
metadata:
  author: "project-custom"
user-invocable: true
disable-model-invocation: false
---

## Purpose

This skill is the `speckit.git.pr` target of the `after_implement` hook in `.specify/extensions.yml`. It
runs once `/speckit-implement` finishes, on the branch `speckit-git-branch` created
(`task/<feature-slug>-T###`), and turns that run's work into a PR closing exactly one GitHub issue —
completing the one-task/one-branch/one-PR/one-issue invariant `speckit-git-branch` gates on entry.

## Outline

0. **Bot identity (optional).** If `CLAUDE_GH_APP_ID`, `CLAUDE_GH_APP_INSTALLATION_ID`, and `CLAUDE_GH_APP_PRIVATE_KEY_PATH` are
   all set, run `export GH_TOKEN="$(.specify/scripts/bash/gh-app-token.sh)"` so every `gh` call below (and
   `CLAUDE_GH_APP_SLUG` for the commit in step 7) authenticates/attributes as the GitHub App's bot identity instead
   of the locally logged-in personal account. If any are unset, skip silently and fall back to the current
   `gh auth` session and default `git` commit identity.

1. Confirm the current branch matches `task/<feature-slug>-T###` (the pattern `speckit-git-branch`
   creates). If it doesn't — e.g. `speckit-git-branch` aborted and `/speckit-implement` should never have
   reached its Outline, but is somehow still running — abort loudly: report that there's no task branch to
   commit to and stop without touching git.

2. Extract the task ID from the branch name (the `T###` suffix) — this is the **single task ID this PR must
   correspond to**, established at branch-creation time.

3. Resolve `FEATURE_DIR`/`TASKS` via `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`
   (same as `speckit-git-branch`).

4. **Verify what actually got done.** Run `git diff main -- <TASKS>` and find every task ID whose checkbox
   flipped `- [ ]` → `- [X]` in this run.
   - **Zero flipped**: abort. Report that `/speckit-implement` didn't complete the task (or didn't mark it
     `[X]`) and nothing will be committed/pushed/opened as a PR. Leave the branch as-is for the user to
     inspect.
   - **Exactly the one task ID from step 2 flipped**: this is the expected path — continue to step 5.
   - **More than one flipped** (including the target task): this is scope creep — `/speckit-implement`
     batched extra tasks despite being invoked for a single task ID. **Do not silently proceed.** Report
     precisely, e.g.:

     ```text
     speckit-git-pr: this run completed more than one task (T004, T005, T006), not just T004. Opening a PR
     here would break the one-PR-per-issue invariant. Nothing has been committed, pushed, or opened.

     Options:
       (a) manually split T005/T006's changes onto their own branches/commits before I open PRs for each,
       (b) accept one PR here closing multiple issues (#4, #5, #6) as a one-off exception,
       (c) revert T005/T006's changes on this branch and re-run them separately later.

     Tell me which you'd like, or handle it yourself and re-invoke /speckit-git-pr when ready.
     ```

     Then stop and wait — this needs a human decision, not an automatic resolution.

5. Map the confirmed task ID to its GitHub issue number. Confirmed convention this session: issue number
   == task number (e.g. `T004` → issue `#4`) in the repo resolved via `git config --get remote.origin.url`
   (must be a GitHub remote; abort if not). Don't assume this holds for every feature without checking —
   if unsure, `gh issue list --repo <owner>/<repo> --search "T### in:title" --json number,title` and match
   the title's leading `T###:` token instead of assuming the number.

6. Read the task's own line in `tasks.md` for its description text (used in the commit/PR title).

7. `git add -A && git commit -m "T###: <task description>"` — if `CLAUDE_GH_APP_SLUG` and `CLAUDE_GH_APP_ID` are set
   (bot identity configured), instead commit as the bot:
   `git -c user.name="${CLAUDE_GH_APP_SLUG}[bot]" -c user.email="${CLAUDE_GH_APP_ID}+${CLAUDE_GH_APP_SLUG}[bot]@users.noreply.github.com" commit -m "T###: <task description>"`.

8. `git push -u origin <branch>`.

9. `gh pr create --repo <owner>/<repo> --title "T###: <task description>" --body`:

   ```text
   Implements T### per <feature-dir>/tasks.md.

   Closes #<issue-number>

   ## Verification
   <the test/build command(s) run and their result during this task, e.g. `dotnet test tests/... — N passed`>
   ```

10. Report the PR URL back to the user, and remind them `/speckit-pr-sync` is how to check on review status
    later.

## Done When

- [ ] Either: aborted cleanly with a clear reason (wrong branch, nothing completed, or scope creep needing
  a human decision) and nothing was committed/pushed/opened —
- [ ] Or: exactly the gated task's changes were committed, pushed, and a PR was opened closing exactly that
  task's GitHub issue, and the PR URL was reported.
