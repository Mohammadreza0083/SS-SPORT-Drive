import { notFound } from "next/navigation";
import { DriftMark, Glow } from "@/components/viewer/backdrop";
import { ButtonLink } from "@/components/viewer/button";
import { Chip } from "@/components/viewer/chip";
import { CountUp } from "@/components/viewer/count-up";
import { NameReveal } from "@/components/viewer/name-reveal";
import { ContactButton, NavLink, SiteNav } from "@/components/viewer/site-nav";
import { SiteFooter } from "@/components/viewer/site-footer";
import { Spotlight } from "@/components/viewer/spotlight";
import { Ticker } from "@/components/viewer/ticker";
import { viewerCopy } from "@/components/viewer/copy";

/** The canvas's sample player, used only on this preview page. */
const sample = {
  name: "Arash Tehrani",
  ticker: ["Arash Tehrani", "Opposite", "Iran", "6 Videos", "Full Match Included", "SS-SPORT"],
  stats: [
    { value: 6, label: "Videos" },
    { value: 117, label: "Minutes of footage" },
    { value: 2, label: "Documents" },
  ],
  tags: ["All", "Highlight", "Spike", "Serve", "Block", "Full match"],
};

/**
 * Development-only preview of the viewer shell (V-1): the canvas's desktop player page frame built from the
 * shared components, for side-by-side comparison with the design canvas. Real player pages arrive with V-2.
 * Production builds answer 404.
 *
 * @returns The preview page.
 */
export default function ShellPreviewPage() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return (
    <>
      <Spotlight style={{ minHeight: 760, overflow: "hidden" }}>
        <Glow />
        <DriftMark />
        <SiteNav>
          <NavLink href="#videos">Videos</NavLink>
          <NavLink href="#docs">Documents</NavLink>
          <ContactButton />
        </SiteNav>
        <div className="ss-container" style={{ padding: "30px var(--ss-gutter) 80px" }}>
          <div className="ss-ribbon ss-fu" style={{ animationDelay: ".5s" }}>
            <span className="ss-ribbon-dot" />
            <span>
              Prepared for <b>Club Aurora</b> · Jan Demir
            </span>
          </div>
          <div className="ss-eyebrow ss-fu" style={{ margin: "30px 0 10px", fontSize: 14, animationDelay: ".7s" }}>
            OPPOSITE <span style={{ color: "#555b68" }}>/</span> IRAN
          </div>
          <NameReveal name={sample.name} />
          <div
            className="ss-under"
            style={{ height: 4, width: 160, background: "var(--ss-red)", margin: "22px 0 28px", borderRadius: 2 }}
          />
          <div className="ss-fu" style={{ display: "flex", gap: 12, flexWrap: "wrap", animationDelay: "1.7s" }}>
            <ButtonLink href="#videos">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M7 4.5v15l13-7.5z" />
              </svg>
              Watch highlights
            </ButtonLink>
            <ButtonLink href="https://volleybox.net" variant="ghost">
              Volleybox profile
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
                <path d="M7 17L17 7M9 7h8v8" />
              </svg>
            </ButtonLink>
          </div>
        </div>
      </Spotlight>

      <Ticker items={sample.ticker} />

      <section
        className="ss-container"
        style={{ padding: "90px var(--ss-gutter) 40px", display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 18 }}
      >
        {sample.stats.map((stat) => (
          <div key={stat.label} className="ss-panel ss-sr">
            <div className="ss-stat-value">
              <CountUp value={stat.value} />
            </div>
            <div className="ss-stat-label">{stat.label}</div>
          </div>
        ))}
        <div className="ss-panel ss-panel-hot ss-sr">
          <div className="ss-stat-value" style={{ color: "var(--ss-red-light)" }}>
            OPP
          </div>
          <div className="ss-stat-label">Position</div>
        </div>
      </section>

      <section id="videos" className="ss-container" style={{ padding: "50px var(--ss-gutter)" }}>
        <div className="ss-sr" style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", flexWrap: "wrap", gap: 16 }}>
          <h2 className="ss-h2">Videos</h2>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            {sample.tags.map((tag) => (
              <Chip key={tag} small pressed={tag === "All"}>
                {tag}
              </Chip>
            ))}
          </div>
        </div>
      </section>

      <section id="contact" className="ss-container" style={{ padding: "40px var(--ss-gutter) 70px" }}>
        <div
          className="ss-cta ss-sr"
          style={{ position: "relative", overflow: "hidden", borderRadius: 30, padding: "64px 56px", display: "flex", flexWrap: "wrap", gap: 30, alignItems: "center", justifyContent: "space-between" }}
        >
          <DriftMark style={{ right: -60, top: -40, width: 520, maxWidth: "none", opacity: 0.25 }} />
          <div style={{ position: "relative", flex: "1 1 420px", minWidth: 0 }}>
            <div style={{ fontWeight: 700, letterSpacing: "-.035em", fontSize: "clamp(44px, 6vw, 76px)", lineHeight: 0.95 }}>
              Interested
              <br />
              in Arash?
            </div>
            <div style={{ fontSize: 16, color: "#FFE0E0", marginTop: 14, maxWidth: 460, lineHeight: 1.55 }}>
              Talk to the SS-SPORT team about availability, trials and terms.
            </div>
          </div>
          <ButtonLink href={`mailto:${viewerCopy.contactEmail}`} variant="light" style={{ position: "relative" }}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <rect x="3" y="5" width="18" height="14" rx="2" />
              <path d="M3 7l9 6 9-6" />
            </svg>
            {viewerCopy.contactEmail}
          </ButtonLink>
        </div>
      </section>

      <SiteFooter>
        Shared privately with Club Aurora · <a href="#contact">{viewerCopy.privacy}</a>
      </SiteFooter>
    </>
  );
}
