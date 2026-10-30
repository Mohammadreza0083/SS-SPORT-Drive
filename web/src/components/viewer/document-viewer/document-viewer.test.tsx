import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { DocumentViewer } from "./document-viewer";

const doc = {
  id: "d1",
  title: "Season stats 2026",
  pageCount: 2,
  pages: ["/samples/season-stats-1.svg", "/samples/season-stats-2.svg"],
};

afterEach(() => {
  cleanup();
  document.body.style.overflow = "";
});

/**
 * Renders the viewer open on {@link doc}.
 *
 * @returns The close callback spy.
 */
function open(email?: string) {
  const onClose = vi.fn();
  render(<DocumentViewer document={doc} playerName="Arash Tehrani" viewerEmail={email} onClose={onClose} />);
  return onClose;
}

describe("DocumentViewer", () => {
  it("opens as a dialog named after the document, view only", () => {
    open();

    const dialog = screen.getByRole("dialog", { name: "Season stats 2026, view-only document" });
    expect(within(dialog).getByRole("heading", { name: "Season stats 2026" })).toBeTruthy();
    expect(within(dialog).getByText(/PDF · 2 pages/)).toBeTruthy();
    expect(within(dialog).getAllByText("View only").length).toBeGreaterThan(0);
  });

  it("puts focus on the close button and stops the page behind from scrolling", () => {
    open();

    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Close document" }));
    expect(document.body.style.overflow).toBe("hidden");
  });

  it("closes with the close button and with Escape", () => {
    const onClose = open();

    fireEvent.click(screen.getByRole("button", { name: "Close document" }));
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });

    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("shows every page as an image only, never the PDF file", () => {
    const { container } = render(<DocumentViewer document={doc} playerName="Arash Tehrani" onClose={() => {}} />);

    const pages = screen.getAllByRole("img", { name: /Page \d of 2/ });
    expect(pages).toHaveLength(2);
    expect(pages[0].getAttribute("src")).toBe("/samples/season-stats-1.svg");
    expect(pages[0].getAttribute("draggable")).toBe("false");
    expect(container.querySelector('a[download], a[href$=".pdf"], embed, object, iframe')).toBeNull();
    expect(container.textContent?.toLowerCase()).not.toMatch(/\bdownload\b(?! or printing)/);
  });

  it("blocks the context menu", () => {
    open();

    const event = new MouseEvent("contextmenu", { bubbles: true, cancelable: true });
    act(() => {
      screen.getAllByRole("img", { name: /Page 1 of 2/ })[0].dispatchEvent(event);
    });

    expect(event.defaultPrevented).toBe(true);
  });

  it("counts pages and moves with the thumbnails", () => {
    open();

    expect(screen.getByText((_, el) => el?.classList.contains("dv-counter") === true).textContent).toBe("Page 1 / 2");
    fireEvent.click(screen.getByRole("button", { name: "Go to page 2" }));

    expect(screen.getByText((_, el) => el?.classList.contains("dv-counter") === true).textContent).toBe("Page 2 / 2");
    expect(screen.getByRole("button", { name: "Go to page 2" }).getAttribute("aria-current")).toBe("page");
  });

  it("moves with Previous and Next, stopping at the ends", () => {
    open();

    fireEvent.click(screen.getByRole("button", { name: "Previous page" }));
    expect(screen.getByText((_, el) => el?.classList.contains("dv-counter") === true).textContent).toBe("Page 1 / 2");

    fireEvent.click(screen.getByRole("button", { name: "Next page" }));
    fireEvent.click(screen.getByRole("button", { name: "Next page" }));
    expect(screen.getByText((_, el) => el?.classList.contains("dv-counter") === true).textContent).toBe("Page 2 / 2");
  });

  it("zooms in and out and fits back to 100 %", () => {
    open();

    fireEvent.click(screen.getByRole("button", { name: "Zoom in" }));
    expect(screen.getByText("125%")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Zoom out" }));
    fireEvent.click(screen.getByRole("button", { name: "Zoom out" }));
    expect(screen.getByText("75%")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Fit" }));
    expect(screen.getByText("100%")).toBeTruthy();
  });

  it("lays the viewer's email over every page", () => {
    const { container } = render(
      <DocumentViewer document={doc} playerName="Arash Tehrani" viewerEmail="j.demir@aurora-volley.example" onClose={() => {}} />,
    );

    const marks = container.querySelectorAll(".dv-page .ss-wm");
    expect(marks).toHaveLength(2);
    expect(marks[0].textContent).toBe("j.demir@aurora-volley.example");
    expect(marks[0].className).toContain("ss-wm-dark");
  });

  it("says the document is protected", () => {
    open();

    expect(screen.getByText("Protected document · view only · no downloads or printing")).toBeTruthy();
  });
});
