"use client";

import { useCallback, useEffect, useState } from "react";
import { fetchStreamUrl } from "@/lib/viewer/media-client";

/** What {@link useSignedSource} hands the player. */
type SignedSource = {
  /** The master playlist URL to play; empty while the stream is being signed or when signing failed. */
  src: string;
  /** Signs the stream again, for the player's 20-minute renewal (`useHlsRefresh`); absent for a fixed source. */
  refreshSource?: () => Promise<string>;
  /** True when the stream could not be signed. */
  failed: boolean;
};

/**
 * Signs a video's stream when it is shown (backlog V-5b). A video on `/r/{code}` carries the same-origin path of its
 * stream route; this asks it for the signed edge playlist URL and gives the player a `refreshSource` that asks again,
 * so the 30-minute edge token is renewed while the viewer watches. A video with a fixed source (the design preview)
 * is used as it is, with no request and no refresh. Picking another video signs that one; an answer for a video that
 * is no longer shown is dropped.
 *
 * @param streamPath - The video's stream route, or undefined for a fixed source.
 * @param fixedSrc - The fixed playlist URL used when there is no path.
 * @returns The source, its renewal and whether signing failed.
 */
export function useSignedSource(streamPath: string | undefined, fixedSrc: string): SignedSource {
  const [signed, setSigned] = useState<{ path: string; src: string } | null>(null);
  const [failedPath, setFailedPath] = useState<string | null>(null);

  useEffect(() => {
    if (!streamPath) {
      return;
    }
    let active = true;
    fetchStreamUrl(streamPath).then(
      (src) => {
        if (active) {
          setSigned({ path: streamPath, src });
        }
      },
      () => {
        if (active) {
          setFailedPath(streamPath);
        }
      },
    );
    return () => {
      active = false;
    };
  }, [streamPath]);

  const refreshSource = useCallback(() => fetchStreamUrl(streamPath ?? ""), [streamPath]);

  if (!streamPath) {
    return { src: fixedSrc, failed: false };
  }

  return { src: signed?.path === streamPath ? signed.src : "", refreshSource, failed: failedPath === streamPath };
}
