import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { NewLinkModal } from "./new-link-modal";

/**
 * The New share link modal (A-6, canvas `AdminPlayer`): recipient email, expiry, personal note, then "Create & email".
 * There is no download switch (decisions §6: nothing is ever downloadable) and no "Never" expiry (decisions §1b: every
 * link expires, 7, 30 or 90 days; the default comes from Settings). It calls `POST /admin/links` through the console's
 * same-origin route with the player and shows the new link when done.
 */

afterEach(() => {
  cleanup();
  document.body.style.overflow = "";
});

/**
 * Renders the modal.
 *
 * @param defaultExpiryDays - The settings default.
 * @returns The spies.
 */
function open(defaultExpiryDays = 30) {
  const onCreate = vi.fn().mockResolvedValue({ code: "X4n8Tq", url: "https://drive.ss-sport.co/r/X4n8Tq" });
  const onClose = vi.fn();
  render(<NewLinkModal player={{ id: "p-arash", name: "Arash Tehrani" }} defaultExpiryDays={defaultExpiryDays} onCreate={onCreate} onClose={onClose} />);
  return { onCreate, onClose };
}

describe("NewLinkModal", () => {
  it("is a modal dialog named after the player", () => {
    open();

    const dialog = screen.getByRole("dialog", { name: "New share link · Arash Tehrani" });
    expect(dialog.getAttribute("aria-modal")).toBe("true");
  });

  it("offers 7, 30 and 90 days only, with the settings default chosen", () => {
    open(90);

    const choices = screen.getAllByRole("button").filter((button) => /days$/.test(button.textContent ?? ""));
    expect(choices.map((button) => button.textContent)).toEqual(["7 days", "30 days", "90 days"]);
    expect(screen.getByRole("button", { name: "90 days" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.queryByRole("button", { name: "Never" })).toBeNull();
  });

  it("has no download switch, only the notice that nothing is downloadable", () => {
    open();

    expect(screen.queryByRole("switch")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByLabelText(/download/i)).toBeNull();
    expect(screen.getByText(/never downloadable/)).toBeTruthy();
  });

  it("disables Create until the email is valid", () => {
    open();

    const create = screen.getByRole("button", { name: "Create & email" }) as HTMLButtonElement;
    expect(create.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText("Recipient email"), { target: { value: "coach@club" } });
    expect(create.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText("Recipient email"), { target: { value: "coach@club.example" } });
    expect(create.disabled).toBe(false);
  });

  it("creates the link for this player and shows it", async () => {
    const { onCreate } = open();

    fireEvent.change(screen.getByLabelText("Recipient email"), { target: { value: " Coach@Club.example " } });
    fireEvent.change(screen.getByLabelText("Recipient name"), { target: { value: "Jan Demir" } });
    fireEvent.change(screen.getByLabelText("Organisation"), { target: { value: "Club Aurora" } });
    fireEvent.click(screen.getByRole("button", { name: "7 days" }));
    fireEvent.change(screen.getByLabelText("Personal note"), { target: { value: "Arash's 2026 season." } });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Create & email" }));
    });

    expect(onCreate).toHaveBeenCalledWith({
      recipient: { name: "Jan Demir", organisation: "Club Aurora", emails: ["coach@club.example"] },
      playerIds: ["p-arash"],
      expiresInDays: 7,
      note: "Arash's 2026 season.",
    });
    expect(screen.getByRole("heading", { name: "Link created" })).toBeTruthy();
    expect(screen.getByText("drive.ss-sport.co/r/X4n8Tq")).toBeTruthy();
  });

  it("shows an error and keeps the form when the API refuses", async () => {
    const onCreate = vi.fn().mockRejectedValue(new Error("refused"));
    render(<NewLinkModal player={{ id: "p-arash", name: "Arash Tehrani" }} defaultExpiryDays={30} onCreate={onCreate} onClose={vi.fn()} />);

    fireEvent.change(screen.getByLabelText("Recipient email"), { target: { value: "coach@club.example" } });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Create & email" }));
    });

    expect(screen.getByRole("alert")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Create & email" })).toBeTruthy();
  });

  it("closes on Cancel and on Escape", () => {
    const { onClose } = open();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    fireEvent.keyDown(document, { key: "Escape" });

    expect(onClose).toHaveBeenCalledTimes(2);
  });
});
