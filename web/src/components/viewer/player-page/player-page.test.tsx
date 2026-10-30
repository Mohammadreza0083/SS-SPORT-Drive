import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";

vi.mock("@/components/viewer/player/video-player", () => ({
  /** Stands in for the V-3 player: shows what it was asked to play. */
  VideoPlayer: ({ title, tag, src, autoPlay, viewerEmail }: { title: string; tag?: string; src: string; autoPlay?: boolean; viewerEmail?: string }) => (
    <div data-testid="player" data-src={src} data-autoplay={autoPlay ? "true" : "false"} data-email={viewerEmail}>
      {title} · {tag}
    </div>
  ),
}));

const { PlayerPage } = await import("./player-page");
const { VideosSection } = await import("./videos-section");
const { samplePlayerPage } = await import("./sample");

afterEach(cleanup);

describe("PlayerPage", () => {
  it("reads the player's name as the page heading", () => {
    render(<PlayerPage data={samplePlayerPage} />);

    expect(screen.getByRole("heading", { level: 1, name: "Arash Tehrani" })).toBeTruthy();
  });

  it("shows position and country with the flag", () => {
    const { container } = render(<PlayerPage data={samplePlayerPage} />);

    const eyebrow = container.querySelector(".pp-eyebrow")!;
    expect(eyebrow.textContent).toContain("OPPOSITE");
    expect(eyebrow.textContent).toContain("IRAN");
    expect(eyebrow.querySelector("svg")).not.toBeNull();
  });

  it("says who the link was prepared for", () => {
    render(<PlayerPage data={samplePlayerPage} />);

    expect(screen.getAllByText((_, el) => el?.textContent === "Prepared for Club Aurora · Jan Demir").length).toBeGreaterThan(0);
  });

  it("leaves out the ribbon, the note and the 'shared with' line on a link without a recipient", () => {
    const { container } = render(<PlayerPage data={{ ...samplePlayerPage, recipient: undefined, note: undefined }} />);

    expect(container.textContent).not.toContain("Prepared for");
    expect(container.textContent).not.toContain("A note for you");
    expect(container.textContent).not.toContain("Shared privately with");
  });

  it("shows the personal note with its author", () => {
    render(<PlayerPage data={samplePlayerPage} />);

    expect(screen.getAllByText(/Arash's 2026 season/).length).toBeGreaterThan(0);
    expect(screen.getAllByText("SS-SPORT Scouting").length).toBeGreaterThan(0);
  });

  it("links Watch highlights to the videos and Volleybox to the profile", () => {
    render(<PlayerPage data={samplePlayerPage} />);

    expect(screen.getByRole("link", { name: /Watch highlights/ }).getAttribute("href")).toBe("#videos");
    const volleybox = screen.getByRole("link", { name: /Volleybox profile/ });
    expect(volleybox.getAttribute("href")).toBe("https://volleybox.net");
    expect(volleybox.getAttribute("rel")).toContain("noopener");
  });

  it("counts videos, minutes and documents and shows the position", () => {
    const { container } = render(<PlayerPage data={samplePlayerPage} />);

    const stats = within(container.querySelector(".pp-stats") as HTMLElement);
    expect(stats.getByText("6")).toBeTruthy();
    expect(stats.getByText("117")).toBeTruthy();
    expect(stats.getByText("2")).toBeTruthy();
    expect(stats.getByText("OPP")).toBeTruthy();
  });

  it("has a Documents section on every breakpoint, each view-only", () => {
    render(<PlayerPage data={samplePlayerPage} />);

    const docs = screen.getByRole("region", { name: "Documents" });
    expect(within(docs).getByText("Season stats 2026")).toBeTruthy();
    expect(within(docs).getByText("Player CV")).toBeTruthy();
    expect(within(docs).queryByText(/download/i)).toBeNull();
  });

  it("offers contact by email only", () => {
    const { container } = render(<PlayerPage data={samplePlayerPage} />);

    expect(screen.getByText("Interested in Arash?", { normalizer: (s) => s.replace(/\s+/g, " ").trim() })).toBeTruthy();
    const mails = [...container.querySelectorAll('a[href^="mailto:"]')].map((a) => a.getAttribute("href"));
    expect(new Set(mails)).toEqual(new Set(["mailto:info@ss-sport.co"]));
    expect(container.innerHTML.toLowerCase()).not.toContain("whatsapp");
    expect(container.querySelector('a[href*="wa.me"], a[href^="tel:"]')).toBeNull();
  });

  it("ends with the 'shared privately' footer", () => {
    render(<PlayerPage data={samplePlayerPage} />);

    expect(screen.getByRole("contentinfo").textContent).toContain("Shared privately with Club Aurora");
  });

  it("hides the documents section when there are none", () => {
    render(<PlayerPage data={{ ...samplePlayerPage, documents: [] }} />);

    expect(screen.queryByRole("region", { name: "Documents" })).toBeNull();
  });
});

describe("VideosSection", () => {
  it("starts on the first video without playing it", () => {
    render(<VideosSection videos={samplePlayerPage.videos} />);

    const player = screen.getByTestId("player");
    expect(player.textContent).toBe("Highlights 2026 · Highlight");
    expect(player.dataset.autoplay).toBe("false");
  });

  it("filters the playlist by tag", () => {
    render(<VideosSection videos={samplePlayerPage.videos} />);

    fireEvent.click(screen.getByRole("button", { name: "Spike" }));

    const list = screen.getByRole("list", { name: "Playlist" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(1);
    expect(within(list).getByText("Spikes vs Northbridge")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Spike" }).getAttribute("aria-pressed")).toBe("true");
  });

  it("plays the video picked from the playlist and marks it as now playing", () => {
    render(<VideosSection videos={samplePlayerPage.videos} />);

    fireEvent.click(screen.getByRole("button", { name: /Full match vs Paykan/ }));

    const player = screen.getByTestId("player");
    expect(player.textContent).toBe("Full match vs Paykan · Full match");
    expect(player.dataset.autoplay).toBe("true");
    const picked = screen.getByRole("button", { name: /Full match vs Paykan/ });
    expect(picked.getAttribute("aria-current")).toBe("true");
    expect(within(picked).getByLabelText("Now playing")).toBeTruthy();
  });

  it("hands the viewer's email to the player for the overlay", () => {
    render(<PlayerPage data={samplePlayerPage} />);

    expect(screen.getByTestId("player").dataset.email).toBe("j.demir@aurora-volley.example");
  });

  it("shows each video's duration", () => {
    render(<VideosSection videos={samplePlayerPage.videos} />);

    expect(screen.getByText("1:48:10")).toBeTruthy();
    expect(screen.getByText("1:24")).toBeTruthy();
  });
});
