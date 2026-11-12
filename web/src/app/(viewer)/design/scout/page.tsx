import { notFound } from "next/navigation";
import { ScoutsSection } from "@/components/viewer/scout/scouts-section";

/**
 * Development-only preview of the Match stats section and its dialog (V-8) with a sample report, for the owner's review
 * (there is no canvas artboard for it yet) and for comparing the look. Press a card to open the dialog. Production builds
 * answer 404.
 *
 * @returns The preview page.
 */
export default function ScoutPreview() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return (
    <main style={{ maxWidth: 1100, margin: "0 auto", padding: "64px 32px" }}>
      <ScoutsSection
        scouts={[{ id: "s1", title: "Match vs Volley Nord", reportPath: "/samples/scout-report.json" }]}
        playerName="Arash Tehrani"
        viewerEmail="j.demir@aurora-volley.example"
      />
    </main>
  );
}
