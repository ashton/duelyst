import { readFileSync, existsSync } from "node:fs";
import path from "node:path";
import { sortByKey } from "./resources";

const LOCALES = ["en", "de", "zh-tw"] as const;

/**
 * Extracts the slice-relevant i18next `cards.json` entries per locale into `assets/i18n/<locale>.json`.
 * Not consumed by the client this milestone (names/descriptions already resolve into cards.json for
 * English), but produced because the pipeline contract requires it and later milestones need it.
 */
export function buildI18n(duelystRoot: string, keys: string[]): Record<string, Record<string, string>> {
  const out: Record<string, Record<string, string>> = {};
  for (const locale of LOCALES) {
    const localePath = path.join(duelystRoot, "app/localization/locales", locale, "cards.json");
    if (!existsSync(localePath)) continue;
    const localeCards = JSON.parse(readFileSync(localePath, "utf-8")) as Record<string, string>;
    const picked: Record<string, string> = {};
    for (const key of keys) {
      if (localeCards[key] !== undefined) picked[key] = localeCards[key];
    }
    out[locale] = sortByKey(picked);
  }
  return sortByKey(out);
}
