# Contract: Asset Pipeline CLI (`tools/AssetPipeline`)

A standalone Node.js/TypeScript tool, decoupled from `Duelyst.sln`. It only writes files under `assets/`
(and clones into `external/duelyst/`). It never carries the original engine's format (plist/XML) into the
game (FR-012).

## Invocation

```
npm run import            # full pipeline: clone -> copy -> resources -> atlases -> cards -> i18n -> publish -> verify
npm run import -- --slice # publish only the vertical-slice subset into the committed (LFS) set
npm run verify            # run verify stage only (no writes)
```

Configuration (pinned for reproducibility) lives in a committed config (e.g. `pipeline.config.json`):
`sourceRepo`, `sourceCommit` (SHA), `sourceDir` (default `external/duelyst/`), `outDir` (default `assets/`),
`slice` (general ids + card ids).

## Stages, inputs, outputs

| Stage | Reads | Writes | Notes |
|-------|-------|--------|-------|
| clone | `sourceRepo`@`sourceCommit` | `external/duelyst/` | Idempotent: reuse+verify existing checkout. |
| copyAssets | `external/duelyst/app/resources/**` | `assets/**` (png/audio/fx) | **Excludes `.plist`.** |
| resources | `app/data/resources.js` | `assets/resources.json` | Conforms to `resources.schema.json`. |
| atlases | `app/resources/**/*.plist` | `assets/atlases.json` | Conforms to `atlases.schema.json`; v2/v3 + rotated/trimmed. |
| cards | `app/sdk/cards/**` | `assets/cards.json` | Conforms to `cards.schema.json`; numeric ids reused. |
| i18n | i18next JSON | `assets/i18n/*.json` | Names/descriptions. |
| publishSlice | generated `assets/**` + slice config | committed LFS asset set | Only the slice. |
| verify | `assets/**` | (report) | See invariants below. |

## Behavioral contract (testable — maps to spec)

- **Reproducible** (FR-002, SC-002): given the same `sourceCommit`, two full runs produce **byte-identical**
  `resources.json`, `atlases.json`, `cards.json` (stable key ordering, no timestamps).
- **No format leak** (FR-012): after a run, **no `.plist` or `.xml` file exists under `assets/`**.
- **Referential integrity** (FR-004, SC-003): every `img`/`audio` in `resources.json` exists on disk; every
  `frame`/`framePrefix+index` referenced resolves to a frame in `atlases.json`; every frame rect lies within
  its PNG bounds.
- **Fail loudly** (FR-006): missing/invalid `external/duelyst/` (R2) → non-zero exit + message naming the
  expected directory and pinned commit. Missing expected source asset → error, not a dangling reference.
- **Clear reporting** (FR-006): prints a summary of counts imported per stage and any failures.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success; all stages passed and `verify` clean. |
| 1 | Source acquisition failed (clone/checkout/commit mismatch). |
| 2 | Extraction/translation error (bad plist, unreadable resource). |
| 3 | Verification failed (dangling reference / out-of-bounds frame / format leak). |
