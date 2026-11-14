"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { adminCall } from "@/lib/admin/client";
import { adminCopy } from "../copy";
import { NewLinkModal, type CreatedLink, type NewLinkBody } from "./new-link-modal";

/** Props of {@link NewLinkLauncher}. */
type NewLinkLauncherProps = {
  /** The player the link is for. */
  player: { id: string; name: string };
  /** The expiry the settings preselect. */
  defaultExpiryDays: number;
  /** Opens the modal on load (`?newLink=1`, from the waiting list's done state). */
  openOnLoad?: boolean;
};

/**
 * The "+ Share link" button of the player page and the New share link modal it opens (A-6). The link is created through
 * the console's route; the page refreshes behind the modal so the new link shows in the aside.
 *
 * @param props - See {@link NewLinkLauncherProps}.
 * @returns The button, and the modal while it is open.
 */
export function NewLinkLauncher({ player, defaultExpiryDays, openOnLoad = false }: NewLinkLauncherProps) {
  const router = useRouter();
  const [open, setOpen] = useState(openOnLoad);

  /**
   * Creates the link.
   *
   * @param body - The form's body.
   * @returns The link's code and public address.
   */
  const create = async (body: NewLinkBody): Promise<CreatedLink> => {
    const link = await adminCall<{ code: string }>("POST", "links", body);
    router.refresh();
    return { code: link.code, url: `${window.location.origin}/r/${link.code}` };
  };

  return (
    <>
      <button type="button" className="adm-btn adm-btn-red" onClick={() => setOpen(true)}>
        {adminCopy.player.shareLink}
      </button>
      {open ? <NewLinkModal player={player} defaultExpiryDays={defaultExpiryDays} onCreate={create} onClose={() => setOpen(false)} /> : null}
    </>
  );
}
