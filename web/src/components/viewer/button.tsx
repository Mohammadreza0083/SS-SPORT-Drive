import type { AnchorHTMLAttributes, ReactNode } from "react";

/** The canvas's three button looks: brand red, outlined ghost, and white (on the red contact panel). */
export type ButtonVariant = "red" | "ghost" | "light";

/** Props of {@link ButtonLink}. */
type ButtonLinkProps = AnchorHTMLAttributes<HTMLAnchorElement> & {
  /** Where the button goes. */
  href: string;
  /** The look; red by default. */
  variant?: ButtonVariant;
  /** The label, optionally with an icon. */
  children: ReactNode;
};

/**
 * A pill-shaped link styled as a button; lifts on hover.
 *
 * @param props - See {@link ButtonLinkProps}; other anchor attributes pass through.
 * @returns The link.
 */
export function ButtonLink({ href, variant = "red", className, children, ...rest }: ButtonLinkProps) {
  return (
    <a href={href} className={["ss-btn", `ss-btn-${variant}`, className].filter(Boolean).join(" ")} {...rest}>
      {children}
    </a>
  );
}
