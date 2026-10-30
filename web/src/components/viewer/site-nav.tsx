import type { ReactNode } from "react";
import { BrandLogo } from "./brand-logo";
import { viewerCopy } from "./copy";

/** Props of {@link SiteNav}. */
type SiteNavProps = {
  /** Right-hand items: {@link NavLink}s, {@link ContactButton}, a sign-in button. */
  children?: ReactNode;
  /** When set, the logo links to this address (for example `/` on the sign-in page). */
  logoHref?: string;
};

/**
 * The viewer site's top bar: logo on the left, links and actions on the right. Fades in with the page.
 *
 * @param props - See {@link SiteNavProps}.
 * @returns The navigation bar.
 */
export function SiteNav({ children, logoHref }: SiteNavProps) {
  const logo = logoHref ? (
    <a href={logoHref} aria-label={viewerCopy.homeLabel}>
      <BrandLogo height={40} className="ss-nav-logo" alt="" priority />
    </a>
  ) : (
    <BrandLogo height={40} className="ss-nav-logo" priority />
  );

  return (
    <nav className="ss-nav ss-fu">
      {logo}
      {children ? <div className="ss-nav-links">{children}</div> : null}
    </nav>
  );
}

/** Props of {@link NavLink}. */
type NavLinkProps = {
  /** Where the link goes (an in-page anchor such as `#videos`, or a path). */
  href: string;
  /** The link text. */
  children: ReactNode;
};

/**
 * A quiet text link in the top bar.
 *
 * @param props - See {@link NavLinkProps}.
 * @returns The link.
 */
export function NavLink({ href, children }: NavLinkProps) {
  return (
    // Hidden on phones, where the canvas keeps only the logo and the contact button.
    <a href={href} className="ss-nav-link">
      {children}
    </a>
  );
}

/**
 * The email-only contact button (decisions §9: `info@ss-sport.co`, no WhatsApp).
 * Desktop reads "Contact SS-SPORT"; tablet and phone shorten it to "Contact", and on phones it becomes
 * an outlined pill, as on the canvas.
 *
 * @returns A pill linking to the contact address.
 */
export function ContactButton() {
  return (
    <a
      href={`mailto:${viewerCopy.contactEmail}`}
      className="ss-btn ss-btn-red ss-btn-sm ss-nav-contact"
      aria-label={viewerCopy.contact}
    >
      <span className="ss-contact-long">{viewerCopy.contact}</span>
      <span className="ss-contact-short">{viewerCopy.contactShort}</span>
    </a>
  );
}
