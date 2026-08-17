import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { translatePlist } from "../src/plistToAtlases";

const fixturesDir = path.dirname(fileURLToPath(import.meta.url)) + "/fixtures";

function loadFixture(name: string): string {
  return readFileSync(path.join(fixturesDir, name), "utf-8");
}

describe("translatePlist", () => {
  it("translates a v2 plist into the normalized atlas shape", () => {
    const xml = loadFixture("v2-basic.plist");
    const atlas = translatePlist(xml, "units/fixture_v2.png");

    expect(atlas.image).toBe("units/fixture_v2.png");
    expect(atlas.frames).toHaveLength(2);

    const frame0 = atlas.frames.find((f) => f.name === "fixture_idle_000.png");
    expect(frame0).toEqual({
      name: "fixture_idle_000.png",
      x: 2,
      y: 4,
      w: 80,
      h: 80,
      rotated: false,
      offsetX: 0,
      offsetY: 0,
      srcW: 80,
      srcH: 80
    });
  });

  it("translates a v3 plist into the normalized atlas shape", () => {
    const xml = loadFixture("v3-basic.plist");
    const atlas = translatePlist(xml, "units/fixture_v3.png");

    expect(atlas.image).toBe("units/fixture_v3.png");
    expect(atlas.frames).toEqual([
      {
        name: "fixture_v3_idle_000.png",
        x: 3,
        y: 6,
        w: 72,
        h: 72,
        rotated: false,
        offsetX: 0,
        offsetY: 0,
        srcW: 72,
        srcH: 72
      }
    ]);
  });

  it("preserves rotated frame metadata (v2)", () => {
    const xml = loadFixture("v2-rotated-trimmed.plist");
    const atlas = translatePlist(xml, "units/fixture_v2_rt.png");
    const rotated = atlas.frames.find((f) => f.name === "fixture_rotated.png");

    expect(rotated).toBeDefined();
    expect(rotated!.rotated).toBe(true);
    // Packed footprint as stored in the atlas (pre-rotation-swap) — the pipeline
    // does not swap w/h itself; the runtime resolver does that (R4/assets-runtime-contract).
    expect(rotated!.w).toBe(40);
    expect(rotated!.h).toBe(60);
  });

  it("preserves rotated frame metadata (v3)", () => {
    const xml = loadFixture("v3-rotated-trimmed.plist");
    const atlas = translatePlist(xml, "units/fixture_v3_rt.png");
    const rotated = atlas.frames.find((f) => f.name === "fixture_v3_rotated.png");

    expect(rotated).toBeDefined();
    expect(rotated!.rotated).toBe(true);
    expect(rotated!.w).toBe(50);
    expect(rotated!.h).toBe(20);
    expect(rotated!.srcW).toBe(20);
    expect(rotated!.srcH).toBe(50);
  });

  it("preserves trimmed frame offset + untrimmed source size (v2)", () => {
    const xml = loadFixture("v2-rotated-trimmed.plist");
    const atlas = translatePlist(xml, "units/fixture_v2_rt.png");
    const trimmed = atlas.frames.find((f) => f.name === "fixture_trimmed.png");

    expect(trimmed).toEqual({
      name: "fixture_trimmed.png",
      x: 60,
      y: 20,
      w: 30,
      h: 30,
      rotated: false,
      offsetX: -5,
      offsetY: 3,
      srcW: 40,
      srcH: 50
    });
  });

  it("preserves trimmed frame offset + untrimmed source size (v3)", () => {
    const xml = loadFixture("v3-rotated-trimmed.plist");
    const atlas = translatePlist(xml, "units/fixture_v3_rt.png");
    const trimmed = atlas.frames.find((f) => f.name === "fixture_v3_trimmed.png");

    expect(trimmed).toEqual({
      name: "fixture_v3_trimmed.png",
      x: 70,
      y: 25,
      w: 25,
      h: 35,
      rotated: false,
      offsetX: 2,
      offsetY: -4,
      srcW: 35,
      srcH: 45
    });
  });

  it("emits frames in stable, sorted-by-name order for reproducibility", () => {
    const xml = loadFixture("v2-basic.plist");
    const atlas = translatePlist(xml, "units/fixture_v2.png");
    const names = atlas.frames.map((f) => f.name);
    expect(names).toEqual([...names].sort());
  });
});
