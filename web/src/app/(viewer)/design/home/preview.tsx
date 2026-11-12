"use client";

import { useSearchParams } from "next/navigation";
import { HomePage } from "@/components/viewer/public/home-page";
import { sampleHome } from "@/components/viewer/public/sample";

/** The viewer shown when the preview is opened with `?signedIn=1`. */
const PREVIEW_VIEWER = { email: "j.demir@aurora-volley.example" };

/**
 * The home page on sample data; `?signedIn=1` shows the signed-in nav. It reads the query string in the browser so
 * the route itself can stay prerendered.
 *
 * @returns The home page.
 */
export function HomePreview() {
  const signedIn = useSearchParams().get("signedIn") === "1";
  return <HomePage data={{ ...sampleHome, viewer: signedIn ? PREVIEW_VIEWER : null }} />;
}
