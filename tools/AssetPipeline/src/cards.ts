import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import path from "node:path";
import type { CardMeta } from "./types";

export interface CardsSliceFactionConfig {
  faction: string; // e.g. "Faction1" — matches the Cards.<Faction> lookup group and the
  // factory/core/<faction-lowercase>.coffee filename.
  cardNames: string[]; // property names within Cards.<Faction>, e.g. "SilverguardSquire".
}

/**
 * Extracts card metadata for the configured slice straight from the original repo's own
 * authoritative source files, without executing the full SDK class hierarchy (which pulls in
 * Cocos2d/browser-only code that cannot run standalone under Node — see pipeline README).
 *
 * - Numeric ids come from `require`-ing app/sdk/cards/cardsLookup.coffee (pure data).
 * - Stats/description/resource ids come from a block-scoped text extraction over the matching
 *   `app/sdk/cards/factory/core/<faction>.coffee` file: each card is defined as a flat
 *   `if (identifier == Cards.<Faction>.<Name>) ... ` block up to the next such `if`, and the
 *   fields we need (`card.atk`, `card.maxHP`, `card.manaCost`, `card.rarityId`, `card.name`,
 *   `card.setDescription`, `card.setIsGeneral`, `RSX.<alias>` references, the constructed class)
 *   appear as simple, consistently-formatted statements within that block.
 */
// `coffeescript/register` patches the process-wide CJS `require.extensions['.coffee']` hook.
// It must be resolved from OUR OWN package (where it's an installed dependency), not from the
// cloned repo's directory tree (which has no node_modules/coffeescript) — but once registered,
// the hook applies globally, so a require() scoped to the target file can then load it.
const ownRequire = createRequire(import.meta.url);
let coffeeRegistered = false;
function ensureCoffeeRegistered(): void {
  if (!coffeeRegistered) {
    ownRequire("coffeescript/register");
    coffeeRegistered = true;
  }
}

export function loadCardsLookup(duelystRoot: string): Record<string, Record<string, number>> {
  ensureCoffeeRegistered();
  const cardsLookupPath = path.join(duelystRoot, "app/sdk/cards/cardsLookup.coffee");
  return createRequire(cardsLookupPath)(cardsLookupPath) as Record<string, Record<string, number>>;
}

export function loadFactionsLookup(duelystRoot: string): Record<string, number> {
  ensureCoffeeRegistered();
  const p = path.join(duelystRoot, "app/sdk/cards/factionsLookup.coffee");
  return createRequire(p)(p) as Record<string, number>;
}

export function loadRarityLookup(duelystRoot: string): Record<string, number> {
  ensureCoffeeRegistered();
  const p = path.join(duelystRoot, "app/sdk/cards/rarityLookup.coffee");
  return createRequire(p)(p) as Record<string, number>;
}

function friendlyFactionName(factionsLookup: Record<string, number>, factionKey: string): string {
  const id = factionsLookup[factionKey];
  const aliases = Object.entries(factionsLookup)
    .filter(([k, v]) => v === id && !/^Faction\d+$/.test(k) && k !== "Neutral" && k !== "Tutorial" && k !== "Boss")
    .map(([k]) => k);
  return aliases[0] ?? factionKey;
}

function rarityName(rarityLookup: Record<string, number>, rarityId: number | undefined): string {
  if (rarityId === undefined) return "";
  const entry = Object.entries(rarityLookup).find(([, v]) => v === rarityId);
  return entry ? entry[0] : "";
}

function loadEnCardsLocale(duelystRoot: string): Record<string, string> {
  const p = path.join(duelystRoot, "app/localization/locales/en/cards.json");
  return JSON.parse(readFileSync(p, "utf-8")) as Record<string, string>;
}

function resolveI18nKey(locale: Record<string, string>, fullKey: string): string {
  return locale[stripCardsNamespace(fullKey)] ?? "";
}

function splitFactoryIntoBlocks(source: string): Array<{ identifier: string; body: string }> {
  const markerRe = /if \(identifier == ([\w.]+)\)/g;
  const matches: { identifier: string; index: number }[] = [];
  let m: RegExpExecArray | null;
  while ((m = markerRe.exec(source)) !== null) {
    matches.push({ identifier: m[1]!, index: m.index });
  }
  const blocks: Array<{ identifier: string; body: string }> = [];
  for (let i = 0; i < matches.length; i++) {
    const current = matches[i]!;
    const start = current.index;
    const end = i + 1 < matches.length ? matches[i + 1]!.index : source.length;
    blocks.push({ identifier: current.identifier, body: source.slice(start, end) });
  }
  return blocks;
}

function extractCardType(body: string): CardMeta["cardType"] {
  const ctorMatch = /card\s*=\s*new\s+(\w+)\(/.exec(body);
  const ctor = ctorMatch?.[1] ?? "";
  if (/card\.setIsGeneral\(true\)/.test(body)) return "general";
  if (ctor === "Unit") return "unit";
  if (ctor === "Artifact") return "artifact";
  if (ctor.startsWith("Spell")) return "spell";
  return "unit";
}

function extractResourceIds(body: string): string[] {
  const ids = new Set<string>();
  const re = /RSX\.(\w+)/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(body)) !== null) ids.add(m[1]!);
  return [...ids].sort((a, b) => a.localeCompare(b));
}

function extractNumber(body: string, fieldRe: RegExp): number | undefined {
  const m = fieldRe.exec(body);
  return m ? Number(m[1]) : undefined;
}

export interface BuildCardsOptions {
  duelystRoot: string;
  slice: CardsSliceFactionConfig[];
}

export interface CardCatalogResult {
  cards: CardMeta[];
  /** Short i18n keys (namespace-stripped, e.g. "faction_1_unit_argeon_name") referenced by the slice. */
  i18nKeys: string[];
}

export function buildCardCatalog(opts: BuildCardsOptions): CardCatalogResult {
  const cardsLookup = loadCardsLookup(opts.duelystRoot);
  const factionsLookup = loadFactionsLookup(opts.duelystRoot);
  const rarityLookup = loadRarityLookup(opts.duelystRoot);
  const enCards = loadEnCardsLocale(opts.duelystRoot);

  const cards: CardMeta[] = [];
  const i18nKeys = new Set<string>();

  for (const factionCfg of opts.slice) {
    const factionGroup = cardsLookup[factionCfg.faction];
    if (!factionGroup) {
      throw new Error(`cardsLookup.coffee has no group "${factionCfg.faction}"`);
    }

    const factoryFileName = `${factionCfg.faction.toLowerCase()}.coffee`;
    const factoryPath = path.join(opts.duelystRoot, "app/sdk/cards/factory/core", factoryFileName);
    const source = readFileSync(factoryPath, "utf-8");
    const blocks = splitFactoryIntoBlocks(source);

    for (const cardName of factionCfg.cardNames) {
      const id = factionGroup[cardName];
      if (id === undefined) {
        throw new Error(`Cards.${factionCfg.faction}.${cardName} not found in cardsLookup.coffee`);
      }
      const fullIdentifier = `Cards.${factionCfg.faction}.${cardName}`;
      const block = blocks.find((b) => b.identifier === fullIdentifier);
      if (!block) {
        throw new Error(`No "if (identifier == ${fullIdentifier})" block found in ${factoryFileName}`);
      }
      const body = block.body;

      const nameKeyMatch = /card\.name\s*=\s*i18next\.t\("([^"]+)"\)/.exec(body);
      const descKeyMatch = /card\.setDescription\(i18next\.t\("([^"]+)"\)\)/.exec(body);
      const rarityIdMatch = /card\.rarityId\s*=\s*Rarity\.(\w+)/.exec(body);
      const rarityIdNum = rarityIdMatch ? rarityLookup[rarityIdMatch[1]!] : undefined;

      const cardType = extractCardType(body);
      const cost = extractNumber(body, /card\.manaCost\s*=\s*(-?\d+)/) ?? 0;
      const atk = extractNumber(body, /card\.atk\s*=\s*(-?\d+)/);
      const hp = extractNumber(body, /card\.maxHP\s*=\s*(-?\d+)/);

      if (nameKeyMatch) i18nKeys.add(stripCardsNamespace(nameKeyMatch[1]!));
      if (descKeyMatch) i18nKeys.add(stripCardsNamespace(descKeyMatch[1]!));

      cards.push({
        id,
        name: nameKeyMatch ? resolveI18nKey(enCards, nameKeyMatch[1]!) || nameKeyMatch[1]! : cardName,
        faction: friendlyFactionName(factionsLookup, factionCfg.faction),
        cost,
        cardType,
        ...(atk !== undefined ? { atk } : {}),
        ...(hp !== undefined ? { hp } : {}),
        rarity: rarityName(rarityLookup, rarityIdNum),
        race: "",
        set: "Core",
        resourceIds: extractResourceIds(body),
        description: descKeyMatch ? resolveI18nKey(enCards, descKeyMatch[1]!) : ""
      });
    }
  }

  cards.sort((a, b) => a.id - b.id);
  return { cards, i18nKeys: [...i18nKeys].sort((a, b) => a.localeCompare(b)) };
}

function stripCardsNamespace(fullKey: string): string {
  return fullKey.startsWith("cards.") ? fullKey.slice("cards.".length) : fullKey;
}
