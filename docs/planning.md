# Duelyst → F# Rewrite: Architecture & Build Plan

## Context

The goal is to re-implement the (now open-source, CC0-licensed) **Duelyst** tactics/card game as a
**native desktop game in F#**, reusing the original **art, audio, cards, and champions** but building a
**brand-new codebase with a different architecture**. The existing `open-duelyst/duelyst` repo is a
Node.js/CoffeeScript stack: a Backbone + **Cocos2d-JS** client and several server services, with a
shared game engine in `app/sdk` (**718 modifier classes, 257 spell classes, 65 action types**, cards
authored imperatively in giant per-faction/per-set CoffeeScript files). That class explosion is the
thing we want to *design away from*; the deterministic action/event simulation at its heart is the part
worth preserving in spirit.

The new build target working dir `/home/john/dev/duelyst_fsharp` is **empty** — this is greenfield.

**Decisions locked in with the user:**
- **Renderer/shell:** Raylib-cs + F# (we build the sprite-animation / UI / particle layers on top of Raylib).
- **Networking:** Offline-first (vs-AI + hot-seat) on a **deterministic, serializable core** designed so
  server-authoritative online PvP can be added later without a rewrite.
- **Card authoring:** **Hybrid** — most cards are pure data referencing a composable effect/keyword DSL;
  the exotic ~5% drop to a typed F# escape-hatch function.
- **First milestone:** a **vertical slice** (2 generals, ~30–40 cards, full board rules, real assets, vs-AI).

Intended outcome: a clean **functional-core / imperative-shell** F# codebase where adding a new card is
usually a data entry and adding a set is a new content module — with the rules engine fully unit-testable
and network-ready.

## Key facts learned from the original repo (drive the design)

- **Simulation is deterministic and action-driven.** Spells/modifiers produce **Actions**; everything
  funnels through `gameSession.executeAction`. Modifiers subscribe to typed lifecycle events
  (`onActivate`, `onAction`, `onBeforeAction`/`onAfterAction`, `onEndTurn`, `onApplyToCard`, `onExpire`, …
  ~28 hooks) and react by emitting **more actions**, with an event-buffering step. There is already a
  `replay/` system and AI `agents/`, confirming full determinism. → maps to an F# reducer + action queue.
- **Actions carry a modify pipeline.** e.g. `DamageAction` has `damageChange`, `damageMultiplier`,
  `finalDamageChange` applied during a *modify-for-execution* phase where modifiers transform the action
  before it resolves (damage buffs, damage reduction, etc.). → must be modeled explicitly.
- **Board & rules constants** (`app/common/config.js`): board **9 cols × 5 rows**; `MAX_MANA=9`,
  `STARTING_MANA=2`, `MAX_HAND_SIZE=6`, `STARTING_HAND_SIZE=5`, mulligan replace count `2`.
- **Assets** live in `app/resources/**` grouped by category (`units`, `generals`, `fx`, `sfx`, `tiles`,
  `icons`, `modifiers`, `card_backgrounds`, `particles`, `music`, …). Units are **Cocos2d texture atlases**:
  a `.png` sprite sheet + a `.plist` (XML) mapping frame-names → rects.
- **`app/data/resources.js`** (~1.5 MB) is the manifest: `RSX = { alias: descriptor }` where a descriptor is
  one of: `{name, img}` (texture), `{name, frame, img, plist}` (sprite in a sheet),
  `{name, framePrefix, frameDelay, img, plist}` (**animation**: frames named `framePrefix+index`, played at
  `frameDelay` s), `{name, audio}` (sound), plus particles/fonts/cubemaps. Card factory code references e.g.
  `RSX.f1GeneralIdle.name`, `RSX.sfx_unit_deploy.audio`.
- **Card metadata** (id/name/cost/faction/rarity/race/atk/hp/description + resource ids) is in
  `app/sdk/cards/**` (`cardsLookup*`, `factionFactory`, per-set factory files) + i18next localization JSON.
  Card **numeric ids** (`cardsLookupComplete`) should be reused verbatim so decks/assets line up.
- License is **CC0** → assets and data are free to extract and reuse.

## Recommended architecture: Functional Core / Imperative Shell

### Solution layout (.NET 9, F#)
```
duelyst_fsharp/
  Duelyst.sln
  src/
    Duelyst.Core/      # PURE sim: GameState, Action DU, resolution pipeline, effect DSL, triggers, RNG. No IO, no Raylib.
    Duelyst.Content/   # Card/set definitions (DSL data + escape-hatch fns) + CardCatalog; seeded by generated cards.json
    Duelyst.AI/        # Bot(s) over the core (legalActions + heuristic/shallow search)
    Duelyst.Assets/    # Runtime asset loading: reads normalized atlases.json + PNG -> Texture2D, RSX manifest, SpriteAnimator (Raylib). No XML/plist at runtime.
    Duelyst.Client/    # Raylib-cs shell: board/hand/FX rendering, input, audio, scene stack, event-stream animation
  tools/
    AssetPipeline/     # Build-time extraction from the original repo -> assets/ + resources.json + cards.json + i18n
  tests/
    Duelyst.Core.Tests/ # Rules/DSL/determinism tests (Expecto + FsCheck); oracle spot-checks vs original SDK
  assets/              # GENERATED: copied png/audio/fx + resources.json + atlases.json + cards.json.
                       #   Vertical-slice subset committed via git-LFS; full set regenerated by the pipeline (see note).
  external/duelyst/    # (gitignored) checkout of open-duelyst/duelyst, used only by the pipeline
```
Tooling defaults (recommended, non-blocking): .NET 9; `Raylib-cs` NuGet; **Expecto** + **FsCheck** for tests.
**Hybrid asset storage:** commit the **curated vertical-slice subset** (2 generals + ~30–40 cards) via **git-LFS**
so a fresh clone runs the app out-of-the-box; do **not** commit the full ~1.3 GB set — regenerate it (or any larger
subset) reproducibly via the pipeline. The pipeline is a build/setup tool, never part of running the game.

### Duelyst.Core — the deterministic engine (the crown jewel)
- **Immutable state**: `GameState` = board (9×5), two `PlayerState` (mana/manaCap, hand, deck, graveyard,
  generalId), `activePlayer`, `turnNumber`, seeded `Rng`, and an append-only event/action log. Strongly-typed
  ids (`EntityId`, `PlayerId`). `Entity` = id, cardId, owner, position, atk, curHP/maxHP, `Modifier list`,
  `exhausted`/`hasMoved` flags. `Position = {X;Y}`.
- **`Action` DU** — the atomic unit of change; consolidates the 65 originals: `PlayCard`, `MoveUnit`,
  `Attack`, `Damage`, `Heal`, `Summon`, `Kill`, `ApplyModifier`, `RemoveModifier`, `DrawCard`, `Mulligan`,
  `StartTurn`, `EndTurn`, `Refresh`, … Tag each as player-initiated (validated) vs system-derived.
- **Resolution pipeline** (models the original's modify-for-execution + event-buffering):
  1. `validate : GameState -> Action -> Result<unit, InvalidReason>` (player intents only).
  2. `modifyForExecution` — fold relevant modifiers' transforms over the action (e.g. +damage, reduction, redirect).
  3. `apply` — pure reducer → `GameState * Event list` (what actually happened).
  4. `triggers` — match `Event`s against subscriptions; enqueue **follow-up actions** that recurse through
     the same pipeline until the queue drains.
  Public entry point:
  `step : GameState -> Action -> Result<GameState * Event list, InvalidReason>`
  (internally an action queue). The full ordered **`Event list` is what the client animates.**
- **`legalActions : GameState -> Action list`** — powers both UI affordances and the AI.
- **Determinism**: an explicit seeded PRNG threaded through `GameState` (no ambient `Random`). A match ≡
  `initialSeed + player Action list` → gives replays now and network-readiness later (send actions or
  server-validated event lists; no core rewrite).

### Effect DSL — the "add cards/sets easily" answer (hybrid model)
- `Selector` (target/query DSL): `Self | TargetedUnit | EnemyGeneral | AllyGeneral | NearbyEnemies |
  AllAllies of RaceFilter | InRadius of int * Filter | …` — replaces `spellFilterType` + radius + filters.
- `Effect` (what an ability does): `DealDamage of Amount * Selector | Heal | Summon of CardId * Placement |
  Draw of int | ApplyModifier of ModifierDef * Selector | Sequence of Effect list |
  Conditional of Condition * Effect * Effect | Custom of (EffectCtx -> Action list)`  ← **F# escape hatch**.
- `Trigger` (when it fires): `OnSummon` (Opening Gambit), `OnDeath` (Dying Wish), `OnDamaged`, `OnAttack`,
  `OnTurnStart/End`, `OnOtherSummon`, `Aura of …` — a small typed set consolidating the ~28 original hooks.
- `ModifierDef` (data for passives/keywords): stat deltas, granted keywords (Provoke, Flying, Rush, Zeal,
  Ranged, Frenzy, Blast, …), aura (radius + filter), triggered `(Trigger*Effect)`, duration/stacking. Keywords
  are **shared predefined values**, not one class each.
- `CardDef` record: `id` (reuse original numeric id), name, faction, cost, cardType, atk, hp, races, rarity,
  set, `keywords`, `triggers: (Trigger*Effect) list`, `deploy: Effect option`, targeting requirement, asset ids.
- **Interpreter**: compiles `Effect`+`Selector` against `GameState` → concrete `Action list` (fed back through
  `step`). New card = a `CardDef` value (mostly data); new set = a new module returning `CardDef list`.

### Duelyst.Content
- `Sets/Core/Faction1.fs` etc. each return `CardDef list`; a `CardCatalog` merges them by id.
- **Stats/names/costs/asset-ids are seeded from generated `cards.json`** (so we don't retype ~700 cards);
  only **behavior** (triggers/effects/keywords) is authored by hand in the DSL. This keeps "same cards" honest
  and cheap.

### Duelyst.Assets + Duelyst.Client (Raylib-cs shell)
- **Assets runtime**: read `resources.json` to resolve each `RSX` alias to `{img, frame/framePrefix, frameDelay,
  audio}`; load the atlas PNG (`img`) → `Texture2D`; get its frame-name→rect table from the pipeline-generated
  **`atlases.json`** (keyed by atlas, already normalized from the original Cocos2d plists — **no XML/plist parsing
  at runtime**, with rotated/trimmed frame metadata preserved); `SpriteAnimator` advances frames at `frameDelay`
  and draws sub-rects for unit states (breathing/idle/run/attack/damage/death/cast).
- **Client is an animation player over the core's Event stream**: input → build one player `Action` →
  `Core.step` → get `Event list` → play it out visually (walk, attack lunge, damage numbers, death) → settle to
  the new `GameState`. Clean split: simulation is instant & pure; presentation is timed & animated.
- Renders 9×5 board (tiles), entities, hand (card_backgrounds + portraits), mana/HP UI, targeting overlays, FX
  (particles). Scene stack: menu → deck select → match → result. AI turn: `AI.chooseActions` → `step` → animate.
  Hot-seat: same loop, human both sides.

### AssetPipeline (tools) — reproducible "same images, same cards"
This is a **standalone build/setup tool, decoupled from the game's F# stack** — it only emits data files under
`assets/`, so it is **not required to be written in F#/.NET**. Use the best language per step: Node/JS is the
pragmatic choice for reading `resources.js` and the CoffeeScript card factories (run them in their native runtime
and dump clean JSON); the plist→JSON translation and file copying can use whatever has the best libraries (e.g. a
Node or Python plist parser). Steps:
1. Checkout `open-duelyst/duelyst` into `external/duelyst/` (pin a known commit for reproducibility).
2. Copy `app/resources/**` → `assets/` — **PNGs, audio, fx only; the `.plist` files are NOT copied to `assets/`.**
3. `require` `app/data/resources.js` and emit clean **`assets/resources.json`** (alias → descriptor).
4. **Translate every Cocos2d `.plist` (XML) → normalized `assets/atlases.json`**: a flat per-atlas frame table
   (`{name, x, y, w, h, rotated, offsetX, offsetY, srcW, srcH}`), handling plist format v2/v3 and rotated/trimmed
   frames once, at build time. This is the step that keeps XML out of the runtime.
5. Emit **`assets/cards.json`** (id, name, faction, cost, type, atk, hp, rarity, race, set, resource ids, desc)
   from `app/sdk/cards/**` — reuse the repo's own package/export tooling in `scripts/` if it produces card data,
   otherwise parse the factory files.
6. Extract i18next localization (names/descriptions) → `assets/i18n/*.json`.
7. **Publish the curated vertical-slice subset** (the assets its `cards.json`/`resources.json` reference) into the
   git-LFS-tracked committed set, so a fresh clone runs without re-running the pipeline.

## Build order (milestones)
- **M0 — Persist this plan.** As the very first implementation step, write this plan to **`doc/planning.md`**
  in the repo so it lives alongside the code and guides the work.
- **M0 — Skeleton & pipeline. DELIVERED** (`specs/001-project-skeleton-asset-import/`). `Duelyst.sln` builds
  five projects (`Duelyst.Core/Content/AI` as IO/Raylib-free stubs, `Duelyst.Assets`, `Duelyst.Client`) plus
  `tests/Duelyst.Assets.Tests` (Expecto + FsCheck, 17 tests green). `tools/AssetPipeline/` (Node/TS, decoupled
  from the solution) clones `open-duelyst/duelyst` at a pinned commit, copies png/audio/fx, translates Cocos2d
  `.plist` v2/v3 → `assets/atlases.json` (no XML ever reaches `assets/`), and emits `assets/resources.json` +
  `assets/cards.json` + `assets/i18n/*.json`; 15 Vitest tests green, `npm run verify` clean, reproducible
  byte-identical across runs. Committed vertical slice: **2 generals (Lyonar/Songhai) + 32 core-set cards**.
  `Duelyst.Assets` (`AtlasManifest.fs`/`Manifest.fs` pure, `AtlasLoader.fs`/`SpriteAnimator.fs` Raylib edge)
  resolves and animates one imported sprite (`f1AzuriteLionIdle`) via a TEA loop in `Duelyst.Client/Program.fs`.
  **Open item**: on-screen rendering (SC-004) was verified at the parse/resolve level only — this dev sandbox
  has no display server, so a real windowed run is still needed on a machine with one before fully closing out
  T037/SC-004.
- **M1 — Core rules (headless).** `GameState`, `Action` pipeline, turn/mana ramp, summon-near-friendly, move (2),
  attack + counterattack, exhaustion, general-death win; `legalActions`; test suite; playable via a text harness.
- **M2 — Effect DSL + first keywords/triggers.** Provoke, Rush/Zeal/Ranged, DealDamage/Heal/Buff spells,
  Opening Gambit, Dying Wish, an aura, a summon effect; ~30–40 `CardDef`s in Content spanning every DSL feature.
- **M3 — Client vertical slice.** Board + hand rendering, input→action→animated event playback, targeting UI,
  mana/HP; hot-seat playable to a win with real assets.
- **M4 — AI.** Heuristic bot (over `legalActions`); vs-AI playable end-to-end.
- **M5 — Scale-out (post-slice).** Bulk-port remaining sets via the DSL, more FX, deck builder; then optionally
  add networking (server runs `Core.step`, broadcasts validated event lists — enabled by the deterministic core).

## Critical files to create (patterns, not exhaustive)
- `Duelyst.sln`, `src/Duelyst.Core/{GameState.fs, Actions.fs, Pipeline.fs, Effects.fs, Selectors.fs, Triggers.fs, Modifiers.fs, Rng.fs, Rules.fs}`
- `src/Duelyst.Content/{Keywords.fs, CardCatalog.fs, Sets/Core/Faction1.fs, Sets/Core/Faction2.fs, …}`
- `src/Duelyst.AI/Bot.fs`
- `src/Duelyst.Assets/{AtlasManifest.fs, AtlasLoader.fs, Manifest.fs, SpriteAnimator.fs}` (reads `atlases.json` + `resources.json`; no plist parser here)
- `src/Duelyst.Client/{Program.fs, Scenes/*.fs, Render/*.fs, Input.fs, EventPlayer.fs}`
- `tools/AssetPipeline/` (extraction scripts, non-F#; includes the plist→`atlases.json` translator)
- `tests/Duelyst.Core.Tests/{PipelineTests.fs, DslTests.fs, DeterminismTests.fs, OracleTests.fs}`

## Verification
- **Core:** `dotnet test` — rules invariants, DSL interpreter correctness, **determinism** (same seed + actions ⇒
  identical event list), FsCheck property tests (mana never negative, HP floors at 0, board never double-occupied),
  and **oracle spot-checks** replaying tricky interactions against the original SDK where feasible.
- **Pipeline:** run AssetPipeline; assert `assets/resources.json`, `assets/atlases.json`, and `assets/cards.json`
  are non-empty, every referenced `img`/`audio` path exists, and every atlas frame resolves within its PNG bounds
  (and no `.plist`/XML leaked into `assets/`); smoke-load N atlases without error.
- **Client (the real end-to-end check):** `dotnet run --project src/Duelyst.Client`, then **play a full hot-seat
  game and a vs-AI game of the vertical slice to a win** — verify mana ramp, summoning rules, movement, attack +
  counterattack, Provoke enforcement, an Opening Gambit and a Dying Wish resolving, targeting UI, and unit
  animations (idle/attack/death) rendering from the extracted assets.

## Main risks / notes
- **Raylib means building the sprite-animation, UI, and particle layers ourselves** (as chosen). Mitigation: keep
  `Duelyst.Core` 100% engine-agnostic so that investment is isolated to the shell and never blocks rules work — and
  so the engine could be swapped later without touching the game.
- ~1.3 GB of assets → **hybrid storage**: commit only the curated vertical-slice subset via git-LFS (fresh clone
  just works), regenerate the full set via the pipeline; never commit the full 1.3 GB.
- The 700+ card behaviors are real work, but the hybrid DSL turns most into data; the vertical slice proves the DSL
  is expressive enough before we scale.
