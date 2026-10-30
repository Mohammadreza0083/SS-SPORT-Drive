import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

const motion = readFileSync(path.join(__dirname, "motion.css"), "utf8");

describe("motion system (brief §10, design canvas)", () => {
  it.each([
    "ss-rise",
    "ss-fade-up",
    "ss-fade-in",
    "ss-glow",
    "ss-drift",
    "ss-sil-in",
    "ss-sweep",
    "ss-marquee",
    "ss-count",
    "ss-line-grow",
    "ss-cta-sweep",
    "ss-floaty",
    "ss-eq",
    "ss-pan",
    "ss-ring",
    "ss-pulse",
    "ss-slide-in",
    "ss-pop",
    "ss-draw",
    "ss-burst",
    "ss-shake",
    "ss-card-in",
    "ss-modal-in",
    "ss-shimmer",
    "ss-wm-move",
  ])("defines the @keyframes %s", (name) => {
    expect(motion).toMatch(new RegExp(`@keyframes ${name}\\s*\\{`));
  });

  it("runs the name shine every 4.5 s and the ticker in 26 s", () => {
    expect(motion).toMatch(/ss-sweep 4\.5s/);
    expect(motion).toMatch(/--ss-marquee-duration:\s*26s/);
  });

  it("counts up over 2.2 s", () => {
    expect(motion).toMatch(/ss-count 2\.2s/);
  });

  it("reveals sections with scroll-driven animations and falls back to a timed fade", () => {
    expect(motion).toContain("@supports (animation-timeline: view())");
    expect(motion).toContain("@supports not (animation-timeline: view())");
  });

  it("turns every animation and transition off for reduced motion", () => {
    const block = motion.slice(motion.indexOf("@media (prefers-reduced-motion: reduce)"));
    expect(block).toMatch(/animation:\s*none !important/);
    expect(block).toMatch(/transition:\s*none !important/);
  });

  it("gives returning viewers a 400 ms fade instead of the full intro", () => {
    expect(motion).toMatch(/\[data-ss-intro="short"\][^{]*\{[^}]*ss-fade-in \.4s/);
  });

  it("scopes its classes to the viewer shell so the Mini App is untouched", () => {
    const rules = motion.replace(/\/\*[\s\S]*?\*\//g, "").match(/^[^@\s}][^{]*\{/gm) ?? [];
    for (const rule of rules.filter((r) => !r.startsWith(":root") && !r.startsWith("from") && !r.startsWith("to"))) {
      expect(rule).toMatch(/\.viewer/);
    }
  });
});
