# Contract: Assets Runtime (`Duelyst.Assets`)

The game-side contract for consuming pipeline output. Split into a **pure** core (testable without a window)
and an **IO/Raylib** edge (Constitution III).

## Pure surface (unit-tested with Expecto/FsCheck)

```fsharp
// AtlasManifest.fs — parse on-disk JSON into typed records (contracts/*.schema.json).
val parseResources : json: string -> Result<ResourcesManifest, ManifestError>
val parseAtlases   : json: string -> Result<AtlasTable, ManifestError>

// Manifest.fs — resolve an alias to what Raylib needs. NO Raylib, NO file IO.
val resolve : ResourcesManifest -> AtlasTable -> alias: string -> Result<ResolvedSprite, ResolveError>
```

**Guarantees**:
- `resolve` returns the correct source `Rectangle` for a `sprite`, and for a `rotated` frame the returned
  rectangle has **w/h swapped** and `Rotated = true` (renderer applies 90° rotation).
- `animation` resolves to the ordered `Frames` list (`framePrefix+0,1,2,…`) with `FrameDelay`.
- Unknown alias, missing frame, or missing image path → `Error` (never an exception, never a silent default).

## IO / Raylib edge (smoke-tested; not unit-tested)

```fsharp
// AtlasLoader.fs
val loadTexture : assetsRoot: string -> imagePath: string -> Texture2D      // Raylib LoadTexture

// SpriteAnimator.fs
val frameAt : ResolvedSprite -> elapsedSeconds: float -> Frame              // PURE timing (unit-tested)
val draw    : Texture2D -> Frame -> dest: Rectangle -> rotated: bool -> unit // Raylib DrawTexturePro
```

**Guarantees**:
- `frameAt` is pure and total: `index = floor(elapsed / frameDelay) mod frameCount`; `frameDelay = 0` ⇒ frame 0.
- `draw` uses `DrawTexturePro(texture, sourceRect, destRect, origin, rotation, WHITE)`; `origin` derived from
  `offset`/`srcSize` so trimmed frames are positioned as in the original.
- A referenced asset that cannot be loaded surfaces a clear error rather than a blank window (spec edge case).

## Client contract (`Duelyst.Client`, immediate-mode / TEA)

- `Model = { Sprite: ResolvedSprite; Texture: Texture2D; ElapsedTime: float }`.
- `update : Msg -> Model -> Model` — `Tick dt` advances `ElapsedTime`; `Close` ends the loop. No other mutation.
- `view : Model -> unit` — pure render each frame via `SpriteAnimator.draw (frameAt sprite elapsed)`. No
  retained/stateful widgets.
- On launch, resolves one committed-slice alias and displays it within 10 s (SC-004); closing the window
  exits cleanly (FR-011).
