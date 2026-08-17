# Quickstart & Validation: Project Skeleton & Reproducible Asset Import

Proves the milestone end-to-end. Details live in [plan.md](./plan.md), [data-model.md](./data-model.md), and
[contracts/](./contracts/). This is a run/validation guide, not implementation.

## Prerequisites

- **.NET 9 SDK**, **Node.js 20 LTS**, **git** + **git-lfs**.
- Network access for the first pipeline run (clones `open-duelyst` at the pinned commit).

## Two audiences, two paths

### A. Consumer — "clone and run" (no pipeline; SC-001, SC-005, SC-006)

The vertical-slice assets are committed via git-LFS, so the app runs without importing anything.

```bash
git lfs install
git clone <repo> && cd duelyst_fsharp
git lfs pull                      # fetch committed slice PNG/audio
dotnet build Duelyst.sln
dotnet run --project src/Duelyst.Client
```

**Expected**: a window opens within 10 s showing one imported unit sprite (idle animation playing). Closing
the window exits cleanly. Target: under 15 minutes from clone to on-screen asset.

### B. Maintainer — regenerate assets (the pipeline; SC-002, FR-002/004/006/012)

```bash
cd tools/AssetPipeline
npm ci
npm run import                    # clone -> copy -> resources -> atlases -> cards -> i18n -> publish -> verify
```

**Expected**: exit code 0 and a per-stage summary. `assets/` now contains `resources.json`, `atlases.json`,
`cards.json`, `i18n/`, and copied `png/audio/fx` — and **no `.plist`/`.xml`**.

## Validation scenarios (acceptance-mapped)

| # | Action | Expected | Spec |
|---|--------|----------|------|
| 1 | `npm run import` twice from clean | `resources.json`/`atlases.json`/`cards.json` byte-identical between runs | SC-002, FR-002 |
| 2 | `find assets -name '*.plist' -o -name '*.xml'` | no results | FR-012 |
| 3 | `npm run verify` | 0 dangling refs; every frame within PNG bounds | SC-003, FR-004 |
| 4 | Replace `external/duelyst/` with a foreign/corrupted directory (not simply absent) then `npm run import` | non-zero exit naming the expected dir + pinned commit. (An *absent* `external/duelyst/` is not an error — the pipeline clones into it.) | FR-006, edge case |
| 5 | `dotnet test` | Expecto suite green (manifest parse, alias→rect, rotated w/h swap, `frameAt` timing) | Constitution II |
| 6 | `cd tools/AssetPipeline && npm test` | Vitest green (plist v2/v3, rotated/trimmed translation) | Constitution II |
| 7 | `dotnet run --project src/Duelyst.Client` | window shows the imported sprite within 10 s; clean close | SC-004, FR-009/010/011 |
| 8 | `dotnet build Duelyst.sln` from clean checkout | solution builds (all stub projects compile) | FR-008, SC-005 |
| 9 | Path A timed: `git clone` + `git lfs pull`, then `dotnet run --project src/Duelyst.Client` **without** running the pipeline, clock start-to-on-screen | window shows the committed-slice asset; total elapsed time under 15 minutes | SC-001, SC-006 |

## Test-first order (Constitution II — write these before the code)

1. `tools/AssetPipeline/test/plistToAtlases.test.ts` — fixtures for plist v2 & v3, a rotated frame, a trimmed
   frame → assert normalized `atlases.json` shape (Red first).
2. `tests/Duelyst.Assets.Tests/ManifestTests.fs` — `parseResources`/`parseAtlases`/`resolve` incl. unknown
   alias → `Error`.
3. `tests/Duelyst.Assets.Tests/AtlasFrameTests.fs` — FsCheck: frames within bounds; rotated ⇒ w/h swapped;
   `frameAt` index math.
4. Only then implement translator, parser, resolver, loader, and the client loop.

## Definition of done

- All 9 validation scenarios pass.
- Constitution Check in [plan.md](./plan.md) still PASS after implementation.
- Committed slice = 2 generals + 20–40 cards; full set regenerable and uncommitted.
