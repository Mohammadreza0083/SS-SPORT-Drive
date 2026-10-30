import { Barlow_Condensed, Geist, Geist_Mono } from "next/font/google";

/*
 * Typefaces (decisions §9). next/font downloads them at build time and serves them from our own origin,
 * so viewers' browsers never call Google.
 */

/** Barlow Condensed, for player names only. Latin Extended covers names such as Yılmaz or Šimić. */
export const barlowCondensed = Barlow_Condensed({
  weight: ["700", "800"],
  subsets: ["latin", "latin-ext"],
  display: "swap",
  variable: "--font-barlow-condensed",
});

/** Geist, for all other text. */
export const geist = Geist({
  weight: ["400", "500", "600", "700"],
  subsets: ["latin", "latin-ext"],
  display: "swap",
  variable: "--font-geist",
});

/** Geist Mono, for numbers, timestamps and labels. */
export const geistMono = Geist_Mono({
  weight: ["400", "500", "700"],
  subsets: ["latin", "latin-ext"],
  display: "swap",
  variable: "--font-geist-mono",
});

/** The class names that declare the three font variables; set on `<html>`. */
export const fontVariables = [barlowCondensed.variable, geist.variable, geistMono.variable].join(" ");
