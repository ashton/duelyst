import plist from "plist";
import type { Atlas, Frame } from "./types";

const parsePlistXml = plist.parse;

interface Point {
  x: number;
  y: number;
}

interface Size {
  w: number;
  h: number;
}

interface Rect extends Point, Size {}

/** Parses cocos2d string-encoded points/rects/sizes: "{x,y}", "{w,h}", "{{x,y},{w,h}}". */
function parseNumberPair(s: string): [number, number] {
  const match = /\{\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\}/.exec(s);
  if (!match) {
    throw new Error(`Malformed cocos2d point/size string: "${s}"`);
  }
  return [Number(match[1]), Number(match[2])];
}

function parsePoint(s: string): Point {
  const [x, y] = parseNumberPair(s);
  return { x, y };
}

function parseSize(s: string): Size {
  const [w, h] = parseNumberPair(s);
  return { w, h };
}

function parseRect(s: string): Rect {
  const match = /\{\{\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\}\s*,\s*\{\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\}\s*\}/.exec(s);
  if (!match) {
    throw new Error(`Malformed cocos2d rect string: "${s}"`);
  }
  return { x: Number(match[1]), y: Number(match[2]), w: Number(match[3]), h: Number(match[4]) };
}

interface V2FrameEntry {
  frame: string;
  offset: string;
  rotated: boolean;
  sourceSize: string;
}

interface V3FrameEntry {
  textureRect: string;
  spriteOffset: string;
  textureRotated: boolean;
  spriteSourceSize: string;
}

/**
 * Translates a Cocos2d-JS plist (v2 or v3 key set) into the project-native normalized
 * atlas shape. Frame rects are copied as packed in the atlas (pre-rotation-swap); the
 * runtime resolver — not this translator — swaps w/h for rotated frames (R4).
 */
export function translatePlist(xml: string, imagePath: string): Atlas {
  const parsed = parsePlistXml(xml) as unknown as {
    frames: Record<string, V2FrameEntry | V3FrameEntry>;
  };

  const frameEntries = Object.entries(parsed.frames);
  const isV3 = frameEntries.length > 0 && "textureRect" in frameEntries[0]![1];

  const frames: Frame[] = frameEntries.map(([name, entry]) => {
    if (isV3) {
      const v3 = entry as V3FrameEntry;
      const rect = parseRect(v3.textureRect);
      const offset = parsePoint(v3.spriteOffset);
      const srcSize = parseSize(v3.spriteSourceSize);
      return {
        name,
        x: rect.x,
        y: rect.y,
        w: rect.w,
        h: rect.h,
        rotated: v3.textureRotated === true,
        offsetX: offset.x,
        offsetY: offset.y,
        srcW: srcSize.w,
        srcH: srcSize.h
      };
    }

    const v2 = entry as V2FrameEntry;
    const rect = parseRect(v2.frame);
    const offset = parsePoint(v2.offset);
    const srcSize = parseSize(v2.sourceSize);
    return {
      name,
      x: rect.x,
      y: rect.y,
      w: rect.w,
      h: rect.h,
      rotated: v2.rotated === true,
      offsetX: offset.x,
      offsetY: offset.y,
      srcW: srcSize.w,
      srcH: srcSize.h
    };
  });

  // Stable sort by frame name — required for byte-reproducible atlases.json (SC-002).
  frames.sort((a, b) => a.name.localeCompare(b.name));

  return { image: imagePath, frames };
}
