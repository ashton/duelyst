# Implementation Plan: Project Skeleton & Reproducible Asset Import

**Branch**: `001-project-skeleton-asset-import` | **Date**: 2026-08-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-project-skeleton-asset-import/spec.md`

## Summary

Stand up the greenfield F#/.NET 9 solution skeleton and a **decoupled, non-.NET asset pipeline** that
clones the original `open-duelyst` repo into a fixed conventional directory, copies the raw PNG/audio/fx,
and **translates the Cocos2d `.plist` sprite-sheet descriptors into a project-native `atlases.json`** (no
XML/plist ever enters the game). The pipeline also emits `resources.json` (alias→descriptor), `cards.json`
(card metadata), and i18n, then publishes a curated **vertical-slice subset (2 generals + 20–40 cards)**
into a **git-LFS-tracked** committed asset set so a fresh clone runs without re-running the pipeline.
Finally, a minimal `Duelyst.Assets` + `Duelyst.Client` reads `resources.json` + `atlases.json`, loads one
atlas PNG, and displays a single imported sprite in a Raylib window via an immediate-mode Model/View/Update
loop. Technical approach and decisions are detailed in [research.md](./research.md).

## Technical Context

**Language/Version**: Game — F# on **.NET 9**. Asset pipeline (decoupled) — **Node.js 20 LTS + TypeScript**
(not part of `Duelyst.sln`).

**Primary Dependencies**: Game — **Raylib-cs** (window/texture/draw). Pipeline — Node stdlib +
`typescript`; a plist XML parser (npm `plist` or `fast-xml-parser`); `git` CLI for cloning. The pipeline
`require`s the original `resources.js` and reuses the original repo's own card-export tooling where present.

**Storage**: Files on disk under `assets/` (`resources.json`, `atlases.json`, `cards.json`, `i18n/`, plus
copied `png/audio/fx`). Vertical-slice subset committed via **git-LFS**; the original source lives in
`external/duelyst/` (gitignored).

**Testing**: Game — **Expecto + FsCheck** (`tests/Duelyst.Assets.Tests`). Pipeline — **Vitest** (its native
ecosystem runner) for the plist→`atlases.json` translator. Cross-cutting — a verification step asserting
manifest references resolve and frames fall within PNG bounds.

**Target Platform**: Desktop via .NET 9 + Raylib-cs (Linux/Windows/macOS); primary dev on Linux.

**Project Type**: Desktop application (multi-project .NET solution) **plus** a standalone build/setup tool.

**Performance Goals**: Window shows an imported asset within **10 s** of launch (SC-004); animation renders
smoothly (~60 fps for one sprite, low bar); pipeline outputs are **byte-reproducible** across runs (SC-002).

**Constraints**: **No Cocos2d/plist/XML format may be committed as a project asset or read at runtime**
(FR-012); `Duelyst.Core` stays IO/Raylib-free (unused this milestone but not violated); offline; a fresh
clone with LFS pulled runs the app **without** the pipeline (SC-001); onboarding to on-screen asset
< **15 min** (SC-006).

**Scale/Scope**: Vertical slice — **2 generals + 20–40 cards** (target ~30), a handful of unit atlases,
~tens of MB committed. Full ~1.3 GB catalog remains regenerable, not committed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against `Duelyst F# Constitution` v1.0.0:

- **I. Domain-First Modeling** — PASS. Work starts by modeling the manifest/atlas schemas as types
  ([data-model.md](./data-model.md)) before any loader/renderer code. Frames use a typed `Rectangle` and a
  strongly-typed `AtlasKey`/alias, not loose primitives.
- **II. Test-First (NON-NEGOTIABLE)** — PASS, with a justified deviation recorded in Complexity Tracking.
  The plist→`atlases.json` translator and the F# manifest parser / alias→rect resolver are written
  test-first (Red→Green→Refactor). *Justified deviation:* the constitution's Technology & Architecture
  Constraints list "Testing stack: Expecto + FsCheck" without qualification; the decoupled, non-.NET
  `tools/AssetPipeline` instead uses **Vitest**, its ecosystem's native runner. TDD itself (Principle II)
  is fully honored — only the *runner* differs for the one tool outside `Duelyst.sln`. Recorded as a
  named, justified violation per the constitution's Governance clause; see Complexity Tracking below.
- **III. Functional Core / Imperative Shell** — PASS. Asset **parsing** (JSON→records) and **alias→source
  `Rectangle` resolution** are **pure** and unit-tested without Raylib; only `AtlasLoader`/`SpriteAnimator`
  draw calls touch Raylib. `Duelyst.Core` remains IO/Raylib-free (untouched this milestone).
- **IV. Immediate-Mode UI + TEA (No Stateful Components)** — PASS. The client is a minimal Model→View→Update
  loop (`Model = { Asset; ElapsedTime }`; `View` is a pure render; `Update` advances animation time). No
  retained/stateful widgets.
- **V. Designed for Evolution (Simplicity & YAGNI)** — PASS. The pipeline emits **data** the runtime consumes
  as data; only the vertical slice is built now; the schemas are the extension seam for later sets.

**Technology & Architecture Constraints** — Honored: .NET 9 / F# / Raylib-cs / Expecto+FsCheck for the game.
The pipeline being non-.NET is explicitly permitted (spec FR-012 constrains only the **asset format**, not the
tool's language), and is recorded as a justified boundary below.

**Result: PASS (no unjustified violations).** Re-checked after Phase 1 — still PASS (design keeps parsing pure,
UI immediate-mode, and no plist/XML in the runtime).

## Project Structure

### Documentation (this feature)

```text
specs/001-project-skeleton-asset-import/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (JSON schemas + tool/runtime contracts)
│   ├── resources.schema.json
│   ├── atlases.schema.json
│   ├── cards.schema.json
│   ├── pipeline-cli.md
│   └── assets-runtime-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
duelyst_fsharp/
├── Duelyst.sln
├── Directory.Build.props           # shared net9.0 settings
├── .gitattributes                  # git-LFS: *.png, *.ogg/*.wav, fx binaries
├── .gitignore                      # external/, bin/, obj/, non-slice generated assets
├── src/
│   ├── Duelyst.Core/               # created as stub (pure, no IO) — not exercised this milestone
│   ├── Duelyst.Content/            # created as stub
│   ├── Duelyst.AI/                 # created as stub
│   ├── Duelyst.Assets/
│   │   ├── AtlasManifest.fs        # PURE: types + parse atlases.json / resources.json
│   │   ├── Manifest.fs             # PURE: resolve RSX alias -> descriptor -> source Rectangle
│   │   ├── AtlasLoader.fs          # IO/Raylib: load PNG -> Texture2D
│   │   └── SpriteAnimator.fs       # frame timing (pure) + draw (Raylib)
│   └── Duelyst.Client/
│       └── Program.fs              # Raylib window; Model/View/Update; displays one asset
├── tools/
│   └── AssetPipeline/              # DECOUPLED Node.js + TypeScript tool (not in Duelyst.sln)
│       ├── package.json
│       ├── tsconfig.json
│       ├── src/
│       │   ├── index.ts            # orchestrate; report success/failure + exit codes
│       │   ├── clone.ts            # clone/pin open-duelyst -> external/duelyst (idempotent)
│       │   ├── copyAssets.ts       # copy png/audio/fx (NO plist)
│       │   ├── resources.ts        # require resources.js -> resources.json
│       │   ├── plistToAtlases.ts   # translate .plist (v2/v3, rotated/trimmed) -> atlases.json
│       │   ├── cards.ts            # emit cards.json
│       │   ├── i18n.ts             # emit i18n/*.json
│       │   ├── publishSlice.ts     # copy vertical slice into committed (LFS) set
│       │   └── verify.ts           # assert references resolve / frames in-bounds / no plist leaked
│       └── test/
│           └── plistToAtlases.test.ts   # Vitest unit tests (written first)
├── tests/
│   └── Duelyst.Assets.Tests/       # Expecto + FsCheck (written first)
│       ├── ManifestTests.fs        # parse + alias->rect resolution (pure)
│       ├── ResolveTests.fs         # resolve alias -> ResolvedSprite; rotated/unknown-alias handling
│       └── AtlasFrameTests.fs      # FsCheck: frame rects within bounds; rotated w/h swap
├── contracts/                      # Vendored JSON Schemas (resources/atlases/cards) shared by pipeline verify + F# tests
│   ├── resources.schema.json
│   ├── atlases.schema.json
│   └── cards.schema.json
├── assets/                         # GENERATED; vertical-slice subset committed via git-LFS
│   ├── resources.json
│   ├── atlases.json
│   ├── cards.json
│   ├── i18n/
│   └── units/ …                    # copied png/audio/fx (slice)
└── external/duelyst/               # gitignored; cloned by the pipeline
```

**Structure Decision**: A single .NET solution (`Duelyst.sln`) holds the five game projects from
`docs/planning.md`; only `Duelyst.Assets`, `Duelyst.Client`, and `tests/Duelyst.Assets.Tests` carry logic
this milestone (the rest are created as stubs so the skeleton builds). The **asset pipeline is a sibling tool
under `tools/AssetPipeline/`, deliberately outside the solution**, with its own Node/TypeScript toolchain — it
only writes files under `assets/`, keeping the game/runtime free of the original's stack and formats.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Second toolchain (Node.js/TypeScript) for `tools/AssetPipeline` — a deviation from the constitution's single .NET/Expecto stack | The authoritative source data is a 1.5 MB JS object literal (`resources.js`) and CoffeeScript card factories; executing them in Node reads them faithfully, and npm has mature plist parsers | Implementing the extractor in F# would require embedding a JS engine or hand-parsing 1.5 MB of JS + CoffeeScript and re-deriving plist semantics — far more code and risk. The tool is decoupled and touches no game code, so the blast radius is contained. |
| Testing Stack constraint ("Expecto + FsCheck") not applied to `tools/AssetPipeline` — it uses **Vitest** | Vitest is the native, zero-friction test runner for the Node/TypeScript pipeline; TDD (Principle II) is still followed, only the runner differs | Forcing Expecto onto non-.NET code isn't possible; shelling out to Node from an Expecto wrapper test would only add an integration layer around the same Vitest run — no real gain, more latency and failure surface |
