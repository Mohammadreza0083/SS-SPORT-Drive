"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { fetchMiniAppProfile, readInitData, type MiniAppSignIn, type TelegramWindow } from "@/lib/miniapp";
import { miniAppCopy } from "./copy";
import { useTelegramReady } from "./telegram";

/** What the page shows: still signing in, outside Telegram, or the sign-in result. */
type ViewState = { kind: "loading" } | { kind: "outside" } | MiniAppSignIn;

/**
 * The Mini App entry: signs in once Telegram's script (loaded by the layout) is ready.
 *
 * @returns The Mini App home.
 */
export function MiniApp() {
  return <MiniAppHome telegramReady={useTelegramReady()} />;
}

/**
 * Signs in with Telegram's initData once the script is ready and greets the team member, or explains why not.
 *
 * @param props - `telegramReady`: whether `window.Telegram.WebApp` is available yet.
 * @returns The home screen.
 */
export function MiniAppHome({ telegramReady }: { telegramReady: boolean }) {
  const [state, setState] = useState<ViewState>({ kind: "loading" });

  useEffect(() => {
    if (!telegramReady) {
      return;
    }

    let cancelled = false;
    void signIn(window as unknown as TelegramAppWindow).then((result) => {
      if (!cancelled) {
        setState(result);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [telegramReady]);

  return (
    <main className="ma-home">
      {state.kind === "ok" ? (
        <>
          <section className="ma-hero">
            {/* eslint-disable-next-line @next/next/no-img-element -- a small static brand file */}
            <img src="/brand/logo-horizontal-reversed.png" alt="" aria-hidden="true" className="ma-hero-logo" />
            <h1>{miniAppCopy.greeting(state.profile.displayName)}</h1>
            <p className="ma-role">{state.profile.role}</p>
          </section>
          <div className="ma-tiles">
            <Link href="/miniapp/new" className="ma-tile ma-tile-accent">
              <span className="ma-tile-icon">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
              </span>
              {miniAppCopy.newPlayer}
            </Link>
            <Link href="/miniapp/players" className="ma-tile">
              <span className="ma-tile-icon">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true"><circle cx="9" cy="8" r="3.5" /><path d="M3 20c.6-3.4 3-5.5 6-5.5s5.4 2.1 6 5.5M16 4.5a3.5 3.5 0 0 1 0 7M21 20c-.4-2.4-1.7-4.2-3.6-5" /></svg>
              </span>
              {miniAppCopy.myPlayers}
            </Link>
          </div>
        </>
      ) : (
        <p role="status" className="ma-center">
          {message(state)}
        </p>
      )}
    </main>
  );
}

/** The window once Telegram's script has loaded, including the `ready()` call. */
type TelegramAppWindow = TelegramWindow & { Telegram?: { WebApp?: { ready?: () => void } } };

/**
 * Reads the initData, tells Telegram the app is ready and signs in.
 *
 * @param telegram - The window after Telegram's script has loaded.
 * @returns The sign-in result, or `outside` when the page was not opened from Telegram.
 */
async function signIn(telegram: TelegramAppWindow): Promise<ViewState> {
  const initData = readInitData(telegram);
  if (!initData) {
    return { kind: "outside" };
  }

  telegram.Telegram?.WebApp?.ready?.();
  return fetchMiniAppProfile(initData);
}

/**
 * The message for every state except a successful sign-in.
 *
 * @param state - The view state.
 * @returns The message.
 */
function message(state: Exclude<ViewState, { kind: "ok" }>): string {
  switch (state.kind) {
    case "loading":
      return miniAppCopy.signingIn;
    case "outside":
      return miniAppCopy.outsideTelegram;
    case "unauthorized":
      return miniAppCopy.unauthorized;
    case "forbidden":
      return miniAppCopy.forbidden;
    case "error":
      return miniAppCopy.error;
  }
}
