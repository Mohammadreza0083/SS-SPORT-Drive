"use client";

import { useSearchParams } from "next/navigation";
import { LockedPlayerPage } from "@/components/viewer/public/locked-page";
import { sampleLocked } from "@/components/viewer/public/sample";
import type { RequestAccessClient } from "@/components/viewer/public/types";

/** The viewer shown when the preview is opened with `?signedIn=1`. */
const PREVIEW_VIEWER = { email: "j.demir@aurora-volley.example" };

/** A client that always succeeds, so the whole dialog can be clicked through without a backend. */
const previewClient: RequestAccessClient = {
  requestCode: async () => ({ kind: "sent", resendAfterSeconds: 60 }),
  submit: async () => ({ kind: "sent" }),
};

/**
 * The locked page on sample data, wired to a client that always succeeds. Functions cannot cross from a server page
 * into a client component, and the query string is read here so the route itself can stay prerendered.
 *
 * @returns The locked page.
 */
export function LockedPreview() {
  const signedIn = useSearchParams().get("signedIn") === "1";
  return <LockedPlayerPage data={{ ...sampleLocked, viewer: signedIn ? PREVIEW_VIEWER : null }} client={previewClient} />;
}
