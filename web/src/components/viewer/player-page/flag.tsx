import * as flags from "country-flag-icons/react/3x2";

/** Props of {@link Flag}. */
type FlagProps = {
  /** ISO 3166-1 alpha-2 code (or XK). */
  code: string;
};

/**
 * A small country flag (3:2), drawn as inline SVG on the server so no flag code reaches the browser bundle.
 *
 * @param props - See {@link FlagProps}.
 * @returns The flag, or nothing for an unknown code.
 */
export function Flag({ code }: FlagProps) {
  const Component = (flags as Record<string, (props: { className?: string; "aria-hidden"?: boolean }) => React.JSX.Element>)[
    code.toUpperCase()
  ];
  return Component ? <Component className="pp-flag" aria-hidden /> : null;
}
