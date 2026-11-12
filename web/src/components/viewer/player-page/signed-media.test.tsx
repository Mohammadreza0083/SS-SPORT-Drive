import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { DocumentsSection } from "./documents-section";
import { VideosSection } from "./videos-section";

/**
 * The player page asks for signed media when it needs it (V-5b): the current video's stream when it is shown, and a
 * document's page images when the document is opened. The design preview's sample data (fixed `src` / `pages`, no
 * paths) keeps working without the API.
 */

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("VideosSection", () => {
  it("signs the current video's stream from its path", async () => {
    const fetchMock = vi.fn(async () => Response.json({ playlistUrl: "/hls/tok/players/p/videos/v1/v1/hls/master.m3u8", expiresAt: "2026-11-12T10:30:00Z" }));
    vi.stubGlobal("fetch", fetchMock);

    render(
      <VideosSection
        videos={[
          { id: "v1", title: "Highlights 2026", tag: "Highlight", durationSeconds: 84, src: "", streamPath: "/api/links/Ab3xYz/players/p/videos/v1/stream" },
          { id: "v2", title: "Full match", tag: "Full match", durationSeconds: 6490, src: "", streamPath: "/api/links/Ab3xYz/players/p/videos/v2/stream" },
        ]}
      />,
    );

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe("/api/links/Ab3xYz/players/p/videos/v1/stream");
    expect(fetchMock.mock.calls.map((call) => String(call[0]))).not.toContain("/api/links/Ab3xYz/players/p/videos/v2/stream");
  });
});

describe("DocumentsSection", () => {
  it("reads a document's signed pages when it is opened, and shows them", async () => {
    const fetchMock = vi.fn(async () => Response.json({ pages: ["/pages/tok/f/1.webp", "/pages/tok/f/2.webp"], expiresAt: "2026-11-12T10:30:00Z" }));
    vi.stubGlobal("fetch", fetchMock);
    render(
      <DocumentsSection
        playerName="Arash Tehrani"
        documents={[{ id: "d1", title: "Season stats 2026", pageCount: 2, pages: [], pagesPath: "/api/links/Ab3xYz/players/p/documents/d1/pages" }]}
      />,
    );
    expect(fetchMock).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /Season stats 2026/ }));

    await waitFor(() => expect(screen.getAllByRole("img", { name: /Page \d of 2/ })).toHaveLength(2));
    expect(screen.getAllByRole("img", { name: /Page 1 of 2/ })[0].getAttribute("src")).toBe("/pages/tok/f/1.webp");
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe("/api/links/Ab3xYz/players/p/documents/d1/pages");
  });

  it("opens sample pages as they are, without asking the API", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    render(<DocumentsSection playerName="Arash Tehrani" documents={[{ id: "d1", title: "Sample", pageCount: 1, pages: ["/samples/season-stats-1.svg"] }]} />);

    fireEvent.click(screen.getByRole("button", { name: /Sample/ }));

    expect(screen.getAllByRole("img", { name: /Page 1 of 1/ })[0].getAttribute("src")).toBe("/samples/season-stats-1.svg");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
