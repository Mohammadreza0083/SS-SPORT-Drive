import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { LockedPlayerPage } from "./locked-page";
import { sampleLocked } from "./sample";
import type { RequestAccessClient } from "./types";

afterEach(cleanup);

/** A client that is never expected to be called while only the page is under test. */
const idleClient: RequestAccessClient = { requestCode: vi.fn(), submit: vi.fn() };

describe("LockedPlayerPage (P-2, canvas Locked)", () => {
  it("names the player, the position in full and the nationality", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    expect(screen.getByRole("heading", { level: 1, name: "Arash Tehrani" })).toBeTruthy();
    expect(screen.getAllByText("OPPOSITE").length).toBeGreaterThan(0);
    expect(screen.getAllByText("IRAN").length).toBeGreaterThan(0);
  });

  it("links back to the search", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    expect(screen.getByRole("link", { name: "← Back to search" }).getAttribute("href")).toBe("/");
  });

  it("shows the public counts as stats", () => {
    const { container } = render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    const stats = [...container.querySelectorAll(".lk-stat")].map((s) => s.textContent);
    expect(stats).toEqual(["6Videos", "117Minutes of footage", "2Documents", "OPPPosition"]);
    expect(screen.getByText("6 videos and 2 documents are shared only by personal link.")).toBeTruthy();
  });

  it("lists the videos by title, tag and length, behind a lock", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    expect(screen.getByText("Footage is private")).toBeTruthy();
    expect(screen.getByText("Highlights 2026")).toBeTruthy();
    expect(screen.getByText("Full match vs Paykan")).toBeTruthy();
    expect(screen.getByText("1:48:10")).toBeTruthy();
  });

  it("lists the documents as view-only PDFs with their page count", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    expect(screen.getByText("Season stats 2026")).toBeTruthy();
    expect(screen.getByText("PDF · 2 pages · view only")).toBeTruthy();
    expect(screen.getByText("PDF · 1 page · view only")).toBeTruthy();
  });

  it("links to the Volleybox profile in a new, isolated tab when there is one", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    const link = screen.getByRole("link", { name: /Volleybox profile/ });
    expect(link.getAttribute("href")).toBe(sampleLocked.player.volleyboxUrl);
    expect(link.getAttribute("target")).toBe("_blank");
    expect(link.getAttribute("rel")).toContain("noopener");
  });

  it("hides the Volleybox button when the player has no profile", () => {
    render(<LockedPlayerPage data={{ ...sampleLocked, player: { ...sampleLocked.player, volleyboxUrl: undefined } }} client={idleClient} />);

    expect(screen.queryByRole("link", { name: /Volleybox profile/ })).toBeNull();
  });

  it("invites the visitor by first name", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    expect(screen.getByText("Want to see Arash in action?")).toBeTruthy();
  });

  it("never puts a media or document address in the page (security)", () => {
    const { container } = render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    const html = container.innerHTML;
    expect(html).not.toMatch(/\.m3u8|\.mp4|\.pdf|\.dvw/i);
    expect(container.querySelector("video, iframe, object, embed, hls-video")).toBeNull();
    const allowed = (value: string) =>
      value.startsWith("/") || value.startsWith("#") || value.startsWith("mailto:") || value === sampleLocked.player.volleyboxUrl;
    container.querySelectorAll("[href], [src]").forEach((el) => {
      const value = el.getAttribute("href") ?? el.getAttribute("src")!;
      expect(allowed(value), value).toBe(true);
    });
  });

  it("opens the request-access dialog from any Request access button and closes it again", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    const buttons = screen.getAllByRole("button", { name: "Request access" });
    expect(buttons.length).toBeGreaterThanOrEqual(3);
    expect(screen.queryByRole("dialog")).toBeNull();

    fireEvent.click(buttons[buttons.length - 1]);
    expect(screen.getByRole("dialog", { name: "Request access" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("closes the dialog with Escape", () => {
    render(<LockedPlayerPage data={sampleLocked} client={idleClient} />);

    fireEvent.click(screen.getAllByRole("button", { name: "Request access" })[0]);
    fireEvent.keyDown(document, { key: "Escape" });

    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
