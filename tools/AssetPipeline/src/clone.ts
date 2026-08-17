import { execFileSync } from "node:child_process";
import { existsSync, readdirSync } from "node:fs";

export interface CloneOptions {
  sourceRepo: string;
  sourceCommit: string;
  targetDir: string;
}

export class SourceAcquisitionError extends Error {}

function git(args: string[], cwd?: string): string {
  return execFileSync("git", args, { cwd, encoding: "utf-8", stdio: ["ignore", "pipe", "pipe"] }).trim();
}

/**
 * Idempotent clone/checkout of the pinned source (spec FR-001/FR-005, research R2):
 * - absent -> clone it (shallow fetch pinned to sourceCommit).
 * - present -> reuse, but verify HEAD matches the pinned commit; mismatch fails loudly.
 */
export function ensureCloned(opts: CloneOptions): void {
  const exists = existsSync(opts.targetDir) && readdirSync(opts.targetDir).length > 0;

  if (!exists) {
    try {
      git(["init", "-q", opts.targetDir]);
      git(["remote", "add", "origin", opts.sourceRepo], opts.targetDir);
      git(["fetch", "--depth", "1", "origin", opts.sourceCommit], opts.targetDir);
      git(["checkout", "-q", "FETCH_HEAD"], opts.targetDir);
    } catch (err) {
      throw new SourceAcquisitionError(
        `Failed to clone ${opts.sourceRepo}@${opts.sourceCommit} into "${opts.targetDir}": ${(err as Error).message}`
      );
    }
    return;
  }

  let head: string;
  try {
    head = git(["rev-parse", "HEAD"], opts.targetDir);
  } catch (err) {
    throw new SourceAcquisitionError(
      `"${opts.targetDir}" exists but is not a valid git checkout (expected ${opts.sourceRepo}@${opts.sourceCommit}): ${
        (err as Error).message
      }`
    );
  }

  if (head !== opts.sourceCommit) {
    throw new SourceAcquisitionError(
      `"${opts.targetDir}" is checked out at ${head}, expected pinned commit ${opts.sourceCommit} of ${opts.sourceRepo}. ` +
        `Remove or update the directory to match the pinned commit.`
    );
  }
}
