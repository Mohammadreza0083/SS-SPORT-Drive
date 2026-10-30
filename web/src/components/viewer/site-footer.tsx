import type { ReactNode } from "react";
import { BrandLogo } from "./brand-logo";

/** Props of {@link SiteFooter}. */
type SiteFooterProps = {
  /** The footer line, for example "Shared privately with {Org} · Privacy". */
  children?: ReactNode;
};

/**
 * The viewer site's footer: logo and one line of text. Stacks and centres on phones.
 *
 * @param props - See {@link SiteFooterProps}.
 * @returns The footer.
 */
export function SiteFooter({ children }: SiteFooterProps) {
  return (
    <footer className="ss-footer">
      <BrandLogo height={30} />
      {children ? <span>{children}</span> : null}
    </footer>
  );
}
