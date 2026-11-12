import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { ScoutReport } from "@/lib/viewer/scout";
import { ScoutReportView } from "./scout-report";
import { ScoutsSection } from "./scouts-section";

/**
 * The DataVolley view (V-8, owner-approved 2026-11-12: parsed stats, view only). A dialog shows the match header and
 * this player's stats per skill; the scout file itself is never offered. The viewer's email lies over it as on videos
 * and PDFs (V-4). It follows the document viewer's dialog behaviour: focus on Close, Escape closes, no context menu.
 */

/** Arash's report: two skills with actions, one without. */
const report: ScoutReport = {
  id: "s1",
  title: "Match vs Volley Nord",
  match: {
    date: "2026-03-12",
    homeTeam: "Club Aurora",
    awayTeam: "Volley Nord",
    homeSets: 3,
    awaySets: 1,
    sets: [
      { home: 25, away: 21 },
      { home: 22, away: 25 },
      { home: 25, away: 19 },
      { home: 26, away: 24 },
    ],
  },
  player: { number: 7, team: "Club Aurora" },
  skills: [
    { skill: "Serve", total: 5, perfect: 2, positive: 1, ok: 0, negative: 1, poor: 0, error: 1, successRate: 40, efficiency: 20 },
    { skill: "Attack", total: 7, perfect: 3, positive: 1, ok: 0, negative: 1, poor: 1, error: 1, successRate: 43, efficiency: 14 },
    { skill: "Set", total: 0, perfect: 0, positive: 0, ok: 0, negative: 0, poor: 0, error: 0, successRate: null, efficiency: null },
  ],
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  document.body.style.overflow = "";
});

/**
 * Renders the dialog.
 *
 * @param data - The report.
 * @param email - The viewer's email.
 * @returns The close spy.
 */
function open(data: ScoutReport = report, email?: string) {
  const onClose = vi.fn();
  render(<ScoutReportView report={data} playerName="Arash Tehrani" viewerEmail={email} onClose={onClose} />);
  return onClose;
}

describe("ScoutReportView", () => {
  it("opens as a dialog named after the match, with the result, date and set scores", () => {
    open();

    const dialog = screen.getByRole("dialog", { name: "Match vs Volley Nord, match stats" });
    expect(within(dialog).getByRole("heading", { name: "Match vs Volley Nord" })).toBeTruthy();
    expect(within(dialog).getByText("Club Aurora")).toBeTruthy();
    expect(within(dialog).getByText("Volley Nord")).toBeTruthy();
    expect(within(dialog).getByText("Won 3–1")).toBeTruthy();
    expect(within(dialog).getByText("12 Mar 2026")).toBeTruthy();
    expect(within(dialog).getByText("25–21 · 22–25 · 25–19 · 26–24")).toBeTruthy();
    expect(within(dialog).getByText("#7 · Club Aurora")).toBeTruthy();
  });

  it("shows one row per skill with totals, perfect, errors, success and efficiency", () => {
    open();

    const table = screen.getByRole("table", { name: "Arash Tehrani's stats per skill" });
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(4);
    expect(within(rows[0]).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Skill", "Total", "Perfect", "Errors", "Success", "Efficiency"]);
    expect(within(rows[1]).getAllByRole("cell").map((cell) => cell.textContent)).toEqual(["Serve", "5", "2", "1", "40%", "20%"]);
    expect(within(rows[2]).getAllByRole("cell").map((cell) => cell.textContent)).toEqual(["Attack", "7", "3", "1", "43%", "14%"]);
    expect(within(rows[3]).getAllByRole("cell").map((cell) => cell.textContent)).toEqual(["Set", "0", "0", "0", "—", "—"]);
  });

  it("says so when the player is not in the file, and still shows the match", () => {
    open({ ...report, player: null, skills: [] });

    expect(screen.getByText("Arash Tehrani's actions are not in this scout file.")).toBeTruthy();
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.getByText("3–1")).toBeTruthy();
  });

  it("puts focus on Close, stops the page from scrolling, and closes with Close or Escape", () => {
    const onClose = open();

    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Close match stats" }));
    expect(document.body.style.overflow).toBe("hidden");
    fireEvent.click(screen.getByRole("button", { name: "Close match stats" }));
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("is view only: no download, no file link, no context menu", () => {
    const { container } = render(<ScoutReportView report={report} playerName="Arash Tehrani" onClose={() => {}} />);

    expect(container.querySelector("a[download], a[href$='.dvw'], embed, object, iframe")).toBeNull();
    expect(container.textContent?.toLowerCase()).not.toContain("download");
    const event = new MouseEvent("contextmenu", { bubbles: true, cancelable: true });
    act(() => {
      screen.getByRole("table").dispatchEvent(event);
    });
    expect(event.defaultPrevented).toBe(true);
  });

  it("lays the viewer's email over the stats", () => {
    open(report, "j.demir@aurora-volley.example");

    expect(screen.getAllByText("j.demir@aurora-volley.example").length).toBeGreaterThan(0);
  });
});

describe("ScoutsSection", () => {
  const scouts = [
    { id: "s1", title: "Match vs Volley Nord", reportPath: "/api/links/Ab3xYz/players/p/scouts/s1" },
    { id: "s2", title: "Match vs Paykan", reportPath: "/api/links/Ab3xYz/players/p/scouts/s2" },
  ];

  it("lists the scout files under Match stats, and nothing at all when there are none", () => {
    const { rerender } = render(<ScoutsSection scouts={scouts} playerName="Arash Tehrani" />);

    expect(screen.getByRole("heading", { name: "Match stats" })).toBeTruthy();
    expect(screen.getByRole("button", { name: /Match vs Volley Nord/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: /Match vs Paykan/ })).toBeTruthy();

    rerender(<ScoutsSection scouts={[]} playerName="Arash Tehrani" />);
    expect(screen.queryByRole("heading", { name: "Match stats" })).toBeNull();
  });

  it("reads the report when a file is opened, and shows it", async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json(report));
    vi.stubGlobal("fetch", fetchMock);
    render(<ScoutsSection scouts={scouts} playerName="Arash Tehrani" viewerEmail="j.demir@aurora-volley.example" />);

    fireEvent.click(screen.getByRole("button", { name: /Match vs Volley Nord/ }));

    await waitFor(() => expect(screen.getByRole("dialog", { name: "Match vs Volley Nord, match stats" })).toBeTruthy());
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe("/api/links/Ab3xYz/players/p/scouts/s1");
  });

  it("says the file could not be read when the API refuses it, and lets the viewer close the message", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 422 })));
    render(<ScoutsSection scouts={scouts} playerName="Arash Tehrani" />);

    fireEvent.click(screen.getByRole("button", { name: /Match vs Paykan/ }));

    await waitFor(() => expect(screen.getByRole("alert").textContent).toBe("This scout file could not be read."));
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
