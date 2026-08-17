import { createRequire } from "node:module";
import type { Descriptor, ResourcesManifest } from "./types";

interface RawRsxEntry {
  name: string;
  img?: string;
  frame?: string;
  framePrefix?: string;
  frameDelay?: number;
  audio?: string;
  plist?: string;
  font?: string;
  imgPosX?: string;
}

/** `require`s the original app/data/resources.js (RSX) — R1: read the JS source of truth directly. */
export function loadRawResources(resourcesJsPath: string): Record<string, RawRsxEntry> {
  const require = createRequire(resourcesJsPath);
  const rsx = require(resourcesJsPath) as Record<string, RawRsxEntry | ((...args: unknown[]) => unknown)>;
  const out: Record<string, RawRsxEntry> = {};
  for (const [key, value] of Object.entries(rsx)) {
    if (typeof value === "function") continue; // RSX.getResourcesByPath etc. — helper methods, not descriptors.
    out[key] = value;
  }
  return out;
}

function classifyDescriptor(alias: string, entry: RawRsxEntry): Descriptor | undefined {
  if (entry.audio !== undefined) {
    return { kind: "audio", audio: entry.audio };
  }
  if (entry.framePrefix !== undefined) {
    if (entry.img === undefined || entry.frameDelay === undefined) return undefined;
    return { kind: "animation", img: entry.img, framePrefix: entry.framePrefix, frameDelay: entry.frameDelay };
  }
  if (entry.frame !== undefined) {
    if (entry.img === undefined) return undefined;
    return { kind: "sprite", img: entry.img, frame: entry.frame };
  }
  if (entry.img !== undefined) {
    return { kind: "texture", img: entry.img };
  }
  // Cubemaps (imgPosX/...), bitmap/ttf fonts, and other exotic RSX shapes are out of scope
  // for this milestone's resources.schema.json (texture|sprite|animation|audio only).
  return undefined;
}

/**
 * Builds the slice-scoped resources.json (contracts/resources.schema.json): only the aliases
 * reachable from the configured slice (card resourceIds + explicit extraResourceAliases) —
 * see pipeline.config.json `slice`. Output keys are sorted for byte-reproducibility (SC-002).
 */
export function buildResourcesManifest(
  raw: Record<string, RawRsxEntry>,
  aliases: Iterable<string>
): ResourcesManifest {
  const manifest: ResourcesManifest = {};
  for (const alias of new Set(aliases)) {
    const entry = raw[alias];
    if (!entry) {
      throw new Error(`resources.js has no RSX entry named "${alias}" (referenced by the configured slice)`);
    }
    const descriptor = classifyDescriptor(alias, entry);
    if (!descriptor) {
      throw new Error(`RSX entry "${alias}" does not map to a texture/sprite/animation/audio descriptor`);
    }
    manifest[alias] = descriptor;
  }
  return sortByKey(manifest);
}

export function sortByKey<T>(obj: Record<string, T>): Record<string, T> {
  const sorted: Record<string, T> = {};
  for (const key of Object.keys(obj).sort((a, b) => a.localeCompare(b))) {
    sorted[key] = obj[key]!;
  }
  return sorted;
}
