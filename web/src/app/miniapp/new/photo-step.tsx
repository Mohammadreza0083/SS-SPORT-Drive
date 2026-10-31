"use client";

import { useEffect, useState } from "react";
import { fetchPhotoUrl, uploadPhoto, type PlayerPhoto } from "@/lib/miniapp";
import { photoCopy as copy, ruleMessages } from "../copy";

/** Where the upload stands. */
type UploadState = { kind: "idle" } | { kind: "uploading" } | { kind: "done"; photo: PlayerPhoto; url: string | null } | { kind: "rejected"; code: string | null };

/**
 * The photo step after a player is created (T-4b): pick a photo with the camera or from the gallery, upload it, and
 * see the stored version. A new photo can replace it.
 *
 * @param props - `playerId` and `playerName` of the new player; `initData` for the API calls.
 * @returns The photo step.
 */
export function PhotoStep({ playerId, playerName, initData }: { playerId: string; playerName: string; initData: string }) {
  const [state, setState] = useState<UploadState>({ kind: "idle" });

  const shownUrl = state.kind === "done" ? state.url : null;
  useEffect(() => {
    return () => {
      if (shownUrl) {
        URL.revokeObjectURL(shownUrl);
      }
    };
  }, [shownUrl]);

  /**
   * Uploads the chosen file and reads the stored photo back.
   *
   * @param file - The chosen photo, if any.
   */
  async function onChoose(file: File | undefined) {
    if (!file) {
      return;
    }

    setState({ kind: "uploading" });
    const result = await uploadPhoto(playerId, file, initData);
    if (result.kind === "rejected") {
      setState(result);
      return;
    }

    setState({ kind: "done", photo: result.photo, url: await fetchPhotoUrl(playerId, initData) });
  }

  return (
    <section className="ma-photo">
      {state.kind === "done" && (
        <>
          {state.url && (
            // The photo comes from a local object URL, so next/image has nothing to optimise.
            // eslint-disable-next-line @next/next/no-img-element
            <img src={state.url} alt={playerName} width={state.photo.width} height={state.photo.height} className="ma-photo-img" />
          )}
          <p role="status" className="ma-ok">
            {state.photo.isComplete ? copy.complete(playerName) : copy.stillMissing(state.photo.missingDetails.join(", "))}
          </p>
        </>
      )}

      <label htmlFor="player-photo" className={state.kind === "done" ? "ma-secondary" : "ma-drop"}>
        {state.kind !== "done" && (
          <span className="ma-drop-icon" aria-hidden="true">
            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M4 8h3l2-3h6l2 3h3v11H4z" />
              <circle cx="12" cy="13" r="3.5" />
            </svg>
          </span>
        )}
        {state.kind === "done" ? copy.replace : copy.label}
      </label>
      <input
        id="player-photo"
        className="ma-file"
        type="file"
        accept="image/jpeg,image/png,image/webp,image/heic,image/heif"
        disabled={state.kind === "uploading"}
        onChange={(event) => void onChoose(event.target.files?.[0])}
      />
      <p className="ma-hint">{copy.hint}</p>
      {state.kind === "uploading" && <p role="status">{copy.uploading}</p>}
      {state.kind === "rejected" && <p role="alert">{ruleMessages[state.code ?? ""] ?? copy.rejected}</p>}
    </section>
  );
}
