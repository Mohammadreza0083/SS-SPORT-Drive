import { notFound } from "next/navigation";
import { Suspense } from "react";
import { LockedPreview } from "./preview";

/**
 * Development-only preview of the locked player page (P-2, P-3 UI) with the canvas's sample player, for comparing
 * against the `Locked` artboard and for the Playwright visual tests. Add `?signedIn=1` for the one-click dialog.
 * Production builds answer 404.
 *
 * @returns The preview page.
 */
export default function LockedPagePreview() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return (
    <Suspense>
      <LockedPreview />
    </Suspense>
  );
}
