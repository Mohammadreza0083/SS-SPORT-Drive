import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

const tokens = readFileSync(path.join(__dirname, "tokens.css"), "utf8");

/**
 * Reads the value of one custom property declared in tokens.css.
 *
 * @param name - The property name without the leading dashes.
 * @returns The declared value, or undefined when it is missing.
 */
function token(name: string): string | undefined {
  return tokens.match(new RegExp(`--${name}:\\s*([^;]+);`))?.[1].trim();
}

describe("brand tokens (brief §10, design canvas)", () => {
  it.each([
    ["ss-red", "#CC0000"],
    ["ss-maroon", "#660000"],
    ["ss-red-light", "#FF4545"],
    ["ss-red-soft", "#FF6B6B"],
    ["ss-bg", "#07080B"],
    ["ss-panel", "#0F1117"],
    ["ss-input", "#12151C"],
    ["ss-border", "#1E222B"],
    ["ss-border-strong", "#2A2F3A"],
    ["ss-divider", "#1A1D24"],
    ["ss-text", "#F2F4F7"],
    ["ss-text-soft", "#B9C0CC"],
    ["ss-text-dim", "#C9CED8"],
    ["ss-muted", "#8A93A6"],
  ])("declares --%s as %s", (name, value) => {
    expect(token(name)?.toUpperCase()).toBe(value);
  });

  it("uses the canvas easing curves", () => {
    expect(token("ss-ease-out")).toBe("cubic-bezier(.2, .8, .2, 1)");
    expect(token("ss-ease-intro")).toBe("cubic-bezier(.2, .9, .2, 1)");
  });

  it("maps the three typefaces to the font variables set by next/font", () => {
    expect(token("ss-font-display")).toContain("var(--font-barlow-condensed)");
    expect(token("ss-font-sans")).toContain("var(--font-geist)");
    expect(token("ss-font-mono")).toContain("var(--font-geist-mono)");
  });

  it("keeps the canvas page width and gutters", () => {
    expect(token("ss-page-max")).toBe("1280px");
    expect(token("ss-gutter")).toBe("40px");
  });
});
