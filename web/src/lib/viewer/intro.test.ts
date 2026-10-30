import { describe, expect, it } from "vitest";
import { decideIntro, introScript } from "./intro";

/** An in-memory stand-in for localStorage. */
function memoryStorage(): Pick<Storage, "getItem" | "setItem"> {
  const items = new Map<string, string>();
  return {
    getItem: (key) => items.get(key) ?? null,
    setItem: (key, value) => void items.set(key, value),
  };
}

describe("decideIntro", () => {
  it("plays the full intro on the first visit to a page", () => {
    expect(decideIntro(memoryStorage(), "/r/abc")).toBe("full");
  });

  it("gives a returning viewer the short fade on the same page", () => {
    const storage = memoryStorage();
    decideIntro(storage, "/r/abc");

    expect(decideIntro(storage, "/r/abc")).toBe("short");
  });

  it("remembers each page separately", () => {
    const storage = memoryStorage();
    decideIntro(storage, "/r/abc");

    expect(decideIntro(storage, "/r/xyz")).toBe("full");
  });

  it("plays the full intro when storage is blocked", () => {
    const blocked = {
      getItem: () => {
        throw new Error("blocked");
      },
      setItem: () => {
        throw new Error("blocked");
      },
    };

    expect(decideIntro(blocked, "/r/abc")).toBe("full");
  });
});

describe("introScript", () => {
  it("marks the document before the first paint", () => {
    document.documentElement.removeAttribute("data-ss-intro");
    localStorage.clear();

    new Function(introScript())();
    expect(document.documentElement.getAttribute("data-ss-intro")).toBe("full");

    new Function(introScript())();
    expect(document.documentElement.getAttribute("data-ss-intro")).toBe("short");
  });
});
