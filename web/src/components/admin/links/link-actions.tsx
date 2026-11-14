"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { adminCall } from "@/lib/admin/client";
import { adminCopy } from "../copy";

/** Props of {@link LinkActions}. */
type LinkActionsProps = {
  /** The link. */
  linkId: string;
  /** The link's status: `Active`, `Paused` or `Revoked`. */
  status: string;
};

/**
 * Pause, Resume and Revoke buttons of one share link (the API's `/admin/links/{id}/pause|resume|revoke`). A revoked
 * link has none: it is final.
 *
 * @param props - See {@link LinkActionsProps}.
 * @returns The buttons.
 */
export function LinkActions({ linkId, status }: LinkActionsProps) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const copy = adminCopy.links;

  /**
   * Applies one change and refreshes the page.
   *
   * @param verb - The API's last path segment.
   */
  const change = async (verb: "pause" | "resume" | "revoke") => {
    setBusy(true);
    try {
      await adminCall("POST", `links/${linkId}/${verb}`, {});
      router.refresh();
    } finally {
      setBusy(false);
    }
  };

  if (status === "Revoked") {
    return null;
  }

  return (
    <span className="adm-inline">
      <button type="button" className="adm-btn adm-btn-small" disabled={busy} onClick={() => void change(status === "Paused" ? "resume" : "pause")}>
        {status === "Paused" ? copy.resume : copy.pause}
      </button>
      <button type="button" className="adm-btn adm-btn-small adm-btn-danger" disabled={busy} onClick={() => void change("revoke")}>
        {copy.revoke}
      </button>
    </span>
  );
}
