# Feature Specification: Project Skeleton & Reproducible Asset Import

**Feature Branch**: `001-project-skeleton-asset-import`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: "the first milestone should create the basic structure, we should be able to import the game's assets from the original implementation into our project. We should have a defined and reproduceble process to import those assets, we should be able to run the desktop app and see one of the imported assets in a new window"

## Clarifications

### Session 2026-08-16

- Q: Where does the asset pipeline read the original project's source code from? → A: A fixed
  conventional repo-relative directory (e.g. `external/duelyst/`); the contributor places the original
  source there before running the pipeline.
- Q: Does the contributor place the source manually, or does tooling obtain it? → A (refined
  2026-08-16, later in the same working session): The pipeline itself clones the pinned original source
  into the fixed directory (idempotent — reuses an existing checkout); the contributor does not place it
  manually. Supersedes the "contributor places" phrasing above.
- Q: What did "must not reuse the original's technologies" mean? → A: The project MUST NOT carry over
  the original engine's asset formats — its Cocos2d sprite-sheet descriptors expressed as plist/XML;
  the pipeline translates those into the project's own normalized formats. This is a format constraint,
  not a constraint on the pipeline's implementation language (which may use best-fit tooling).
- Q: Are imported assets committed to version control, and at what scope? → A: Yes — commit a curated
  vertical-slice subset: 2 generals plus 20–40 cards.
- Q: How are the committed binary assets stored? → A: Via git-LFS (PNG/audio/fx tracked through
  git-LFS).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reproducibly import original game assets (Priority: P1)

A contributor runs a single, documented process that itself obtains (clones) the game's original source
into a fixed conventional directory (e.g. `external/duelyst/`), then pulls the game's art, audio, and
card data from that source into this project, producing a machine-readable manifest that describes
every imported asset. Running the same process again against the same source yields the same result,
so anyone on the team can regenerate the assets on demand rather than depending on a one-off manual
copy.

**Why this priority**: Every later capability (rendering, rules content, UI) depends on having the
real assets and a trustworthy index of them. A *reproducible* process is the explicit ask and the
foundation of the whole rewrite — without it, the project can't legitimately claim "same art, same
cards." This slice alone delivers a concrete, reusable artifact: the imported asset set plus manifest.

**Independent Test**: From a clean state (no pre-existing source checkout), run the documented import
process end-to-end and confirm it clones the source and produces the asset set and a manifest; run it a
second time and confirm the outputs are identical; confirm every asset the manifest references
resolves to a real file.

**Acceptance Scenarios**:

1. **Given** a clean checkout and the documented prerequisites are met, **When** a contributor runs
   the import process, **Then** the project contains the imported assets and a manifest describing
   each one, and the process reports what was imported.
2. **Given** the import process has already been run once, **When** it is run again from a clean
   state, **Then** the resulting manifest and asset inventory are identical to the first run.
3. **Given** a generated manifest, **When** its entries are checked against the imported files,
   **Then** every referenced asset (image, audio, sprite/animation data) resolves to an existing file.
4. **Given** the import process runs, **When** it finishes, **Then** it clearly reports success or
   failure, including which assets were imported and any that could not be.

---

### User Story 2 - See an imported asset in the desktop app (Priority: P2)

A contributor launches the desktop application and a window opens showing at least one of the
imported assets rendered on screen, proving the imported assets are consumable end-to-end by the app.

**Why this priority**: This is the visible proof that the milestone works — the pipeline output is
not just files on disk but something the application can actually load and display. It is the
headline demonstration of the milestone, but it depends on Story 1 having produced assets, so it is
P2.

**Independent Test**: With assets already imported, launch the desktop application and observe that a
window opens and displays a recognizable imported asset.

**Acceptance Scenarios**:

1. **Given** assets have been imported, **When** the contributor launches the desktop application,
   **Then** a window opens and displays at least one imported asset.
2. **Given** the application window is open with an asset displayed, **When** the contributor closes
   the window, **Then** the application exits cleanly.

---

### User Story 3 - Establish the project skeleton (Priority: P3)

The project has a basic, buildable structure — a clear place for the core, content, asset handling,
the desktop client, the import tooling, and tests — so that all subsequent work has a home and a new
contributor can build and run the project from a clean checkout.

**Why this priority**: The skeleton is the foundation the other two stories sit on, but on its own it
is the least user-visible deliverable. It is essential and enabling, so it is included but ranked
after the capabilities that deliver observable value.

**Independent Test**: From a clean checkout, follow the documented setup steps and confirm the
project builds and the desktop application launches successfully.

**Acceptance Scenarios**:

1. **Given** a clean checkout, **When** a contributor follows the documented setup and build steps,
   **Then** the project builds successfully.
2. **Given** a successful build, **When** the contributor launches the desktop application, **Then**
   it starts and opens a window without error.

---

### Edge Cases

- **Source clone fails or is invalid**: If the pipeline cannot clone the pinned original source into the
  fixed conventional directory (network failure, unreachable remote, pinned commit not found), or an
  existing directory at that path does not contain the expected original-source layout (foreign or
  corrupted content), the import process MUST fail with a clear, actionable message naming the expected
  directory and pinned commit, rather than producing a partial or silently-empty result. A directory that
  is simply absent is not an error — the pipeline clones into it.
- **Expected source asset missing or renamed**: If an asset the process expects is absent or renamed
  in the source, manifest generation MUST flag it and verification MUST fail loudly, not emit a
  dangling reference.
- **Imported asset present but corrupt/unreadable**: If the application cannot load a referenced
  asset, it MUST report the load failure clearly instead of crashing silently or showing a blank
  window with no explanation.
- **Re-running import over existing output**: Re-running the process MUST regenerate cleanly (idempotent
  outputs) without leaving stale or duplicated assets.
- **Insufficient disk space for the large asset set**: The process MUST fail with a clear message
  rather than leaving a half-written, inconsistent asset set.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The project MUST provide a defined, documented process to import the game's assets (art,
  audio, card data) from the original source into this project. The process MUST obtain the original
  source itself — cloning a pinned version into a fixed conventional repo-relative directory (e.g.
  `external/duelyst/`), reusing it if already present — rather than requiring a contributor to place it
  there manually first.
- **FR-002**: The import process MUST be reproducible — running it from a clean state produces the
  same asset inventory and the same manifest every time.
- **FR-003**: The import process MUST produce a machine-readable manifest that describes each
  imported asset and how to locate it (including images, audio, and sprite/animation frame data).
- **FR-004**: The project MUST provide a way to verify that every asset referenced by the manifest
  resolves to an existing imported file, with zero dangling references.
- **FR-005**: The import process MUST document its prerequisites and steps — including that it clones
  the pinned original source into the fixed conventional directory itself — so a contributor can run it
  unaided.
- **FR-006**: The import process MUST report its outcome clearly, including which assets were imported
  and any that failed, and MUST fail loudly (non-silently) on error.
- **FR-007**: A curated vertical-slice subset of imported assets (2 generals plus 20–40 cards) MUST
  be committed to version control via git-LFS, so a fresh clone can run the app without running the
  pipeline; the full set MUST remain regenerable and expandable via the import process and is not
  committed.
- **FR-008**: The project MUST establish a basic structure that separates the concerns of the future
  system (core, content, asset handling, desktop client, import tooling, tests) and builds from a
  clean checkout.
- **FR-009**: The project MUST provide a runnable desktop application that opens a window.
- **FR-010**: The desktop application MUST load at least one imported asset and display it visually in
  the window.
- **FR-011**: The desktop application MUST exit cleanly when its window is closed.
- **FR-012**: The import process MUST translate the original engine's asset/metadata formats (its
  Cocos2d sprite-sheet frame descriptors expressed as plist/XML) into the project's own normalized
  formats; no original-engine-specific format may be committed as a project asset or read by the
  desktop application at runtime.

### Key Entities *(include if feature involves data)*

- **Original asset source**: A copy of the upstream game's source code placed in a fixed conventional
  repo-relative directory (e.g. `external/duelyst/`), containing the original art, audio, card data,
  and sprite-sheet frame mappings; the authoritative input to the import process.
- **Imported asset**: A single asset (image, audio clip, or sprite/animation data) brought into this
  project by the import process; regenerable and not hand-authored. The vertical-slice subset is
  committed via git-LFS; the remainder is generated on demand.
- **Asset manifest**: A machine-readable index that maps each logical asset to its descriptor and
  location, and is the contract the desktop application uses to find assets to display.
- **Import process**: The defined, reproducible procedure that obtains the original source and
  produces the imported assets plus the manifest, reporting success/failure.
- **Desktop application window**: The runnable surface that loads the manifest, resolves an imported
  asset, and displays it to the contributor.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh clone already contains the committed vertical-slice assets (2 generals plus
  20–40 cards) and can display an asset without running the import process.
- **SC-002**: Running the import process twice against the same source directory produces identical
  outputs (same manifest and same asset inventory), confirming reproducibility.
- **SC-003**: 100% of assets referenced by the generated manifest resolve to existing files (zero
  dangling references).
- **SC-004**: Launching the desktop application opens a window that displays at least one imported
  asset within 10 seconds of startup.
- **SC-005**: The project builds and the desktop application launches successfully from a clean
  checkout by following the documented steps.
- **SC-006**: A new contributor can reach a running window showing an imported asset in under 15
  minutes, using only the committed assets (obtaining the original source is not required).

## Assumptions

- The original implementation is the open-source, CC0-licensed upstream game repository, and its
  assets are free to extract and reuse.
- The pipeline clones the pinned original source into a fixed conventional repo-relative directory (e.g.
  `external/duelyst/`) itself, reusing an existing checkout if present; obtaining the source is not a
  manual contributor step.
- No original-engine-specific asset format (Cocos2d plist/XML frame descriptors) is committed or read
  at runtime; the pipeline normalizes such metadata into the project's own formats. The pipeline's
  implementation language is unconstrained and may use whatever tooling best reads the original files.
- The curated vertical-slice subset is committed to version control via git-LFS so the app runs from a
  fresh clone; the full ~1.3 GB set is not committed and is regenerated via the pipeline.
- The milestone requires displaying only a single imported asset to prove the pipeline end-to-end;
  full board/game rendering, animation state machines beyond a static display, and multiple scenes
  are out of scope here.
- The rules engine, gameplay, card behavior, AI, networking, and any UI beyond a single window
  displaying an asset are out of scope for this milestone.
- The target is a desktop application; the specific supported operating-system matrix is deferred to
  planning.
- The current committed scope is the vertical slice (2 generals + 20–40 cards); the pipeline is
  nonetheless designed to import and commit larger subsets (up to the complete catalog) in later
  milestones.
