import { describe, expect, it } from "vitest";
import type { AtlasTable, ResourcesManifest } from "../src/types";
import { checkFormatLeaks, checkReferentialIntegrity, checkStableKeyOrder } from "../src/verify";

describe("checkReferentialIntegrity", () => {
  const atlases: AtlasTable = {
    "units/hero.png": {
      image: "units/hero.png",
      frames: [
        { name: "hero_idle_0.png", x: 0, y: 0, w: 10, h: 10, rotated: false, offsetX: 0, offsetY: 0, srcW: 10, srcH: 10 }
      ]
    }
  };
  const pngSizes = new Map<string, { width: number; height: number }>([
    ["units/hero.png", { width: 100, height: 100 }],
    ["units/portrait.png", { width: 64, height: 64 }]
  ]);

  it("passes for a fully-resolvable manifest with in-bounds frames", () => {
    const resources: ResourcesManifest = {
      heroIdle: { kind: "animation", img: "units/hero.png", framePrefix: "hero_idle_", frameDelay: 0.1 },
      heroPortrait: { kind: "texture", img: "units/portrait.png" }
    };
    const issues = checkReferentialIntegrity(resources, atlases, pngSizes);
    expect(issues).toEqual([]);
  });

  it("detects a dangling image reference (missing PNG)", () => {
    const resources: ResourcesManifest = {
      ghost: { kind: "texture", img: "units/does-not-exist.png" }
    };
    const issues = checkReferentialIntegrity(resources, atlases, pngSizes);
    expect(issues.some((i) => i.code === "dangling-reference" && i.message.includes("units/does-not-exist.png"))).toBe(
      true
    );
  });

  it("detects a dangling sprite frame reference (missing frame name in atlas)", () => {
    const resources: ResourcesManifest = {
      badSprite: { kind: "sprite", img: "units/hero.png", frame: "hero_not_a_frame.png" }
    };
    const issues = checkReferentialIntegrity(resources, atlases, pngSizes);
    expect(issues.some((i) => i.code === "dangling-reference" && i.message.includes("hero_not_a_frame.png"))).toBe(
      true
    );
  });

  it("detects an out-of-bounds frame rect", () => {
    const oob: AtlasTable = {
      "units/hero.png": {
        image: "units/hero.png",
        frames: [
          { name: "hero_idle_0.png", x: 90, y: 90, w: 20, h: 20, rotated: false, offsetX: 0, offsetY: 0, srcW: 20, srcH: 20 }
        ]
      }
    };
    const resources: ResourcesManifest = {
      heroIdle: { kind: "sprite", img: "units/hero.png", frame: "hero_idle_0.png" }
    };
    const issues = checkReferentialIntegrity(resources, oob, pngSizes);
    expect(issues.some((i) => i.code === "out-of-bounds-frame")).toBe(true);
  });
});

describe("checkFormatLeaks", () => {
  it("flags any .plist or .xml file under the given file list", () => {
    const files = ["units/hero.png", "units/hero.plist", "i18n/en.json", "fonts/foo.xml"];
    const issues = checkFormatLeaks(files);
    expect(issues).toHaveLength(2);
    expect(issues.map((i) => i.message).join(" ")).toContain("units/hero.plist");
    expect(issues.map((i) => i.message).join(" ")).toContain("fonts/foo.xml");
  });

  it("passes when no plist/xml files are present", () => {
    const issues = checkFormatLeaks(["units/hero.png", "i18n/en.json"]);
    expect(issues).toEqual([]);
  });
});

describe("checkStableKeyOrder", () => {
  it("passes when keys are already sorted", () => {
    const issues = checkStableKeyOrder({ a: 1, b: 2, c: 3 }, "resources.json");
    expect(issues).toEqual([]);
  });

  it("flags non-reproducible (unsorted) key ordering", () => {
    const issues = checkStableKeyOrder({ b: 2, a: 1 }, "resources.json");
    expect(issues.some((i) => i.code === "non-reproducible-ordering")).toBe(true);
  });
});
