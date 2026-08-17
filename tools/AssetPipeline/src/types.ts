export interface Frame {
  name: string;
  x: number;
  y: number;
  w: number;
  h: number;
  rotated: boolean;
  offsetX: number;
  offsetY: number;
  srcW: number;
  srcH: number;
}

export interface Atlas {
  image: string;
  frames: Frame[];
}

/** Keyed by atlas PNG path, relative to assets/. */
export type AtlasTable = Record<string, Atlas>;

export type ResourceKind = "texture" | "sprite" | "animation" | "audio";

export interface TextureDescriptor {
  kind: "texture";
  img: string;
}

export interface SpriteDescriptor {
  kind: "sprite";
  img: string;
  frame: string;
}

export interface AnimationDescriptor {
  kind: "animation";
  img: string;
  framePrefix: string;
  frameDelay: number;
}

export interface AudioDescriptor {
  kind: "audio";
  audio: string;
}

export type Descriptor = TextureDescriptor | SpriteDescriptor | AnimationDescriptor | AudioDescriptor;

/** Keyed by alias. */
export type ResourcesManifest = Record<string, Descriptor>;

export interface CardMeta {
  id: number;
  name: string;
  faction: string;
  cost: number;
  cardType: "unit" | "spell" | "artifact" | "general";
  atk?: number;
  hp?: number;
  rarity: string;
  race: string;
  set: string;
  resourceIds: string[];
  description: string;
}
