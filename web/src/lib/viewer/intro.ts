/** Which page-load animation a viewer gets: the full intro, or the 400 ms fade for a returning viewer. */
export type IntroKind = "full" | "short";

/**
 * Decides whether this page plays its full intro (first visit) or the short fade (returning viewer),
 * and remembers the visit. Storage that throws (private mode, blocked cookies) always gives the full intro.
 *
 * Kept self-contained (no imports, no outer variables) because {@link introScript} inlines its source.
 *
 * @param storage - Where visits are remembered, normally `localStorage`.
 * @param page - The page's path, so each player page is remembered on its own.
 * @returns `full` on the first visit to the page, `short` afterwards.
 */
export function decideIntro(storage: Pick<Storage, "getItem" | "setItem">, page: string): IntroKind {
  const key = "ss-intro:" + page;
  try {
    if (storage.getItem(key)) {
      return "short";
    }
    storage.setItem(key, "1");
  } catch {
    return "full";
  }
  return "full";
}

/**
 * Builds the inline script the viewer shell runs before its content paints. It marks `<html>` with
 * `data-ss-intro`, which the motion CSS reads, so the intro never flashes and then switches.
 *
 * @returns The script source.
 */
export function introScript(): string {
  return `(function(){var d=${decideIntro.toString()};var k="full";try{k=d(window.localStorage,location.pathname)}catch(e){}document.documentElement.setAttribute("data-ss-intro",k)})();`;
}
