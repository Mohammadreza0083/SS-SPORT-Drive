import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render } from "@testing-library/react";
import { EmailOverlay } from "./email-overlay";

/** Lets MutationObserver callbacks (microtasks) run. */
const flush = () => act(async () => await Promise.resolve());

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

/**
 * Renders the overlay inside a stand-in player frame.
 *
 * @returns The frame and the overlay element.
 */
function renderOverlay(random?: () => number) {
  const { container } = render(
    <div className="ss-player-frame">
      <EmailOverlay email="j.demir@aurora-volley.example" random={random} />
    </div>,
  );
  const frame = container.querySelector(".ss-player-frame") as HTMLElement;
  return { frame, overlay: () => frame.querySelector(".ss-wm") as HTMLElement };
}

describe("EmailOverlay", () => {
  it("shows the viewer's email, hidden from screen readers", () => {
    const { overlay } = renderOverlay();

    expect(overlay().textContent).toBe("j.demir@aurora-volley.example");
    expect(overlay().getAttribute("aria-hidden")).toBe("true");
  });

  it("moves to a new spot every 8 seconds, fading out and back in", () => {
    let n = 0;
    const draws = [0.2, 0.5, 0.8, 0.3];
    const { overlay } = renderOverlay(() => draws[n++ % draws.length]);
    const first = overlay().style.getPropertyValue("--wm-x");

    act(() => vi.advanceTimersByTime(8000));
    expect(overlay().classList.contains("is-fading")).toBe(true);

    act(() => vi.advanceTimersByTime(250));
    expect(overlay().classList.contains("is-fading")).toBe(false);
    expect(overlay().style.getPropertyValue("--wm-x")).not.toBe(first);
  });

  it("comes back when it is removed from the page", async () => {
    const { frame, overlay } = renderOverlay();

    overlay().remove();
    await flush();

    expect(frame.querySelector(".ss-wm")?.textContent).toBe("j.demir@aurora-volley.example");
  });

  it("undoes attempts to hide it or change its text", async () => {
    const { overlay } = renderOverlay();

    overlay().style.display = "none";
    overlay().setAttribute("hidden", "");
    overlay().className = "";
    await flush();
    overlay().textContent = "someone@else.example";
    await flush();

    expect(overlay().style.display).toBe("");
    expect(overlay().hasAttribute("hidden")).toBe(false);
    expect(overlay().className).toContain("ss-wm");
    expect(overlay().textContent).toBe("j.demir@aurora-volley.example");
  });

  it("stops moving once it is gone from the player", () => {
    const { overlay } = renderOverlay();
    const node = overlay();

    cleanup();
    act(() => vi.advanceTimersByTime(20000));

    expect(node.isConnected).toBe(false);
  });
});
