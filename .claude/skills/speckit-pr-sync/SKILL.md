---
name: "speckit-pr-sync"
description: "Check the current feature's open phase PR(s) for review feedback, address comments, or merge on approval and report the next phase"
argument-hint: "Optional phase key (e.g. US1, Foundational) to sync just that one PR; omit to sync every open story/<feature>-* PR"
metadata:
  author: "project-custom"
user-invocable: true
disable-model-invocation: false
---

## Purpose

The manual review-loop step of the branch → PR → review-loop → merge workflow. Run this after leaving (or
expecting) review comments on a phase PR opened by `speckit-git-pr`, or after approving one. It does
exactly one of: push a fix for outstanding feedback, merge an approved PR, or report there's nothing to do
yet — never more than one PR-affecting action per PR per invocation, and never auto-starts
`/speckit-implement` for the next phase (that stays a manual, separate step by design).

## Outline

0. **Bot identity (optional, API calls only).** If `CLAUDE_GH_APP_ID`, `CLAUDE_GH_APP_INSTALLATION_ID`, and
   `CLAUDE_GH_APP_PRIVATE_KEY_PATH` are all set, run
   `export GH_TOKEN="$(.specify/scripts/bash/gh-app-token.sh)"` so every `gh`/`gh api` call below (PR
   viewing/merging, review-comment fetching) authenticates as the GitHub App's bot identity instead of the
   locally logged-in personal account. If any are unset, skip silently and fall back to the current
   `gh auth` session. This is scoped to `gh` calls only — the fix commit in step 3d always uses the
   default local git identity; the bot identity never appears as a commit author/committer.

1. Resolve `FEATURE_DIR` via `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`; let
   `feature-slug` = `basename "$FEATURE_DIR"`. Resolve the GitHub repo via
   `git config --get remote.origin.url` (must be a GitHub remote; abort if not, same check
   `speckit-taskstoissues` uses).

2. Determine which PR(s) are in scope:
   - If an argument like `US1` was given: scope to branch `story/<feature-slug>-US1` only.
   - If no argument: `gh pr list --repo <owner>/<repo> --state open --json number,url,headRefName` and
     filter client-side for `headRefName` starting with `story/<feature-slug>-` (`gh pr list --head`
     matches an exact branch, not a prefix). If none are found, report "no open phase PRs for this
     feature" and stop.

3. For **each** PR in scope, independently:

   a. `gh pr view <N> --repo <owner>/<repo> --json number,url,headRefName,reviewDecision,reviews,comments,mergeable,state`.

   b. **If `state != "OPEN"`**: report it's already closed/merged and skip.

   c. **If `reviewDecision == "APPROVED"`**:
      - `gh pr merge <N> --repo <owner>/<repo> --squash --delete-branch`.
      - `git checkout main && git pull --ff-only origin main`.
      - Extract `PhaseKey` from `headRefName` and report the merge succeeded, naming the phase and how many
        issues it closed.
      - Resolve `TASKS` via `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`, parse every
        `## Phase N: <Title>` header (same key-derivation rules as `speckit-git-branch`/`speckit-git-pr`:
        `US<N>` if the title contains `User Story (\d+)`, else the title's first word), and find the first
        phase (in file order) with a non-empty remaining-unchecked-task set. Report it as the suggested next
        phase, with its task range — **do not** invoke `/speckit-implement` automatically; just name it.

   d. **Else if there are unresolved review comments or a "changes requested" review** (a `reviews[]` entry
      with `state == "CHANGES_REQUESTED"`, or any comments not clearly superseded by a later commit):
      - Collect the feedback: `gh api repos/<owner>/<repo>/pulls/<N>/comments` for inline code-review
        comments, plus each `reviews[].body` for top-level review comments.
      - `git fetch origin && git checkout <headRefName>`.
      - Make the requested change(s) directly on that branch — read the comment(s) carefully, locate the
        referenced file/line, and fix exactly what was asked; re-run whatever test/build command is
        relevant to confirm the fix (mirror what the original phase's PR verification used).
      - `git add -A && git commit -m "$(cat <<'EOF'
Address review feedback on <phase-key>

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"` — commit author/committer stays the default local git identity; the bot identity is used only for the
        `gh`/`gh api` calls in this skill, never for commit authorship.
      - `git push` (updates the same open PR — never open a new one here).
      - Report what was changed and that the PR was updated.

   e. **Else** (no reviews yet, nothing actionable): report "no feedback yet on <phase-key>'s PR" and move
      to the next PR in scope without changing anything.

4. After processing all PRs in scope, give a one-line summary per PR (merged / fix pushed / nothing to do).

## Notes

- This command is intentionally conservative: it never merges without an explicit `APPROVED` review
  decision, and it never pushes a fix without identifiable, actionable feedback to address.
- If a PR has both new commits since the last review *and* an old `CHANGES_REQUESTED` that looks already
  addressed, use judgement — prefer treating it as "nothing new to do" and say so, rather than guessing at
  stale feedback.

## Done When

- [ ] Every PR in scope was checked exactly once and got exactly one outcome (merged, fix pushed, nothing
  to do, or skipped-with-reason).
- [ ] Any merge was followed by reporting the next phase with remaining work, without auto-invoking
  `/speckit-implement`.
