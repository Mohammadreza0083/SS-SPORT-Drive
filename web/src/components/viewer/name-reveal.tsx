/** Props of {@link NameReveal}. */
type NameRevealProps = {
  /** The player's full name, as stored (Latin letters). */
  name: string;
  /** When the first letter starts rising, in seconds; the canvas starts at 0.8 s. */
  startDelay?: number;
  /** The gap between letters, in seconds; the brief asks for 50 ms. */
  stagger?: number;
};

/**
 * The player's name in Barlow Condensed: one word per line, letters rising in one after another,
 * the last letter in brand red, and a light sweeping across it every 4.5 s.
 * Screen readers get the plain name; the letter spans are hidden from them.
 *
 * @param props - See {@link NameRevealProps}.
 * @returns The page's main heading.
 */
export function NameReveal({ name, startDelay = 0.8, stagger = 0.05 }: NameRevealProps) {
  const words = name.trim().toUpperCase().split(/\s+/);
  const total = words.reduce((sum, word) => sum + word.length, 0);
  let index = 0;

  return (
    <h1 className="ss-name" aria-label={name}>
      {words.map((word, w) => (
        <span key={w} className="ss-ln" aria-hidden="true">
          {[...word].map((letter, l) => {
            const i = index++;
            return (
              <span
                key={l}
                className={i === total - 1 ? "ss-ch ss-name-accent" : "ss-ch"}
                style={{ animationDelay: `${(startDelay + i * stagger).toFixed(2)}s` }}
              >
                {letter}
              </span>
            );
          })}
        </span>
      ))}
    </h1>
  );
}
