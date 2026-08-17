# Asset Pipeline

A standalone Node.js 20 + TypeScript tool, decoupled from `Duelyst.sln`. It clones the pinned
`open-duelyst/duelyst` source, translates its Cocos2d `.plist` sprite-sheet descriptors into a
project-native `atlases.json`, and emits `resources.json` / `cards.json` / `i18n/*.json` — then
publishes the result (the curated vertical slice) under `assets/` for git-LFS commit. No
`.plist`/XML file is ever copied into `assets/` or read at runtime (FR-012).

## Usage

```bash
cd tools/AssetPipeline
npm ci
npm run import      # full pipeline: clone -> cards -> resources -> copyAssets -> atlases -> i18n -> write -> publish -> verify
npm run verify       # verify stage only, against the assets/ already on disk (no writes)
npm test              # Vitest unit tests (plist translation, verify checks)
```

Configuration lives in `pipeline.config.json`: `sourceRepo`/`sourceCommit` pin the exact original
commit for reproducibility; `sourceDir`/`outDir`/`contractsDir` are repo-relative paths; `slice`
lists the generals + card names (by their `Cards.<Faction>.<Name>` lookup key) that make up the
committed vertical slice.

## Design notes

- **Slice-scoped by design**: every stage (cards, resources, copyAssets, atlases) is driven by
  `pipeline.config.json`'s `slice`, so `assets/` only ever contains the curated 2-generals +
  ~30-cards slice — never the full ~1.3 GB catalog. This keeps the committed set small and avoids
  needing separate gitignore/allowlist bookkeeping to hide an uncommitted full regeneration under
  the same directory. Widening the slice later means adding names to `pipeline.config.json`.
- **Card metadata extraction**: rather than executing the original SDK's full class hierarchy
  (`SDK.CardFactory.getAllCards(...)`, which transitively pulls in Cocos2d/browser-only code and
  cannot run standalone under plain Node), `src/cards.ts` `require`s the pure-data
  `cardsLookup.coffee` for numeric ids (via the `coffeescript` package's require hook) and then
  does a block-scoped text extraction over the matching `app/sdk/cards/factory/core/<faction>.coffee`
  file: each card is a flat `if (identifier == Cards.<Faction>.<Name>) ... ` block up to the next
  such `if`, and the fields we need (`card.atk`, `card.maxHP`, `card.manaCost`, `card.rarityId`,
  `card.name`, `card.setDescription`, `card.setIsGeneral`, every `RSX.<alias>` reference) are
  simple, consistently-formatted statements within that block. This reads the real authoritative
  source (not hand-typed data) while staying tractable and dependency-free.
- **`resources.js`**: `require`d directly (R1) — it's a plain CommonJS module exporting the `RSX`
  object literal, needing only `underscore`. A `node_modules/underscore` symlink is placed at
  `external/duelyst/node_modules/underscore` (pointing at this package's own installed copy) so
  the clone's own `require('underscore')` resolves without a full `npm install` in `external/`.
- **Idempotent clone** (`src/clone.ts`): if `external/duelyst/` is absent, it's cloned (shallow
  fetch pinned to `sourceCommit`); if present, its `HEAD` is verified against the pinned commit —
  a mismatch fails loudly rather than silently re-cloning or silently using the wrong source.
- **Reproducibility**: all JSON output keys are sorted; atlas frames are sorted by name. Two
  `npm run import` runs from the same source commit produce byte-identical `resources.json`,
  `atlases.json`, and `cards.json`.
- **`verify`** (`src/verify.ts`) checks: every `img`/`audio`/`frame` reference in `resources.json`
  resolves; every atlas frame lies within its PNG's bounds; no `.plist`/`.xml` file exists under
  `assets/`; and every manifest's key order is stable (sorted) — a proxy for reproducibility.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success. |
| 1 | Source acquisition failed (clone/checkout/commit mismatch). |
| 2 | Extraction/translation error (bad plist, missing card/resource, unreadable file). |
| 3 | Verification failed (dangling reference / out-of-bounds frame / format leak / unstable ordering). |
