"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { adminCall } from "@/lib/admin/client";
import { adminCopy } from "../copy";

/** Props of {@link RetryButton}. */
type RetryButtonProps = {
  /** The path after `/api/admin/`, such as `files/{id}/retry` or `emails/{id}/retry`. */
  path: string;
};

/**
 * The Retry button of a failed file or email (A-9): posts the retry, then refreshes the page.
 *
 * @param props - See {@link RetryButtonProps}.
 * @returns The button.
 */
export function RetryButton({ path }: RetryButtonProps) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  /** Sends the retry. */
  const retry = async () => {
    setBusy(true);
    try {
      await adminCall("POST", path, {});
      router.refresh();
    } finally {
      setBusy(false);
    }
  };

  return (
    <button type="button" className="adm-btn adm-btn-small" disabled={busy} onClick={() => void retry()}>
      {adminCopy.jobs.retry}
    </button>
  );
}
