import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { NameReveal } from "./name-reveal";
import { Ticker } from "./ticker";
import { CountUp } from "./count-up";
import { Spotlight } from "./spotlight";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("NameReveal", () => {
  it("is read as the full name, not letter by letter", () => {
    render(<NameReveal name="Arash Tehrani" />);

    expect(screen.getByRole("heading", { level: 1, name: "Arash Tehrani" })).toBeTruthy();
  });

  it("puts each word on its own line and staggers the letters 50 ms apart from 0.8 s", () => {
    const { container } = render(<NameReveal name="Arash Tehrani" />);

    expect(container.querySelectorAll(".ss-ln")).toHaveLength(2);
    const letters = [...container.querySelectorAll<HTMLElement>(".ss-ch")];
    expect(letters.map((l) => l.textContent).join("")).toBe("ARASHTEHRANI");
    expect(letters[0].style.animationDelay).toBe("0.8s");
    expect(letters[1].style.animationDelay).toBe("0.85s");
    expect(letters[11].style.animationDelay).toBe("1.35s");
  });

  it("paints the last letter brand red, as on the canvas", () => {
    const { container } = render(<NameReveal name="Arash Tehrani" />);

    const letters = container.querySelectorAll(".ss-ch");
    expect(letters[letters.length - 1].className).toContain("ss-name-accent");
    expect(letters[0].className).not.toContain("ss-name-accent");
  });

  it("carries the light sweep on the heading", () => {
    render(<NameReveal name="Arash Tehrani" />);

    expect(screen.getByRole("heading").className).toContain("ss-name");
  });
});

describe("Ticker", () => {
  it("repeats the items twice for a seamless loop and hides the copy from screen readers", () => {
    const { container } = render(<Ticker items={["Arash Tehrani", "Opposite", "Iran"]} />);

    const halves = container.querySelectorAll(".ss-track > .ss-ticker-set");
    expect(halves).toHaveLength(2);
    expect(halves[0].getAttribute("aria-hidden")).toBeNull();
    expect(halves[1].getAttribute("aria-hidden")).toBe("true");
    expect(halves[0].querySelectorAll(".ss-ticker-dot")).toHaveLength(3);
  });
});

describe("CountUp", () => {
  it("gives the final value to screen readers and the counter animation", () => {
    const { container } = render(<CountUp value={117} />);

    expect(screen.getByText("117").className).toContain("ss-sr-only");
    const counter = container.querySelector<HTMLElement>(".ss-count");
    expect(counter?.style.getPropertyValue("--n")).toBe("117");
    expect(counter?.getAttribute("aria-hidden")).toBe("true");
  });

  it("starts counting only once it is on screen", () => {
    let notify: (entries: Partial<IntersectionObserverEntry>[]) => void = () => {};
    vi.stubGlobal(
      "IntersectionObserver",
      class {
        constructor(callback: typeof notify) {
          notify = callback;
        }
        observe() {}
        disconnect() {}
      },
    );
    const { container } = render(<CountUp value={6} />);
    const counter = container.querySelector(".ss-count")!;

    expect(counter.className).not.toContain("is-counting");
    act(() => notify([{ isIntersecting: true }]));
    expect(counter.className).toContain("is-counting");
  });

  it("counts straight away where IntersectionObserver is missing", () => {
    vi.stubGlobal("IntersectionObserver", undefined);

    const { container } = render(<CountUp value={2} />);

    expect(container.querySelector(".ss-count")?.className).toContain("is-counting");
  });
});

describe("Spotlight", () => {
  it("moves the red light to the cursor", () => {
    render(
      <Spotlight data-testid="hero">
        <p>hero</p>
      </Spotlight>,
    );
    const hero = screen.getByTestId("hero");
    hero.getBoundingClientRect = () => ({ left: 100, top: 50 }) as DOMRect;

    fireEvent.mouseMove(hero, { clientX: 400, clientY: 250 });

    expect(hero.style.getPropertyValue("--mx")).toBe("300px");
    expect(hero.style.getPropertyValue("--my")).toBe("200px");
  });
});
