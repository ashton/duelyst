# Duelyst F#

A native desktop reimplementation of [Duelyst](https://github.com/open-duelyst/duelyst) in F# / .NET 9,
reusing the original's CC0-licensed art, audio, cards, and champions on a new functional-core /
imperative-shell architecture. See [`docs/planning.md`](docs/planning.md) for the full architecture and
build plan, and [`specs/001-project-skeleton-asset-import/`](specs/001-project-skeleton-asset-import/) for
this milestone's spec, plan, and design docs.

## Quick start (consumer — clone and run)

The vertical-slice assets (2 generals + 20-40 cards) are committed via git-LFS, so the app runs without
importing anything.

**Prerequisites**: [.NET 9 SDK](https://dotnet.microsoft.com/download), `git`, [`git-lfs`](https://git-lfs.com/).

```bash
git lfs install
git clone <repo-url> && cd duelyst_fsharp
git lfs pull                      # fetch the committed slice PNG/audio
dotnet build Duelyst.sln
dotnet run --project src/Duelyst.Client
```

A window opens showing one imported unit sprite. Closing the window exits cleanly.

## Solution layout

```text
Duelyst.sln
src/
  Duelyst.Core/      # pure sim engine (no IO, no Raylib) — stub this milestone
  Duelyst.Content/   # card/set content definitions — stub this milestone
  Duelyst.AI/        # bot(s) over Duelyst.Core — stub this milestone
  Duelyst.Assets/    # manifest parsing (pure) + Raylib texture loading/drawing
  Duelyst.Client/    # Raylib-cs desktop shell (immediate-mode Model/View/Update)
tests/
  Duelyst.Assets.Tests/  # Expecto + FsCheck
tools/
  AssetPipeline/     # decoupled Node.js/TypeScript tool that regenerates assets/
assets/              # generated + committed vertical-slice (git-LFS)
external/duelyst/    # gitignored checkout of the original repo, used by the pipeline
```

## Maintainer — regenerating assets

Assets are produced by a standalone Node.js/TypeScript pipeline, decoupled from the .NET solution. See
[`tools/AssetPipeline/README.md`](tools/AssetPipeline/README.md) for usage.

```bash
cd tools/AssetPipeline
npm ci
npm run import
```

## Running tests

```bash
dotnet test Duelyst.sln
```

## Constitution

Development follows [`.specify/memory/constitution.md`](.specify/memory/constitution.md): domain-first
modeling, test-first (TDD), functional core / imperative shell, immediate-mode UI (TEA), and designed for
evolution.
