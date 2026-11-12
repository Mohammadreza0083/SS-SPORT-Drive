import { notFound } from "next/navigation";
import { Suspense } from "react";
import { HomePage } from "@/components/viewer/public/home-page";
import { sampleHome } from "@/components/viewer/public/sample";
import { HomePreview } from "./preview";

/**
 * Development-only preview of the public home page (P-1, P-7) with the canvas's sample players, for comparing against
 * the `Home` artboard and for the Playwright visual tests. Add `?signedIn=1` to see the signed-in nav. Production
 * builds answer 404.
 *
 * @returns The preview page.
 */
export default function HomePreviewPage() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return (
    <Suspense fallback={<HomePage data={sampleHome} />}>
      <HomePreview />
    </Suspense>
  );
}
