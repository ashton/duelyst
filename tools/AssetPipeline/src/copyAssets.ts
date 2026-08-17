import { copyFileSync, mkdirSync } from "node:fs";
import path from "node:path";
import type { ResourcesManifest } from "./types";

/** Collects every png/audio path (RSX-relative, e.g. "resources/units/f1_general.png") a resources manifest needs. */
export function collectCopyPaths(resources: ResourcesManifest): string[] {
  const paths = new Set<string>();
  for (const descriptor of Object.values(resources)) {
    switch (descriptor.kind) {
      case "texture":
      case "sprite":
      case "animation":
        paths.add(descriptor.img);
        break;
      case "audio":
        paths.add(descriptor.audio);
        break;
    }
  }
  return [...paths].sort((a, b) => a.localeCompare(b));
}

/**
 * Copies png/audio/fx from the original checkout into assets/, explicitly excluding `.plist`
 * (FR-012 — no Cocos2d/XML format ever reaches the project). Paths are RSX-relative
 * (e.g. "resources/units/f1_general.png") and preserved verbatim under both `app/` in the
 * source and `assets/` in the destination.
 */
export function copyAssets(duelystRoot: string, assetsRoot: string, relativePaths: string[]): string[] {
  const copied: string[] = [];
  for (const rel of relativePaths) {
    if (rel.endsWith(".plist") || rel.endsWith(".xml")) {
      throw new Error(`Refusing to copy disallowed format file into assets/: "${rel}" (FR-012)`);
    }
    const src = path.join(duelystRoot, "app", rel);
    const dest = path.join(assetsRoot, rel);
    mkdirSync(path.dirname(dest), { recursive: true });
    copyFileSync(src, dest);
    copied.push(rel);
  }
  return copied;
}
