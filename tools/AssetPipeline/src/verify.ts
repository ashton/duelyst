import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import type { AtlasTable, ResourcesManifest } from "./types";

export type VerifyIssueCode =
  | "dangling-reference"
  | "out-of-bounds-frame"
  | "format-leak"
  | "non-reproducible-ordering";

export interface VerifyIssue {
  code: VerifyIssueCode;
  message: string;
}

export interface PngSize {
  width: number;
  height: number;
}

/**
 * Referential integrity + in-bounds checks (FR-004, SC-003):
 * - every `img`/`audio` path in resources.json must exist (resolvable PNG size given).
 * - every sprite `frame` / animation `framePrefix+index` must resolve to a frame in atlases.json.
 * - every resolved frame rect must lie within its PNG's bounds.
 */
export function checkReferentialIntegrity(
  resources: ResourcesManifest,
  atlases: AtlasTable,
  pngSizes: Map<string, PngSize>
): VerifyIssue[] {
  const issues: VerifyIssue[] = [];

  const checkImageExists = (img: string): PngSize | undefined => {
    const size = pngSizes.get(img);
    if (!size) {
      issues.push({ code: "dangling-reference", message: `resources.json references missing image "${img}"` });
    }
    return size;
  };

  const checkFrameInBounds = (image: string, frameName: string, size: PngSize) => {
    const atlas = atlases[image];
    const frame = atlas?.frames.find((f) => f.name === frameName);
    if (!frame) {
      issues.push({
        code: "dangling-reference",
        message: `resources.json references frame "${frameName}" not present in atlases.json for "${image}"`
      });
      return;
    }
    if (frame.x < 0 || frame.y < 0 || frame.x + frame.w > size.width || frame.y + frame.h > size.height) {
      issues.push({
        code: "out-of-bounds-frame",
        message: `frame "${frameName}" in "${image}" (x=${frame.x},y=${frame.y},w=${frame.w},h=${frame.h}) exceeds PNG bounds ${size.width}x${size.height}`
      });
    }
  };

  for (const descriptor of Object.values(resources)) {
    switch (descriptor.kind) {
      case "texture": {
        checkImageExists(descriptor.img);
        break;
      }
      case "sprite": {
        const size = checkImageExists(descriptor.img);
        if (size) checkFrameInBounds(descriptor.img, descriptor.frame, size);
        break;
      }
      case "animation": {
        const size = checkImageExists(descriptor.img);
        if (size) {
          const atlas = atlases[descriptor.img];
          const frameNames =
            atlas?.frames.map((f) => f.name).filter((n) => n.startsWith(descriptor.framePrefix)) ?? [];
          if (frameNames.length === 0) {
            issues.push({
              code: "dangling-reference",
              message: `animation framePrefix "${descriptor.framePrefix}" matches no frames in "${descriptor.img}"`
            });
          } else {
            for (const frameName of frameNames) checkFrameInBounds(descriptor.img, frameName, size);
          }
        }
        break;
      }
      case "audio": {
        checkImageExists(descriptor.audio);
        break;
      }
    }
  }

  // Every atlas frame must itself sit within its PNG's bounds, independent of resources.json usage.
  for (const atlas of Object.values(atlases)) {
    const size = pngSizes.get(atlas.image);
    if (!size) {
      issues.push({ code: "dangling-reference", message: `atlases.json references missing image "${atlas.image}"` });
      continue;
    }
    for (const frame of atlas.frames) {
      if (frame.x < 0 || frame.y < 0 || frame.x + frame.w > size.width || frame.y + frame.h > size.height) {
        issues.push({
          code: "out-of-bounds-frame",
          message: `frame "${frame.name}" in "${atlas.image}" (x=${frame.x},y=${frame.y},w=${frame.w},h=${frame.h}) exceeds PNG bounds ${size.width}x${size.height}`
        });
      }
    }
  }

  return dedupe(issues);
}

function dedupe(issues: VerifyIssue[]): VerifyIssue[] {
  const seen = new Set<string>();
  return issues.filter((i) => {
    const key = `${i.code}:${i.message}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

/** No Cocos2d/XML format may ever reach assets/ (FR-012). */
export function checkFormatLeaks(relativeFilePaths: string[]): VerifyIssue[] {
  return relativeFilePaths
    .filter((p) => p.endsWith(".plist") || p.endsWith(".xml"))
    .map((p) => ({ code: "format-leak" as const, message: `disallowed format file leaked into assets/: "${p}"` }));
}

/** Object.entries preserves insertion order; reproducibility requires that order be sorted-by-key. */
export function checkStableKeyOrder(obj: Record<string, unknown>, label: string): VerifyIssue[] {
  const keys = Object.keys(obj);
  const sorted = [...keys].sort((a, b) => a.localeCompare(b));
  const stable = keys.every((k, i) => k === sorted[i]);
  return stable
    ? []
    : [
        {
          code: "non-reproducible-ordering",
          message: `${label} keys are not in stable sorted order — output would not be byte-reproducible`
        }
      ];
}

export interface RunVerifyOptions {
  assetsRoot: string;
  resources: ResourcesManifest;
  atlases: AtlasTable;
  cards: unknown[];
  pngSizes: Map<string, PngSize>;
  presentFiles: string[];
}

export function runVerify(opts: RunVerifyOptions): VerifyIssue[] {
  return [
    ...checkReferentialIntegrity(opts.resources, opts.atlases, opts.pngSizes),
    ...checkFormatLeaks(opts.presentFiles),
    ...checkStableKeyOrder(opts.resources as unknown as Record<string, unknown>, "resources.json"),
    ...checkStableKeyOrder(opts.atlases as unknown as Record<string, unknown>, "atlases.json")
  ];
}

/** Reads the PNG IHDR chunk directly (width/height are the first 8 bytes after a fixed 16-byte header). */
export function readPngSize(absPath: string): PngSize {
  const fd = readFileSync(absPath);
  if (fd.length < 24 || fd.readUInt32BE(0) !== 0x89504e47) {
    throw new Error(`Not a valid PNG (or too small): ${absPath}`);
  }
  const width = fd.readUInt32BE(16);
  const height = fd.readUInt32BE(20);
  return { width, height };
}

export function listFilesRecursive(root: string): string[] {
  const out: string[] = [];
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir)) {
      const abs = path.join(dir, entry);
      const stat = statSync(abs);
      if (stat.isDirectory()) walk(abs);
      else out.push(path.relative(root, abs));
    }
  };
  walk(root);
  return out;
}
