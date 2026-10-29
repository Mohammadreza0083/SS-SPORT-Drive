import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NewPlayerForm } from "./new-player-form";

const countries = [
  { code: "IR", name: "Iran" },
  { code: "IT", name: "Italy" },
];

/**
 * Stubs the Mini App API routes the form calls.
 *
 * @param volleybox - The answer of the Volleybox check.
 * @param created - The answer of the create call.
 * @returns The fetch stub.
 */
function stubApi(
  volleybox: Response,
  created: Response = Response.json({ id: "p1", name: "Arash Tehrani", status: "Waiting" }, { status: 201 }),
  driveFolder: Response = folderPreview(),
) {
  const fetchMock = vi.fn((url: string, init?: RequestInit) => {
    void init;
    if (url.startsWith("/api/miniapp/countries")) {
      return Promise.resolve(Response.json(countries));
    }

    if (url.startsWith("/api/miniapp/volleybox")) {
      return Promise.resolve(volleybox.clone());
    }

    if (url.startsWith("/api/miniapp/drive-folder")) {
      return Promise.resolve(driveFolder.clone());
    }

    return Promise.resolve(created.clone());
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/** A Drive check for a readable folder. */
const folderPreview = () =>
  Response.json({
    folderId: "1AbC-dEf_GhIjKlMnOp",
    name: "SS-SPORT Arash Tehrani",
    videoCount: 14,
    documentCount: 3,
    scoutCount: 2,
    otherCount: 0,
    totalBytes: 4831838208,
    isTruncated: false,
  });

/** A Volleybox check for a new player. */
const newVolleybox = () => Response.json({ volleyboxId: 48213, url: "u", existingPlayerId: null, existingPlayerName: null });

/** Fills every field with valid values. */
async function fillForm() {
  fireEvent.click(screen.getByRole("button", { name: "Men" }));
  fireEvent.change(screen.getByLabelText("Full name"), { target: { value: "Arash Tehrani" } });
  fireEvent.change(screen.getByLabelText("Nationality"), { target: { value: "ira" } });
  fireEvent.click(await screen.findByRole("option", { name: /Iran/ }));
  fireEvent.click(screen.getByRole("button", { name: "OH" }));
  fireEvent.change(screen.getByLabelText("Volleybox link"), { target: { value: "https://volleybox.net/arash-tehrani-p48213" } });
  fireEvent.blur(screen.getByLabelText("Volleybox link"));
  fireEvent.change(screen.getByLabelText("Drive folder link"), { target: { value: "https://drive.google.com/drive/folders/1AbC-dEf_GhIjKlMnOp" } });
  fireEvent.blur(screen.getByLabelText("Drive folder link"));
}

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("NewPlayerForm", () => {
  it("keeps Create disabled until every field is valid, then creates the player", async () => {
    const fetchMock = stubApi(newVolleybox());
    render(<NewPlayerForm initData="user=1&hash=ab" />);
    const create = screen.getByRole("button", { name: "Create player" }) as HTMLButtonElement;

    expect(create.disabled).toBe(true);
    await fillForm();
    expect(await screen.findByText("Player ID 48213 · new player")).toBeTruthy();
    await waitFor(() => expect(create.disabled).toBe(false));

    fireEvent.click(create);

    expect(await screen.findByText(/waiting for an admin/i)).toBeTruthy();
    const post = fetchMock.mock.calls.find(([url]) => url === "/api/miniapp/players")!;
    expect(JSON.parse(post[1]!.body as string)).toEqual({
      category: "Men",
      name: "Arash Tehrani",
      nationalityCode: "IR",
      positions: ["OH"],
      volleyboxUrl: "https://volleybox.net/arash-tehrani-p48213",
      driveFolderUrl: "https://drive.google.com/drive/folders/1AbC-dEf_GhIjKlMnOp",
    });
  });

  it("blocks a Volleybox id that already belongs to a player", async () => {
    stubApi(Response.json({ volleyboxId: 48213, url: "u", existingPlayerId: "p9", existingPlayerName: "Arash Tehrani" }));
    render(<NewPlayerForm initData="x" />);

    await fillForm();

    expect(await screen.findByText(/Arash Tehrani already has a page/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Create player" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("explains a team page", async () => {
    stubApi(new Response(JSON.stringify({ status: 422, code: "player.volleybox_not_a_player" }), { status: 422 }));
    render(<NewPlayerForm initData="x" />);

    await fillForm();

    expect(await screen.findByText(/team page/i)).toBeTruthy();
  });

  it("previews the Drive folder once the link is checked", async () => {
    stubApi(newVolleybox());
    render(<NewPlayerForm initData="x" />);

    await fillForm();

    expect(await screen.findByText("SS-SPORT Arash Tehrani · 14 videos · 3 documents · 2 scout files · 4.5 GB")).toBeTruthy();
  });

  it("blocks a folder the service account cannot read", async () => {
    stubApi(newVolleybox(), undefined, new Response(JSON.stringify({ status: 422, code: "drive_folder.no_access" }), { status: 422 }));
    render(<NewPlayerForm initData="x" />);

    await fillForm();

    expect(await screen.findByText(/no access to it/i)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Create player" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("says when Google Drive cannot be reached", async () => {
    stubApi(newVolleybox(), undefined, new Response(JSON.stringify({ status: 503, code: "drive.unavailable" }), { status: 503 }));
    render(<NewPlayerForm initData="x" />);

    await fillForm();

    expect(await screen.findByText(/cannot be reached/i)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Create player" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("flags a link that is not a Drive folder", async () => {
    stubApi(newVolleybox());
    render(<NewPlayerForm initData="x" />);

    fireEvent.change(screen.getByLabelText("Drive folder link"), { target: { value: "https://drive.google.com/file/d/1AbC-dEf_GhIjKlMnOp/view" } });

    expect(await screen.findByText(/link of the player's Drive folder/i)).toBeTruthy();
  });

  it("shows the reason when the API refuses the player", async () => {
    stubApi(newVolleybox(), new Response(JSON.stringify({ status: 422, code: "drive_folder.taken" }), { status: 422 }));
    render(<NewPlayerForm initData="x" />);
    await fillForm();
    const create = screen.getByRole("button", { name: "Create player" }) as HTMLButtonElement;
    await waitFor(() => expect(create.disabled).toBe(false));

    fireEvent.click(create);

    expect(await screen.findByText(/already belongs to a player/i)).toBeTruthy();
  });

  it("asks for the photo after creating the player, uploads it and shows it back", async () => {
    const fetchMock = stubApi(newVolleybox());
    fetchMock.mockImplementation((url: string, init?: RequestInit) => {
      if (url.startsWith("/api/miniapp/countries")) {
        return Promise.resolve(Response.json(countries));
      }

      if (url.startsWith("/api/miniapp/volleybox")) {
        return Promise.resolve(newVolleybox());
      }

      if (url.startsWith("/api/miniapp/drive-folder")) {
        return Promise.resolve(folderPreview());
      }

      if (url === "/api/miniapp/players") {
        return Promise.resolve(Response.json({ id: "p1", name: "Arash Tehrani", status: "Waiting" }, { status: 201 }));
      }

      if (init?.method === "PUT") {
        return Promise.resolve(Response.json({ photoKey: "players/p1/photo-1.webp", width: 1000, height: 1200, isComplete: true, missingDetails: [] }));
      }

      return Promise.resolve(new Response(new Blob(["webp"], { type: "image/webp" })));
    });
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: vi.fn().mockReturnValue("blob:photo"), revokeObjectURL: vi.fn() }));
    render(<NewPlayerForm initData="x" />);
    await fillForm();
    const create = screen.getByRole("button", { name: "Create player" }) as HTMLButtonElement;
    await waitFor(() => expect(create.disabled).toBe(false));
    fireEvent.click(create);
    const input = await screen.findByLabelText("Player photo");

    fireEvent.change(input, { target: { files: [new File(["jpeg"], "photo.jpg", { type: "image/jpeg" })] } });

    expect(await screen.findByText(/complete/i)).toBeTruthy();
    expect((screen.getByRole("img", { name: "Arash Tehrani" }) as HTMLImageElement).src).toBe("blob:photo");
    expect(fetchMock).toHaveBeenCalledWith("/api/miniapp/players/p1/photo", expect.objectContaining({ method: "PUT" }));
  });

  it("explains a photo that is too small", async () => {
    const fetchMock = stubApi(newVolleybox());
    fetchMock.mockImplementation((url: string, init?: RequestInit) => {
      if (url.startsWith("/api/miniapp/countries")) {
        return Promise.resolve(Response.json(countries));
      }

      if (url.startsWith("/api/miniapp/volleybox")) {
        return Promise.resolve(newVolleybox());
      }

      if (url.startsWith("/api/miniapp/drive-folder")) {
        return Promise.resolve(folderPreview());
      }

      if (init?.method === "PUT") {
        return Promise.resolve(new Response(JSON.stringify({ status: 422, code: "player.photo_too_small" }), { status: 422 }));
      }

      return Promise.resolve(Response.json({ id: "p1", name: "Arash Tehrani", status: "Waiting" }, { status: 201 }));
    });
    render(<NewPlayerForm initData="x" />);
    await fillForm();
    const create = screen.getByRole("button", { name: "Create player" }) as HTMLButtonElement;
    await waitFor(() => expect(create.disabled).toBe(false));
    fireEvent.click(create);

    fireEvent.change(await screen.findByLabelText("Player photo"), { target: { files: [new File(["x"], "small.jpg", { type: "image/jpeg" })] } });

    expect(await screen.findByText(/at least 800 px/i)).toBeTruthy();
  });

  it("restores a saved draft", async () => {
    window.localStorage.setItem("ssd.miniapp.newPlayerDraft", JSON.stringify({ name: "Saved Name", positions: ["MB"] }));
    stubApi(newVolleybox());

    render(<NewPlayerForm initData="x" />);

    expect(((await screen.findByLabelText("Full name")) as HTMLInputElement).value).toBe("Saved Name");
    expect(screen.getByRole("button", { name: "MB" }).getAttribute("aria-pressed")).toBe("true");
  });
});
