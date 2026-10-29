/** UI copy of the Telegram Mini App (English; kept out of the components). */
export const miniAppCopy = {
  signingIn: "Signing you in…",
  greeting: (name: string) => `Hello, ${name}`,
  outsideTelegram: "This page is the SS-SPORT Drive Mini App. Open it from the SS-SPORT Drive bot in Telegram.",
  unauthorized: "Your Telegram sign-in could not be verified or has expired. Close and reopen the Mini App.",
  forbidden: "You do not have team access yet. Send /start and ask for access in the bot.",
  error: "Something went wrong. Please try again in a moment.",
  comingSoon: "New player and My players arrive here soon.",
} as const;
