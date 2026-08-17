import { fileURLToPath } from "node:url";
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import path from "node:path";
import { ensureCloned, SourceAcquisitionError } from "./clone";
import { collectCopyPaths, copyAssets } from "./copyAssets";
import { loadRawResources, buildResourcesManifest, sortByKey } from "./resources";
import { translatePlist } from "./plistToAtlases";
import { buildCardCatalog, type CardsSliceFactionConfig } from "./cards";
import { buildI18n } from "./i18n";
import { publishSlice } from "./publishSlice";
import { listFilesRecursive, readPngSize, runVerify } from "./verify";
import type { AtlasTable, ResourcesManifest } from "./types";

const pkgRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = path.resolve(pkgRoot, "..", "..");

interface PipelineConfig {
  sourceRepo: string;
  sourceCommit: string;
  sourceDir: string;
  outDir: string;
  contractsDir: string;
  slice: {
    factions: CardsSliceFactionConfig[];
    extraResourceAliases: string[];
  };
}

function loadConfig(): PipelineConfig {
  const raw = JSON.parse(readFileSync(path.join(pkgRoot, "pipeline.config.json"), "utf-8")) as PipelineConfig;
  return raw;
}

const EXIT = { OK: 0, SOURCE: 1, EXTRACT: 2, VERIFY: 3 } as const;

function fail(code: (typeof EXIT)[keyof typeof EXIT], message: string): never {
  console.error(`\n[asset-pipeline] FAILED: ${message}`);
  process.exit(code);
}

function summary(stage: string, detail: string) {
  console.log(`[asset-pipeline] ${stage}: ${detail}`);
}

async function main() {
  const verifyOnly = process.argv.includes("--verify-only");
  const config = loadConfig();

  const duelystRoot = path.resolve(pkgRoot, config.sourceDir);
  const assetsRoot = path.resolve(pkgRoot, config.outDir);
  const contractsDir = path.resolve(pkgRoot, config.contractsDir);

  if (!verifyOnly) {
    // --- clone ---
    try {
      ensureCloned({ sourceRepo: config.sourceRepo, sourceCommit: config.sourceCommit, targetDir: duelystRoot });
    } catch (err) {
      if (err instanceof SourceAcquisitionError) fail(EXIT.SOURCE, err.message);
      throw err;
    }
    summary("clone", `${config.sourceRepo}@${config.sourceCommit} ready at ${path.relative(repoRoot, duelystRoot)}`);

    // --- cards ---
    let cardsResult;
    try {
      cardsResult = buildCardCatalog({ duelystRoot, slice: config.slice.factions });
    } catch (err) {
      fail(EXIT.EXTRACT, `card extraction failed: ${(err as Error).message}`);
    }
    const { cards, i18nKeys } = cardsResult!;
    summary("cards", `${cards.length} cards extracted`);

    // --- resources ---
    const allAliases = new Set<string>(config.slice.extraResourceAliases);
    for (const card of cards) for (const alias of card.resourceIds) allAliases.add(alias);

    let resources: ResourcesManifest;
    try {
      const raw = loadRawResources(path.join(duelystRoot, "app/data/resources.js"));
      resources = buildResourcesManifest(raw, allAliases);
    } catch (err) {
      fail(EXIT.EXTRACT, `resources extraction failed: ${(err as Error).message}`);
    }
    summary("resources", `${Object.keys(resources!).length} aliases resolved`);

    // --- copy png/audio/fx (no plist) ---
    let copiedPaths: string[];
    try {
      const toCopy = collectCopyPaths(resources!);
      copiedPaths = copyAssets(duelystRoot, assetsRoot, toCopy);
    } catch (err) {
      fail(EXIT.EXTRACT, `asset copy failed: ${(err as Error).message}`);
    }
    summary("copyAssets", `${copiedPaths!.length} files copied`);

    // --- atlases (plist -> atlases.json) ---
    let atlases: AtlasTable = {};
    try {
      const raw = loadRawResources(path.join(duelystRoot, "app/data/resources.js"));
      const seen = new Map<string, string>(); // img -> plist
      for (const [alias, descriptor] of Object.entries(resources!)) {
        if (descriptor.kind !== "sprite" && descriptor.kind !== "animation") continue;
        const rawEntry = raw[alias];
        if (rawEntry?.plist) seen.set(descriptor.img, rawEntry.plist);
      }
      for (const [img, plistRel] of seen) {
        const xml = readFileSync(path.join(duelystRoot, "app", plistRel), "utf-8");
        atlases[img] = translatePlist(xml, img);
      }
      atlases = sortByKey(atlases);
    } catch (err) {
      fail(EXIT.EXTRACT, `atlas translation failed: ${(err as Error).message}`);
    }
    summary("atlases", `${Object.keys(atlases).length} atlases translated`);

    // --- i18n ---
    let i18n: Record<string, Record<string, string>>;
    try {
      i18n = buildI18n(duelystRoot, i18nKeys);
    } catch (err) {
      fail(EXIT.EXTRACT, `i18n extraction failed: ${(err as Error).message}`);
    }
    summary("i18n", `${Object.keys(i18n!).length} locales emitted`);

    // --- write outputs ---
    mkdirSync(assetsRoot, { recursive: true });
    mkdirSync(path.join(assetsRoot, "i18n"), { recursive: true });
    writeFileSync(path.join(assetsRoot, "resources.json"), JSON.stringify(resources!, null, 2) + "\n");
    writeFileSync(path.join(assetsRoot, "atlases.json"), JSON.stringify(atlases, null, 2) + "\n");
    writeFileSync(path.join(assetsRoot, "cards.json"), JSON.stringify(cards, null, 2) + "\n");
    for (const [locale, entries] of Object.entries(i18n!)) {
      writeFileSync(path.join(assetsRoot, "i18n", `${locale}.json`), JSON.stringify(entries, null, 2) + "\n");
    }
    summary("write", `resources.json, atlases.json, cards.json, i18n/*.json written under ${path.relative(repoRoot, assetsRoot)}`);

    // --- publish (stage for git-LFS commit) ---
    try {
      const staged = publishSlice(repoRoot, path.relative(repoRoot, assetsRoot));
      summary("publishSlice", `${staged.length} paths staged for commit`);
    } catch (err) {
      summary("publishSlice", `skipped (${(err as Error).message})`);
    }
  }

  // --- verify ---
  const resources = JSON.parse(readFileSync(path.join(assetsRoot, "resources.json"), "utf-8")) as ResourcesManifest;
  const atlases = JSON.parse(readFileSync(path.join(assetsRoot, "atlases.json"), "utf-8")) as AtlasTable;
  const cards = JSON.parse(readFileSync(path.join(assetsRoot, "cards.json"), "utf-8"));

  // `pngSizes` doubles as an existence cache for checkReferentialIntegrity: PNGs get their real
  // width/height (for in-bounds frame checks); audio files just need a truthy presence entry.
  const pngSizes = new Map<string, { width: number; height: number }>();
  for (const img of new Set(Object.values(resources).flatMap((d) => ("img" in d ? [d.img] : [])))) {
    pngSizes.set(img, readPngSize(path.join(assetsRoot, img)));
  }
  for (const audioPath of new Set(Object.values(resources).flatMap((d) => ("audio" in d ? [d.audio] : [])))) {
    if (existsSync(path.join(assetsRoot, audioPath))) pngSizes.set(audioPath, { width: 0, height: 0 });
  }

  const presentFiles = listFilesRecursive(assetsRoot);
  const issues = runVerify({ assetsRoot, resources, atlases, cards, pngSizes, presentFiles });

  if (issues.length > 0) {
    console.error(`\n[asset-pipeline] verify: ${issues.length} issue(s) found:`);
    for (const issue of issues) console.error(`  - [${issue.code}] ${issue.message}`);
    process.exit(EXIT.VERIFY);
  }

  summary("verify", `clean (0 issues; ${presentFiles.length} files scanned)`);
  void contractsDir; // reserved for future JSON-Schema validation against the vendored contracts
  console.log(verifyOnly ? "\n[asset-pipeline] verify complete." : "\n[asset-pipeline] import complete.");
  process.exit(EXIT.OK);
}

main().catch((err) => fail(EXIT.EXTRACT, (err as Error).stack ?? String(err)));
