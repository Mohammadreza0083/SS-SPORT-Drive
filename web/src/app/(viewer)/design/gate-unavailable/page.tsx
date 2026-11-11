import { notFound } from "next/navigation";
import { LinkUnavailable } from "@/components/viewer/gate/link-unavailable";

/**
 * Development-only preview of the "not available" page of a personal link (L-2), for the Playwright visual tests.
 * Production builds answer 404.
 *
 * @returns The preview page.
 */
export default function GateUnavailablePreview() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return <LinkUnavailable />;
}
