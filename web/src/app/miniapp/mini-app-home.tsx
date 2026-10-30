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
    <main>
      {state.kind === "ok" ? (
        <>
          <h1>{miniAppCopy.greeting(state.profile.displayName)}</h1>
          <p>{state.profile.role}</p>
          <Link href="/miniapp/new">{miniAppCopy.newPlayer}</Link>
          <Link href="/miniapp/players">{miniAppCopy.myPlayers}</Link>
        </>
      ) : (
        <p role="status">{message(state)}</p>
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
