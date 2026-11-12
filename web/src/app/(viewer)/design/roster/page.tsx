import { notFound } from "next/navigation";
import { RosterPage } from "@/components/viewer/roster/roster-page";
import { sampleRoster } from "@/components/viewer/roster/sample";

/**
 * Development-only preview of the Roster page (V-2b) with the canvas's sample players, for comparing against the
 * `Roster` artboard and for the Playwright visual tests. Production builds answer 404.
 *
 * @returns The preview page.
 */
export default function RosterPreview() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return <RosterPage data={sampleRoster} year={2026} />;
}
