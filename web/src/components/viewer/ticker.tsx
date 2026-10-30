import { Fragment } from "react";

/** Props of {@link Ticker}. */
type TickerProps = {
  /** The phrases that scroll by, e.g. name, position, country, video count. */
  items: string[];
};

/**
 * The tilted red band that scrolls the player's facts. The set is rendered twice so the loop is seamless;
 * the copy is hidden from screen readers.
 *
 * @param props - See {@link TickerProps}.
 * @returns The ticker band.
 */
export function Ticker({ items }: TickerProps) {
  /**
   * Renders one full set of items, each followed by a dot.
   *
   * @param hidden - Hides the set from assistive technology (the duplicate).
   * @returns The set.
   */
  const set = (hidden: boolean) => (
    <div className="ss-ticker-set" aria-hidden={hidden ? "true" : undefined}>
      {items.map((item, i) => (
        <Fragment key={i}>
          <span>{item}</span>
          <span className="ss-ticker-dot" aria-hidden="true">
            ●
          </span>
        </Fragment>
      ))}
    </div>
  );

  return (
    <div className="ss-ticker">
      <div className="ss-track">
        {set(false)}
        {set(true)}
      </div>
    </div>
  );
}
