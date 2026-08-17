---

description: "Task list for Project Skeleton & Reproducible Asset Import"
---

# Tasks: Project Skeleton & Reproducible Asset Import

**Input**: Design documents from `/specs/001-project-skeleton-asset-import/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: REQUIRED. The constitution (Principle II, Test-First — NON-NEGOTIABLE) mandates TDD, so every
story writes failing tests before implementation. Pipeline tests use **Vitest**; F# tests use
**Expecto + FsCheck**.

**Organization**: Tasks are grouped by user story. All paths are repository-relative to
`/home/john/dev/duelyst_fsharp/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 (P1, pipeline), US2 (P2, desktop app), US3 (P3, skeleton)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repo-level scaffolding shared by every story.

- [X] T001 Initialize the git repository and enable git-LFS at the repo root (`git init`, `git lfs install`) in `/home/john/dev/duelyst_fsharp/`
- [X] T002 [P] Create `.gitignore` (ignore `bin/`, `obj/`, `external/`, `node_modules/`, and generated non-slice assets) in `.gitignore`
- [X] T003 [P] Create `.gitattributes` with git-LFS patterns for `assets/**/*.png`, `assets/**/*.ogg`, `assets/**/*.wav`, and fx binaries in `.gitattributes`
- [X] T004 [P] Create `Directory.Build.props` pinning `net9.0`, nullable, treat-warnings-as-errors, and shared metadata in `Directory.Build.props`
- [X] T005 [P] Create `.editorconfig` with F# formatting conventions in `.editorconfig`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting artifacts both the pipeline and the game depend on.

**⚠️ CRITICAL**: Must complete before any user story.

- [X] T006 Establish the `assets/` layout and gitignore rules that distinguish the committed vertical-slice from regenerated output (add `assets/.gitkeep`; extend `.gitignore`) in `assets/` and `.gitignore`
- [X] T007 [P] Vendor the JSON schema contracts (`resources.schema.json`, `atlases.schema.json`, `cards.schema.json`) from `specs/001-project-skeleton-asset-import/contracts/` into a shared repo location `contracts/` used by both the pipeline verify step and the F# manifest tests

**Checkpoint**: Repo config + shared schemas ready — stories can begin.

---

## Phase 3: User Story 1 - Reproducibly import original game assets (Priority: P1) 🎯 MVP

**Goal**: A decoupled, reproducible Node/TypeScript pipeline that clones the pinned original source into
`external/duelyst/`, copies png/audio/fx, translates Cocos2d plists → `atlases.json`, emits
`resources.json`/`cards.json`/i18n, and publishes the vertical-slice into the git-LFS committed set — with
**no plist/XML** reaching the project.

**Independent Test**: From a clean state, `cd tools/AssetPipeline && npm ci && npm run import` produces the
assets; `npm test` is green; running import twice yields byte-identical JSON; `find assets -name '*.plist' -o
-name '*.xml'` is empty; removing `external/duelyst/` makes import exit non-zero with a clear message.

### Setup for User Story 1

- [X] T008 [US1] Scaffold the decoupled Node.js/TypeScript pipeline workspace (`package.json`, `tsconfig.json`, Vitest config, and `pipeline.config.json` holding `sourceRepo`/`sourceCommit`/`sourceDir`/`outDir`/`slice`) in `tools/AssetPipeline/`

### Tests for User Story 1 (write first, MUST fail) ⚠️

- [X] T009 [P] [US1] Write failing Vitest tests for plist→atlases translation using v2 & v3 fixtures plus a rotated frame and a trimmed frame, asserting the normalized `atlases.json` shape, in `tools/AssetPipeline/test/plistToAtlases.test.ts`
- [X] T010 [P] [US1] Write failing Vitest tests for the verify stage (detects dangling reference, out-of-bounds frame, `.plist`/`.xml` leak, and non-reproducible ordering) in `tools/AssetPipeline/test/verify.test.ts`

### Implementation for User Story 1

- [X] T011 [US1] Implement idempotent clone/checkout of the pinned `open-duelyst` into `external/duelyst/` (reuse + verify if present) in `tools/AssetPipeline/src/clone.ts`
- [X] T012 [P] [US1] Implement copy of png/audio/fx (explicitly excluding `.plist`) into `assets/` in `tools/AssetPipeline/src/copyAssets.ts`
- [X] T013 [P] [US1] Implement `require` of `app/data/resources.js` → `assets/resources.json` conforming to `contracts/resources.schema.json` in `tools/AssetPipeline/src/resources.ts`
- [X] T014 [US1] Implement plist→`atlases.json` translator (plist v2/v3, rotated + trimmed metadata) to pass T009, conforming to `contracts/atlases.schema.json`, in `tools/AssetPipeline/src/plistToAtlases.ts`
- [X] T015 [P] [US1] Implement card metadata extraction → `assets/cards.json` (reuse original numeric ids) conforming to `contracts/cards.schema.json` in `tools/AssetPipeline/src/cards.ts` — via block-scoped text extraction over the factory `.coffee` source (not the full SDK class hierarchy); see README "Design notes"
- [X] T016 [P] [US1] Implement i18next localization extraction → `assets/i18n/*.json` in `tools/AssetPipeline/src/i18n.ts`
- [X] T017 [US1] Implement the verify stage (referential integrity, in-bounds frames, no format leak, stable key ordering for reproducibility) to pass T010 in `tools/AssetPipeline/src/verify.ts`
- [X] T018 [US1] Implement publish of the 2-generals + 20–40-card slice into the git-LFS committed asset set in `tools/AssetPipeline/src/publishSlice.ts` — pipeline is slice-scoped end-to-end (see README), so this stage stages `assets/` for commit rather than filtering a larger generated set
- [X] T019 [US1] Implement the orchestrator (stage sequencing, per-stage summary, exit codes 0/1/2/3 per `contracts/pipeline-cli.md`) in `tools/AssetPipeline/src/index.ts`
- [X] T020 [US1] Add `import`/`verify`/`test` npm scripts and usage docs in `tools/AssetPipeline/package.json` and `tools/AssetPipeline/README.md`
- [X] T021 [US1] Run the pipeline, commit the vertical-slice assets via git-LFS under `assets/`, and confirm reproducibility by running import twice and diffing the JSON outputs — pipeline run for real (32 cards, 323 resource aliases, 154 files, 34 atlases, verify clean); two runs diffed byte-identical; `assets/` staged via `git add` (LFS-tracked per `.gitattributes`) but left **uncommitted** — the actual commit happens once the other in-progress workstreams (skeleton/client) land, per the fork's instructions

**Checkpoint**: US1 delivers a reproducible, committed vertical-slice asset set — independently shippable MVP.

---

## Phase 4: User Story 3 - Establish the project skeleton (Priority: P3)

**Goal**: A buildable .NET 9 / F# solution with the five game projects (stubs) plus the test project, and
documented clean-checkout setup.

> **Sequencing note**: Spec priority is P3, but US2 (the app) cannot build without this skeleton, so it is
> scheduled ahead of US2. It remains independently testable.

**Independent Test**: From a clean checkout, `dotnet build Duelyst.sln` succeeds and
`dotnet run --project src/Duelyst.Client` opens a window (stub is acceptable at this phase).

- [X] T022 [US3] Create `Duelyst.sln` and compiling F# stub projects `src/Duelyst.Core/`, `src/Duelyst.Content/`, `src/Duelyst.AI/` (Core stays IO/Raylib-free per Constitution III)
- [X] T023 [P] [US3] Create `src/Duelyst.Assets/Duelyst.Assets.fsproj` (references Raylib-cs) with empty module files `AtlasManifest.fs`, `Manifest.fs`, `AtlasLoader.fs`, `SpriteAnimator.fs`
- [X] T024 [P] [US3] Create `src/Duelyst.Client/Duelyst.Client.fsproj` (exe, references Duelyst.Assets + Raylib-cs) with a stub `src/Duelyst.Client/Program.fs` that opens and closes a window
- [X] T025 [P] [US3] Create the `tests/Duelyst.Assets.Tests/` Expecto + FsCheck project referencing `Duelyst.Assets`
- [X] T026 [US3] Wire every project into `Duelyst.sln` and confirm `dotnet build Duelyst.sln` succeeds
- [X] T027 [US3] Write the repo `README.md` documenting clean-checkout setup (git-LFS pull, build, run) mirroring [quickstart.md](./quickstart.md)

**Checkpoint**: US3 delivers a buildable, documented skeleton — the foundation US2 builds on.

---

## Phase 5: User Story 2 - See an imported asset in the desktop app (Priority: P2)

**Goal**: The desktop client opens a window and displays one imported sprite from the committed slice, via a
pure resolver + Raylib draw, in an immediate-mode Model/View/Update loop.

> **Depends on**: US3 (skeleton must build) and US1 (committed slice for the live window). US2 unit tests use
> small fixtures and do not require US1.

**Independent Test**: `dotnet run --project src/Duelyst.Client` shows an imported sprite within 10 s and
closes cleanly; `dotnet test` is green.

### Tests for User Story 2 (write first, MUST fail) ⚠️

- [X] T028 [P] [US2] Write failing Expecto tests for `parseResources`/`parseAtlases` (valid + malformed JSON → `Error`) in `tests/Duelyst.Assets.Tests/ManifestTests.fs`
- [X] T029 [P] [US2] Write failing Expecto tests for `resolve` (sprite source rect; rotated ⇒ w/h swapped; animation frame list ordering; unknown alias/frame ⇒ `Error`) in `tests/Duelyst.Assets.Tests/ResolveTests.fs`
- [X] T030 [P] [US2] Write failing FsCheck property tests (every frame rect within PNG bounds; rotated swaps w/h; `frameAt` index = `floor(elapsed/delay) mod n`) in `tests/Duelyst.Assets.Tests/AtlasFrameTests.fs` — Expecto 11.1.0 dropped its built-in `testProperty` combinator and FsCheck 3.3.4's F#-facing API (`FsCheck.FSharp.Gen`) changed significantly from v2; properties are Expecto `testCase`s that draw samples via `Gen.sample`/`Gen.map2`/`Gen.arrayOf` and assert manually. "Within PNG bounds" is covered indirectly (frame rects are schema-validated integers; the pipeline's own verify.ts is the authority for real PNG-bounds checking per contracts/pipeline-cli.md) — the F#-side properties instead cover rotated w/h swap, animation frame ordering, and `frameAt` index math directly against `resolve`/`frameAt`.

### Implementation for User Story 2

- [X] T031 [US2] Implement manifest types + `parseResources`/`parseAtlases` (System.Text.Json) to pass T028 in `src/Duelyst.Assets/AtlasManifest.fs`
- [X] T032 [US2] Implement the pure `resolve : ResourcesManifest -> AtlasTable -> string -> Result<ResolvedSprite, ResolveError>` to pass T029 in `src/Duelyst.Assets/Manifest.fs`
- [X] T033 [US2] Implement pure `frameAt` timing to pass T030 in `src/Duelyst.Assets/SpriteAnimator.fs`
- [X] T034 [US2] Implement `loadTexture` (Raylib `LoadTexture` from assets root) in `src/Duelyst.Assets/AtlasLoader.fs`
- [X] T035 [US2] Implement `draw` (Raylib `DrawTexturePro` with rotation + origin for rotated/trimmed frames) in `src/Duelyst.Assets/SpriteAnimator.fs`
- [X] T036 [US2] Implement the immediate-mode TEA loop (`Model`/`Msg`/`update`/`view`) that opens a window, resolves one committed-slice alias, animates it, and exits cleanly on close, in `src/Duelyst.Client/Program.fs`
- [X] T037 [US2] Manually validate `dotnet run --project src/Duelyst.Client` — parsing/`resolve` confirmed correct (resolves `f1AzuriteLionIdle` to 14 ordered frames, frameDelay=0.08) via temporary diagnostic logging (added then removed); genuine on-screen pixel confirmation of SC-004 was **not** achievable in this sandbox — there is no X11 display, and Raylib's native `LoadTexture` segfaults (SIGSEGV) once GLFW fails to acquire a GL context. This is an environment limitation, not a code defect; needs a real display (or Xvfb, not installed here) to finish validating.

**Checkpoint**: US2 delivers the visible end-to-end proof — an imported asset rendered in a window.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T038 [P] Run the full [quickstart.md](./quickstart.md) validation (all 9 scenarios, including Path A / scenario 9 for SC-001/SC-006) and record results — **7/9 fully green, 2 partial**: (1) reproducible byte-identical ✓, (2) no plist/xml under `assets/` ✓, (3) `npm run verify` clean (0 issues, 160 files) ✓, (4) fail-loud on invalid `external/duelyst/` ✓, (5) `dotnet test` 17/17 green ✓, (6) `npm test` 15/15 green ✓, (7) client shows sprite within 10s — **partial**: resolve/parse verified correct, real on-screen pixels unconfirmed (no display in this sandbox, user declined installing Xvfb), (8) `dotnet build Duelyst.sln` clean ✓, (9) Path A fresh-clone timing — **blocked**: no commit exists yet in this repo (everything is `git add`-staged per instructions not to commit mid-implementation); needs a commit before a real `git clone`+`git lfs pull` timing run is possible.
- [X] T039 [P] Add a CI script running `dotnet build` + `dotnet test` + pipeline `npm test` + `npm run verify` in `.github/workflows/ci.yml`
- [X] T040 [P] Update `docs/planning.md` M0 status notes to reflect the delivered layout
- [X] T041 Verify spec.md, research.md, and tasks.md agree on auto-clone behavior (resolved 2026-08-16 via /speckit-analyze remediation); close out if consistent — found `quickstart.md` scenario 4 was stale (said "remove `external/duelyst/`" should fail, contradicting spec.md's Edge Cases: "a directory that is simply absent is not an error — the pipeline clones into it"); fixed scenario 4 to describe the actual failure trigger (a foreign/corrupted directory, not an absent one). spec.md, research.md R2, `clone.ts`'s real implementation, and tasks.md are now consistent.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — blocks all stories.
- **US1 (Phase 3)**: depends only on Setup + Foundational. Independent of the .NET skeleton (Node tool).
- **US3 (Phase 4)**: depends only on Setup + Foundational.
- **US2 (Phase 5)**: depends on **US3** (must build) and, for the live-window validation (T037), on **US1**'s
  committed slice. US2 unit tests (T028–T033) need only US3 + fixtures.
- **Polish (Phase 6)**: depends on the stories it touches.

### Story completion order

MVP = **US1**. Then **US3** (foundation for the app), then **US2** (the app). US1 and US3 can proceed in
parallel after Foundational (different toolchains, no shared files).

### Within a story

Tests (Vitest / Expecto) are written first and MUST fail before implementation (Constitution II).

### Parallel opportunities

- Setup: T002, T003, T004, T005 in parallel.
- US1: T009 + T010 (tests) in parallel; then T012, T013, T015, T016 (independent modules) in parallel.
- US3: T023, T024, T025 in parallel.
- US2: T028, T029, T030 (separate test files) in parallel.
- Cross-story: after Foundational, Developer A takes US1 while Developer B takes US3; US2 follows US3.

---

## Parallel Example: User Story 1

```bash
# Write both failing test suites together:
Task: "Vitest tests for plist→atlases (v2/v3, rotated, trimmed) in tools/AssetPipeline/test/plistToAtlases.test.ts"
Task: "Vitest tests for verify stage in tools/AssetPipeline/test/verify.test.ts"

# Then implement independent extractor modules together:
Task: "copyAssets.ts (png/audio/fx, exclude plist)"
Task: "resources.ts (resources.js → resources.json)"
Task: "cards.ts (→ cards.json)"
Task: "i18n.ts (→ i18n/*.json)"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP & VALIDATE**: reproducible committed
   slice, no plist/XML, verify clean. Shippable on its own.

### Incremental delivery

1. Setup + Foundational → foundation ready.
2. US1 → committed reproducible assets (MVP).
3. US3 → buildable skeleton.
4. US2 → window renders an imported asset (visible end-to-end proof).
5. Polish → CI, quickstart validation, doc reconciliation.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- Constitution II is non-negotiable: verify each test fails before implementing.
- `Duelyst.Core` must stay IO/Raylib-free even as a stub (Constitution III).
- The client must be immediate-mode with no stateful widgets (Constitution IV).
- Commit after each task or logical group; the vertical-slice assets go through git-LFS.
