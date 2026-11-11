import { notFound } from "next/navigation";
import { EmailGate } from "@/components/viewer/gate/email-gate";
import type { GateLink } from "@/lib/viewer/gate";

/** The canvas `Gate` artboard's sample link. */
const sampleLink: GateLink = {
  code: "Ab3xYz",
  type: "Personal",
  playerCount: 1,
  recipientHint: "j***@aurora-volley.example",
  player: { name: "Arash Tehrani", position: "OPP", nationality: { code: "IR", name: "Iran" } },
};

/**
 * Development-only preview of the email gate (L-2) with the canvas's sample player, for comparing against the
 * `Gate` artboard and for the Playwright visual tests (which stub the `/api/links/…` routes). Production builds
 * answer 404.
 *
 * @returns The preview page.
 */
export default function GatePreview() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return <EmailGate link={sampleLink} />;
}
