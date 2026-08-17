import { execFileSync } from "node:child_process";

/**
 * Publishes the vertical slice into the git-LFS committed set. Because the earlier stages are
 * already slice-scoped (pipeline.config.json `slice` drives copyAssets/resources/cards/atlases —
 * see resources.ts), everything under `assets/` at this point IS the curated slice; publishing
 * is simply staging it for commit (git-LFS attributes for png/ogg/wav/mp3 are already declared
 * in the repo root .gitattributes). Does not commit — leaves changes staged.
 */
export function publishSlice(repoRoot: string, assetsRelPath: string): string[] {
  execFileSync("git", ["add", assetsRelPath], { cwd: repoRoot, stdio: "pipe" });
  const status = execFileSync("git", ["status", "--porcelain", "--", assetsRelPath], {
    cwd: repoRoot,
    encoding: "utf-8"
  });
  return status
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}
