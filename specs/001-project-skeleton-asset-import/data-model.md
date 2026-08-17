# Phase 1 Data Model: Project Skeleton & Reproducible Asset Import

Domain-first (Constitution I): the manifest/atlas schemas are modeled before any loader or renderer. Two
representations exist for the same data — the **on-disk JSON** the pipeline emits (contracts in
[`contracts/`](./contracts/)) and the **F# records** the runtime parses. They MUST stay in lock-step.

## Entities

### 1. ResourcesManifest (`assets/resources.json`)

A map of **alias → descriptor**, derived from the original `RSX` object. The alias (e.g. `f1GeneralIdle`) is
the logical name card/content code references.

| Field | Type | Notes |
|-------|------|-------|
| `alias` (map key) | string | Unique. Canonical logical resource name. |
| `kind` | enum `texture \| sprite \| animation \| audio` | Discriminates the descriptor shape. |
| `img` | string (path) | Present for texture/sprite/animation. Relative to `assets/`. |
| `frame` | string | sprite only — frame name within the atlas. |
| `framePrefix` | string | animation only — frames named `framePrefix+index`. |
| `frameDelay` | number (seconds) | animation only — per-frame delay. |
| `audio` | string (path) | audio only. Relative to `assets/`. |

**F# model** (`AtlasManifest.fs`):

```fsharp
type Descriptor =
    | Texture   of img: string
    | Sprite    of img: string * frame: string
    | Animation of img: string * framePrefix: string * frameDelay: float
    | Audio     of path: string
type ResourcesManifest = Map<string, Descriptor>   // key = alias
```

**Validation**: every `img`/`audio` path MUST exist under `assets/` (FR-004, SC-003). `kind` MUST match the
present fields (illegal states unrepresentable via the DU).

### 2. AtlasTable (`assets/atlases.json`)

Per-atlas frame tables, **normalized from the original `.plist`** (R3). Keyed by atlas image path.

| Field | Type | Notes |
|-------|------|-------|
| `image` (map key) | string (path) | The atlas PNG, relative to `assets/`. |
| `frames[].name` | string | Frame name; matches `frame`/`framePrefix+index` in ResourcesManifest. |
| `frames[].x,y,w,h` | int | Sub-rectangle in the PNG (packed rect). |
| `frames[].rotated` | bool | If true, frame is packed at 90°; draw swaps w/h and rotates. |
| `frames[].offsetX,offsetY` | int | Trim offset of the sprite within its untrimmed canvas. |
| `frames[].srcW,srcH` | int | Original (untrimmed) sprite size. |

**F# model**:

```fsharp
type Frame =
    { Name: string; X: int; Y: int; W: int; H: int
      Rotated: bool; OffsetX: int; OffsetY: int; SrcW: int; SrcH: int }
type Atlas = { Image: string; Frames: Map<string, Frame> }   // key = frame name
type AtlasTable = Map<string, Atlas>                          // key = image path
```

**Validation** (FsCheck in `AtlasFrameTests.fs`):
- `x ≥ 0 && y ≥ 0 && x + w ≤ pngWidth && y + h ≤ pngHeight` — every frame lies within its PNG bounds.
- `w > 0 && h > 0`.
- No `.plist`/XML file exists under `assets/` (format-leak guard, FR-012).

### 3. CardMeta (`assets/cards.json`)

Card metadata extracted from `app/sdk/cards/**`. Not rendered this milestone, but produced by the pipeline
and validated as non-empty; seeds later content work.

| Field | Type | Notes |
|-------|------|-------|
| `id` | int | Original numeric id (reused verbatim). Unique. |
| `name` | string | |
| `faction` | string | |
| `cost` | int | |
| `cardType` | string | unit / spell / artifact / general |
| `atk`,`hp` | int? | units/generals only |
| `rarity`,`race`,`set` | string | |
| `resourceIds` | string[] | aliases into ResourcesManifest (idle/attack/death/sfx…). |
| `description` | string | (localized text may come via i18n). |

**Validation**: `id` unique; each `resourceIds` entry SHOULD resolve in ResourcesManifest for committed-slice
cards.

### 4. ResolvedSprite (runtime, derived — not persisted)

Output of the **pure resolver** (`Manifest.fs`): given an `alias` + the two manifests, produce what Raylib
needs. This is the seam tested without a window (Constitution III).

```fsharp
type ResolvedSprite =
    { Image: string                 // atlas PNG to load
      Source: Rectangle             // sub-rect (w/h swapped if Rotated)
      Rotated: bool
      Origin: Vector2               // from offset/srcSize for correct placement
      Frames: Frame list            // >1 for animations
      FrameDelay: float }           // 0 for static
type Rectangle = { X: float; Y: float; W: float; H: float }
```

**Resolution rules**:
- `Texture img` → single full-image `Source` (0,0,pngW,pngH).
- `Sprite(img, frame)` → look up `frame` in `AtlasTable[img]`; `Source = frame rect` (swap w/h if `rotated`).
- `Animation(img, prefix, delay)` → all frames named `prefix+index` in order; `FrameDelay = delay`.
- Missing alias / missing frame / missing image → a typed error (`Result<ResolvedSprite, ResolveError>`),
  surfaced clearly (FR-006, "corrupt/unreadable asset" edge case).

### 5. Client Model (immediate-mode / TEA — Constitution IV)

```fsharp
type Model = { Sprite: ResolvedSprite; ElapsedTime: float; Texture: Texture2D }
type Msg   = | Tick of dt: float | Close
// update: Tick advances ElapsedTime -> current frame index; Close ends the loop
// view: pure function Model -> draw calls (no retained widget state)
```

## Relationships

```
CardMeta.resourceIds ──► ResourcesManifest.alias ──► Descriptor.img ──► AtlasTable.image ──► Frame(s)
                                                                                   │
                                                              Manifest.resolve ────┴──► ResolvedSprite ──► Client renders
```

## State transitions

Only the client animation loop has state-over-time: `ElapsedTime` monotonically increases on each `Tick` and
maps to `frameIndex = floor(ElapsedTime / FrameDelay) mod Frames.length`. All other entities are immutable
data produced once by the pipeline.
