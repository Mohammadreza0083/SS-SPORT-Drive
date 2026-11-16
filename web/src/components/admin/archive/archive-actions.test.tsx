import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CsvUpload, ImportFolders, StartScanButton } from "./archive-actions";

/**
 * The archive page's actions (Epic 8): start a scan (M-1), import the chosen folders as waiting players (M-2), and send
 * the filled-in CSV back (M-3). Every action goes through the same-origin `/api/admin/…` route as JSON.
 */

const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

/** The stubbed fetch. */
let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => {
  refresh.mockReset();
  fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

/**
 * Builds a JSON answer.
 *
 * @param status - The status.
 * @param body - The body.
 * @returns The answer.
 */
function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("StartScanButton", () => {
  it("starts a scan and refreshes the page", async () => {
    fetchMock.mockResolvedValue(json(202, { id: "s1", status: "Running" }));
    render(<StartScanButton running={false} />);

    fireEvent.click(screen.getByRole("button", { name: "Scan the Drive" }));

    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(fetchMock).toHaveBeenCalledWith("/api/admin/archive/scans", expect.objectContaining({ method: "POST", body: "{}" }));
  });

  it("is disabled while a scan runs", () => {
    render(<StartScanButton running />);

    expect(screen.getByRole("button", { name: "Scanning…" }).hasAttribute("disabled")).toBe(true);
  });

  it("says when another scan already runs", async () => {
    fetchMock.mockResolvedValue(json(409, { code: "archive.scan_running" }));
    render(<StartScanButton running={false} />);

    fireEvent.click(screen.getByRole("button", { name: "Scan the Drive" }));

    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "A scan is already running.");
  });
});

describe("ImportFolders", () => {
  const rows = [
    { id: "f1", label: "SS-SPORT Ivan Valeev／OH-OPP" },
    { id: "f2", label: "SS-SPORT Sara Ahmadi／L" },
  ];

  it("imports the chosen folders and reports the result", async () => {
    fetchMock.mockResolvedValue(json(200, { imported: [{ folderId: "f2", playerId: "p2" }], refused: [] }));
    render(<ImportFolders rows={rows} />);

    fireEvent.click(screen.getByRole("checkbox", { name: "SS-SPORT Sara Ahmadi／L" }));
    fireEvent.click(screen.getByRole("button", { name: "Import 1 to the waiting list" }));

    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(fetchMock).toHaveBeenCalledWith("/api/admin/archive/import", expect.objectContaining({ method: "POST", body: JSON.stringify({ folderIds: ["f2"] }) }));
    expect(screen.getByRole("status").textContent).toBe("1 imported, 0 refused.");
  });

  it("selects every folder on the page at once", () => {
    render(<ImportFolders rows={rows} />);

    fireEvent.click(screen.getByRole("checkbox", { name: "Select all on this page" }));

    expect(screen.getByRole("button", { name: "Import 2 to the waiting list" })).toBeTruthy();
  });

  it("cannot import nothing", () => {
    render(<ImportFolders rows={rows} />);

    expect(screen.getByRole("button", { name: "Import 0 to the waiting list" }).hasAttribute("disabled")).toBe(true);
  });
});

describe("CsvUpload", () => {
  it("sends the file as JSON text and lists the refused rows", async () => {
    fetchMock.mockResolvedValue(json(200, { updated: 2, unchanged: 1, errors: [{ row: 4, code: "player.nationality_invalid", message: "Bad country." }] }));
    render(<CsvUpload />);
    const file = new File(["Id,Name\n1,Ali"], "archive.csv", { type: "text/csv" });

    fireEvent.change(screen.getByLabelText("Filled-in CSV"), { target: { files: [file] } });

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(fetchMock).toHaveBeenCalledWith("/api/admin/archive/waiting.csv", expect.objectContaining({ method: "POST", body: JSON.stringify({ csv: "Id,Name\n1,Ali" }) }));
    expect((await screen.findByRole("status")).textContent).toBe("2 updated, 1 unchanged, 1 refused.");
    expect(screen.getByText("Row 4: Bad country. (player.nationality_invalid)")).toBeTruthy();
    expect(refresh).toHaveBeenCalled();
  });

  it("refuses an empty file without calling the API", async () => {
    render(<CsvUpload />);

    fireEvent.change(screen.getByLabelText("Filled-in CSV"), { target: { files: [new File([" "], "empty.csv", { type: "text/csv" })] } });

    expect((await screen.findByRole("alert")).textContent).toBe("The file is empty.");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
