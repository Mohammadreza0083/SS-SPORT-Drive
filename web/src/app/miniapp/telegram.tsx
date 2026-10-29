"use client";

import Script from "next/script";
import { createContext, useContext, useState, type ReactNode } from "react";
import { readInitData, type TelegramWindow } from "@/lib/miniapp";

/** Telegram's Mini App script; it fills in `window.Telegram.WebApp`. */
const TELEGRAM_SCRIPT = "https://telegram.org/js/telegram-web-app.js";

/** Whether Telegram's script has loaded. */
const TelegramReadyContext = createContext(false);

/**
 * Loads Telegram's Mini App script once for every Mini App page and tells them when it is ready.
 *
 * @param props - `children`: the Mini App pages.
 * @returns The pages, with the script.
 */
export function TelegramProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);

  return (
    <>
      <Script src={TELEGRAM_SCRIPT} strategy="afterInteractive" onReady={() => setReady(true)} />
      <TelegramReadyContext.Provider value={ready}>{children}</TelegramReadyContext.Provider>
    </>
  );
}

/**
 * Tells whether Telegram's script has loaded.
 *
 * @returns `true` once `window.Telegram.WebApp` is available.
 */
export function useTelegramReady(): boolean {
  return useContext(TelegramReadyContext);
}

/**
 * Reads the signed initData once Telegram's script has loaded.
 *
 * @returns `undefined` while loading, `null` outside Telegram, otherwise the raw initData.
 */
export function useInitData(): string | null | undefined {
  const ready = useTelegramReady();
  return ready ? readInitData(window as unknown as TelegramWindow) : undefined;
}
