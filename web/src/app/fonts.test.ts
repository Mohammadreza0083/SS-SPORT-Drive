import { describe, expect, it, vi } from "vitest";

vi.mock("next/font/google", () => {
  /** Records the options a font loader was called with. */
  const loader = (family: string) => (options: Record<string, unknown>) => ({
    family,
    options,
    variable: `${family}-variable`,
    className: `${family}-class`,
    style: { fontFamily: family },
  });
  return { Barlow_Condensed: loader("Barlow Condensed"), Geist: loader("Geist"), Geist_Mono: loader("Geist Mono") };
});

const { barlowCondensed, geist, geistMono, fontVariables } = await import("./fonts");

/** The options a mocked loader received. */
type Loaded = { options: Record<string, unknown> };

describe("fonts (decisions §9)", () => {
  it("loads Barlow Condensed 700/800 for player names, with Latin Extended for names like Yılmaz", () => {
    const { options } = barlowCondensed as unknown as Loaded;
    expect(options.weight).toEqual(["700", "800"]);
    expect(options.subsets).toEqual(["latin", "latin-ext"]);
    expect(options.variable).toBe("--font-barlow-condensed");
  });

  it("loads Geist for the interface", () => {
    const { options } = geist as unknown as Loaded;
    expect(options.variable).toBe("--font-geist");
    expect(options.weight).toEqual(["400", "500", "600", "700"]);
  });

  it("loads Geist Mono for numbers, timestamps and labels", () => {
    const { options } = geistMono as unknown as Loaded;
    expect(options.variable).toBe("--font-geist-mono");
    expect(options.weight).toEqual(["400", "500", "700"]);
  });

  it("swaps fonts in so text is never invisible", () => {
    for (const font of [barlowCondensed, geist, geistMono]) {
      expect((font as unknown as Loaded).options.display).toBe("swap");
    }
  });

  it("exposes all three variables as one class list for the <html> element", () => {
    expect(fontVariables).toBe("Barlow Condensed-variable Geist-variable Geist Mono-variable");
  });
});
