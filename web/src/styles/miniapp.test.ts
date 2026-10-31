import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

const css = readFileSync(path.join(__dirname, "miniapp.css"), "utf8");

describe("Mini App styles (T-9, canvas artboards Mini*)", () => {
  it.each([
    ["--ma-bg", "--tg-theme-bg-color", "#17212b"],
    ["--ma-text", "--tg-theme-text-color", "#f5f5f5"],
    ["--ma-hint", "--tg-theme-hint-color", "#708499"],
    ["--ma-section", "--tg-theme-secondary-bg-color", "#232e3c"],
    ["--ma-link", "--tg-theme-link-color", "#6ab3f3"],
  ])("takes %s from Telegram's %s, falling back to the design's %s", (token, telegram, fallback) => {
    expect(css).toMatch(new RegExp(`${token}:\\s*var\\(${telegram},\\s*${fallback}\\)`, "i"));
  });

  it("keeps SS-SPORT red for actions, whatever the Telegram theme", () => {
    expect(css).toMatch(/--ma-accent:\s*#CC0000/i);
  });

  it("gives every button and field a 44 px touch target", () => {
    expect(css).toMatch(/\.ma button[^{]*\{[^}]*min-height:\s*44px/);
    expect(css).toMatch(/\.ma input[^{]*\{[^}]*min-height:\s*46px/);
  });

  it("turns motion off for reduced motion", () => {
    expect(css.slice(css.indexOf("@media (prefers-reduced-motion: reduce)"))).toMatch(/animation:\s*none !important/);
  });

  it("keeps generated text out of accessible names (badges, arrows)", () => {
    for (const [declaration] of css.matchAll(/content:\s*"[^"]+"[^;]*;/g)) {
      expect(declaration).toMatch(/\/\s*""\s*;$/);
    }
  });

  it("scopes every rule to the Mini App root, so the viewer site is untouched", () => {
    const rules = css.replace(/\/\*[\s\S]*?\*\//g, "").match(/^[^@\s}][^{]*\{/gm) ?? [];
    for (const rule of rules.filter((r) => !/^(from|to|\d)/.test(r))) {
      expect(rule).toMatch(/\.ma\b/);
    }
  });
});
