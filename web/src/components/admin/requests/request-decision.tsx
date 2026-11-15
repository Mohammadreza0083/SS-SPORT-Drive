"use client";

import { adminCall } from "@/lib/admin/client";
import type { ApprovalBody } from "@/lib/admin/requests";
import type { AccessRequestRow, Paged, PlayerRow } from "@/lib/admin/types";
import { RequestDetail, type GrantablePlayer } from "./request-detail";

/** Props of {@link RequestDecision}. */
type RequestDecisionProps = {
  /** The open request. */
  request: AccessRequestRow;
  /** The expiry the settings preselect. */
  defaultExpiryDays: number;
  /** Where "Next request" goes once this one is decided. */
  nextHref: string;
};

/**
 * Wires {@link RequestDetail} to the API through the console's same-origin route (`/api/admin/…`, CSRF-checked):
 * approve, decline and the published-player search. A decision shows its outcome in place; "Next request" then loads the
 * pending tab again, so the counts, the list and the navigation badge follow.
 *
 * @param props - See {@link RequestDecisionProps}.
 * @returns The detail panel.
 */
export function RequestDecision({ request, defaultExpiryDays, nextHref }: RequestDecisionProps) {
  const path = `access-requests/${encodeURIComponent(request.id)}`;

  /**
   * Finds published players by name (the players list's `live` filter, one short page).
   *
   * @param text - Part of a name.
   * @returns The players found.
   */
  const searchPlayers = async (text: string): Promise<GrantablePlayer[]> => {
    const page = await adminCall<Paged<PlayerRow>>("GET", `players?status=live&pageSize=8&q=${encodeURIComponent(text)}`);
    return page.items.filter((player) => player.name).map((player) => ({ id: player.id, name: player.name as string }));
  };

  return (
    <RequestDetail
      request={request}
      defaultExpiryDays={defaultExpiryDays}
      onApprove={(body: ApprovalBody) => adminCall<AccessRequestRow>("POST", `${path}/approve`, body)}
      onDecline={() => adminCall<AccessRequestRow>("POST", `${path}/decline`, {})}
      onSearchPlayers={searchPlayers}
      nextHref={nextHref}
    />
  );
}
