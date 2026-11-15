import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { AdminActionError } from "@/lib/admin/client";
import type { AccessRequestRow } from "@/lib/admin/types";
import { RequestDetail } from "./request-detail";

/**
 * One access request in the console's inbox (A-12, canvas `AdminRequests`, decisions §7 P-4): who asked, for which
 * player, what they wrote and how their email was proven. A pending request can be approved, which makes a personal link
 * and emails it (players to grant, 7/30/90 days with the settings default, an optional note; never "no expiry"), or
 * declined, which emails nobody. A decided request shows who decided and, when approved, the link.
 */

afterEach(cleanup);

/** A pending request from the public search. */
const PENDING: AccessRequestRow = {
  id: "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c",
  player: { id: "p-arash", name: "Arash Tehrani", slug: "arash-tehrani" },
  email: "scout@vakifbank-volley.example",
  about: "Head scout at our club. <b>Looking</b> for an opposite.",
  source: "EmailCode",
  status: "Pending",
  createdAt: "2026-11-14T09:00:00+00:00",
  decidedAt: null,
  decidedBy: null,
  shareLink: null,
};

/** The same request once approved. */
const APPROVED: AccessRequestRow = {
  ...PENDING,
  status: "Approved",
  decidedAt: "2026-11-14T09:30:00+00:00",
  decidedBy: "Mohammadreza",
  shareLink: { id: "l-1", code: "X4n8Tq", path: "/r/X4n8Tq", expiresAt: "2026-12-14T09:30:00+00:00" },
};

/**
 * Renders the detail with spies.
 *
 * @param request - The request to show.
 * @param defaultExpiryDays - The settings default.
 * @returns The spies.
 */
function show(request: AccessRequestRow = PENDING, defaultExpiryDays = 30) {
  const onApprove = vi.fn().mockResolvedValue(APPROVED);
  const onDecline = vi.fn().mockResolvedValue({ ...PENDING, status: "Declined", decidedBy: "Mohammadreza", decidedAt: "2026-11-14T09:31:00+00:00" });
  const onSearchPlayers = vi.fn().mockResolvedValue([
    { id: "p-reza", name: "Reza Moradi" },
    { id: "p-arash", name: "Arash Tehrani" },
  ]);
  render(<RequestDetail request={request} defaultExpiryDays={defaultExpiryDays} onApprove={onApprove} onDecline={onDecline} onSearchPlayers={onSearchPlayers} nextHref="/admin/requests" />);
  return { onApprove, onDecline, onSearchPlayers };
}

describe("RequestDetail", () => {
  it("shows who asked, for which player, and their message as plain text", () => {
    show();

    const detail = screen.getByRole("region", { name: "Request detail" });
    expect(detail.textContent).toContain("scout@vakifbank-volley.example");
    expect(detail.textContent).toContain("Arash Tehrani");
    expect(detail.textContent).toContain("Verified by code");
    const quote = detail.querySelector("blockquote");
    expect(quote?.textContent).toBe("Head scout at our club. <b>Looking</b> for an opposite.");
    expect(quote?.querySelector("b")).toBeNull();
  });

  it("says when a signed-in viewer asked", () => {
    show({ ...PENDING, source: "SignedIn" });

    expect(screen.getByRole("region", { name: "Request detail" }).textContent).toContain("Signed-in viewer");
  });

  it("offers 7, 30 and 90 days only, with the settings default chosen", () => {
    show(PENDING, 90);

    const group = screen.getByRole("group", { name: "Link expires" });
    const choices = [...group.querySelectorAll("button")];
    expect(choices.map((button) => button.textContent)).toEqual(["7 days", "30 days", "90 days"]);
    expect(screen.getByRole("button", { name: "90 days" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.queryByRole("button", { name: "Never" })).toBeNull();
  });

  it("grants the requested player by default and approves with the choices", async () => {
    const { onApprove } = show();

    expect(screen.getByRole("button", { name: "Arash Tehrani" }).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(screen.getByRole("button", { name: "7 days" }));
    fireEvent.change(screen.getByLabelText("Note in the email (optional)"), { target: { value: " Full 2026 footage. " } });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Approve & email link" }));
    });

    expect(onApprove).toHaveBeenCalledWith({ expiresInDays: 7, playerIds: ["p-arash"], note: "Full 2026 footage." });
  });

  it("adds another published player found by search", async () => {
    const { onApprove, onSearchPlayers } = show();

    fireEvent.change(screen.getByLabelText("Add a player"), { target: { value: "Reza" } });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Find" }));
    });
    expect(onSearchPlayers).toHaveBeenCalledWith("Reza");
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "+ Reza Moradi" }));
    });
    expect(screen.getByRole("button", { name: "Reza Moradi" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.queryByRole("button", { name: "+ Arash Tehrani" })).toBeNull();

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Approve & email link" }));
    });
    expect(onApprove).toHaveBeenCalledWith({ expiresInDays: 30, playerIds: ["p-arash", "p-reza"] });
  });

  it("cannot approve with no player chosen", () => {
    show();

    fireEvent.click(screen.getByRole("button", { name: "Arash Tehrani" }));

    expect((screen.getByRole("button", { name: "Approve & email link" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("shows the new link once approved", async () => {
    show();

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Approve & email link" }));
    });

    const status = screen.getByRole("status");
    expect(status.textContent).toContain("Approved by Mohammadreza");
    expect(status.textContent).toContain("/r/X4n8Tq");
    expect(screen.queryByRole("button", { name: "Approve & email link" })).toBeNull();
    expect(screen.getByRole("link", { name: "Next request →" }).getAttribute("href")).toBe("/admin/requests");
  });

  it("declines without emailing anyone", async () => {
    const { onDecline } = show();

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Decline" }));
    });

    expect(onDecline).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("status").textContent).toContain("Declined by Mohammadreza. The requester was not emailed.");
  });

  it("explains when another admin decided first", async () => {
    const { onApprove } = show();
    onApprove.mockRejectedValueOnce(new AdminActionError(409, "access_request.already_decided"));

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Approve & email link" }));
    });

    expect(screen.getByRole("alert").textContent).toContain("already decided");
  });

  it("shows a decided request without the form", () => {
    show(APPROVED);

    expect(screen.queryByRole("button", { name: "Approve & email link" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Decline" })).toBeNull();
    expect(screen.getByRole("status").textContent).toContain("/r/X4n8Tq");
    expect(screen.queryByRole("link", { name: "Next request →" })).toBeNull();
  });
});
