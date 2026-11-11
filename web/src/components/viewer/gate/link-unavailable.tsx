import { BrandLogo } from "../brand-logo";
import { viewerCopy } from "../copy";
import { gateCopy } from "./copy";

/**
 * The page for a personal link that does not open (unknown, paused, revoked or expired; the API does not say which),
 * in the gate's look, with the email-only contact (V-7).
 *
 * @returns The page.
 */
export function LinkUnavailable() {
  return (
    <main className="gate">
      <div aria-hidden="true" className="gate-glow ss-glow" />
      <div className="gate-column">
        <header className="gate-top">
          <BrandLogo height={30} priority />
        </header>
        <div className="gate-body gate-unavailable">
          <h1 className="gate-title">{gateCopy.unavailableTitle}</h1>
          <p className="gate-intro">
            {gateCopy.unavailableText}
            <a href={`mailto:${viewerCopy.contactEmail}`}>{viewerCopy.contactEmail}</a>
            {gateCopy.unavailableAfter}
          </p>
        </div>
      </div>
    </main>
  );
}
