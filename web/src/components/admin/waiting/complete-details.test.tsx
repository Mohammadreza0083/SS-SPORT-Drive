import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { CompleteDetails, type WaitingPlayer } from "./complete-details";

/**
 * The waiting list's drawer (A-5, canvas `Waiting`, "Done when: matches canvas"): the admin fills the missing details;
 * a checklist ticks each required field; Publish stays disabled, with "N fields left", until every field is valid; then
 * it publishes and shows the done state with "Create share link" and "Next waiting player".
 */

afterEach(cleanup);

/** Ali: has a photo, name and nationality; no position and no Volleybox link yet. */
const ali: WaitingPlayer = {
  id: "p-ali",
  folderName: "SS-SPORT Ali Hoshmand",
  name: "Ali Hoshmand",
  nationalityCode: "IR",
  positions: [],
  volleyboxUrl: "",
  hasPhoto: true,
};

/**
 * Renders the drawer.
 *
 * @param player - The player.
 * @returns The save and publish spies.
 */
function open(player: WaitingPlayer = ali) {
  const onSave = vi.fn().mockResolvedValue(undefined);
  const onPublish = vi.fn().mockResolvedValue(undefined);
  const onNext = vi.fn();
  render(
    <CompleteDetails
      player={player}
      countries={[{ code: "IR", name: "Iran" }, { code: "TR", name: "Türkiye" }]}
      onSave={onSave}
      onPublish={onPublish}
      onNext={onNext}
    />,
  );
  return { onSave, onPublish, onNext };
}

describe("CompleteDetails", () => {
  it("is a region named for completing the details, headed by the Drive folder", () => {
    open();

    expect(screen.getByRole("region", { name: "Complete player details" })).toBeTruthy();
    expect(screen.getByText("SS-SPORT Ali Hoshmand")).toBeTruthy();
  });

  it("keeps Publish disabled and says how many fields are left", () => {
    open();

    const publish = screen.getByRole("button", { name: "Publish page" }) as HTMLButtonElement;
    expect(publish.disabled).toBe(true);
    expect(screen.getByText("2 fields left before this player can go live.")).toBeTruthy();
  });

  it("allows at most two positions and marks the second", () => {
    open();

    fireEvent.click(screen.getByRole("button", { name: "OH" }));
    fireEvent.click(screen.getByRole("button", { name: "OPP" }));
    fireEvent.click(screen.getByRole("button", { name: "MB" }));

    expect(screen.getByRole("button", { name: "OH" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "OPP · 2nd" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "MB" }).getAttribute("aria-pressed")).toBe("false");
  });

  it("reads the player id from the Volleybox link, or explains the expected link", () => {
    open();
    const input = screen.getByLabelText("Volleybox link");

    fireEvent.change(input, { target: { value: "volleybox.net/ali-hoshmand" } });
    expect(screen.getByText("Paste a player profile link that ends in -p and a number.")).toBeTruthy();

    fireEvent.change(input, { target: { value: "volleybox.net/ali-hoshmand-p60214" } });
    expect(screen.getByText("Player ID 60214 · new player")).toBeTruthy();
  });

  it("enables Publish once every field is valid, saves the details and then publishes", async () => {
    const { onSave, onPublish } = open();

    fireEvent.click(screen.getByRole("button", { name: "OH" }));
    fireEvent.change(screen.getByLabelText("Volleybox link"), { target: { value: "volleybox.net/ali-hoshmand-p60214" } });
    const publish = screen.getByRole("button", { name: "Publish page" }) as HTMLButtonElement;
    expect(publish.disabled).toBe(false);
    expect(screen.getByText("Publishing starts transcoding with the watermark.")).toBeTruthy();

    await act(async () => {
      fireEvent.click(publish);
    });

    expect(onSave).toHaveBeenCalledWith("p-ali", {
      name: "Ali Hoshmand",
      nationalityCode: "IR",
      positions: ["OH"],
      volleyboxUrl: "volleybox.net/ali-hoshmand-p60214",
    });
    expect(onPublish).toHaveBeenCalledWith("p-ali");
    expect(onSave.mock.invocationCallOrder[0]).toBeLessThan(onPublish.mock.invocationCallOrder[0]);
    expect(screen.getByText("Ali Hoshmand")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Create share link" }).getAttribute("href")).toBe("/admin/players/p-ali?newLink=1");
    expect(screen.getByRole("button", { name: "Next waiting player" })).toBeTruthy();
  });

  it("asks for a photo when there is none and counts it as missing", () => {
    open({ ...ali, hasPhoto: false, positions: ["OH"], volleyboxUrl: "volleybox.net/ali-hoshmand-p60214" });

    expect(screen.getByRole("button", { name: "Upload photo" })).toBeTruthy();
    expect((screen.getByRole("button", { name: "Publish page" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("1 field left before this player can go live.")).toBeTruthy();
  });

  it("shows the API's refusal and stays open when publishing fails", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const onPublish = vi.fn().mockRejectedValue(Object.assign(new Error("refused"), { code: "player.incomplete" }));
    render(
      <CompleteDetails
        player={{ ...ali, positions: ["OH"], volleyboxUrl: "volleybox.net/ali-hoshmand-p60214" }}
        countries={[{ code: "IR", name: "Iran" }]}
        onSave={onSave}
        onPublish={onPublish}
        onNext={vi.fn()}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Publish page" }));
    });

    expect(screen.getByRole("alert").textContent).toBeTruthy();
    expect(screen.getByRole("button", { name: "Publish page" })).toBeTruthy();
  });
});
