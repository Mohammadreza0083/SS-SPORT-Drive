/** UI copy shared by the viewer shell (English; kept out of the components). */
export const viewerCopy = {
  brand: "SS-SPORT",
  homeLabel: "SS-SPORT home",
  contactEmail: "info@ss-sport.co",
  contact: "Contact SS-SPORT",
  contactShort: "Contact",
  privacy: "Privacy",
  slogan: "Fetch Your Dreams",
} as const;

/** Paths of the brand images served from `public/brand`, taken from the design canvas. */
export const brandImages = {
  /** Horizontal lockup for dark backgrounds (red mark, white wordmark); 561 × 120. */
  logo: { src: "/brand/logo-horizontal-reversed.png", width: 561, height: 120 },
  /** The infinity mark alone; 704 × 346. */
  mark: { src: "/brand/mark.png", width: 704, height: 346 },
} as const;
