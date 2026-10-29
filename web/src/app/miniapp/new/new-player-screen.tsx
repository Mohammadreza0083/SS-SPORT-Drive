"use client";

import { miniAppCopy } from "../copy";
import { useInitData } from "../telegram";
import { NewPlayerForm } from "./new-player-form";

/**
 * Shows the new-player form once Telegram's initData is available, or explains that the page needs Telegram.
 *
 * @returns The screen.
 */
export function NewPlayerScreen() {
  const initData = useInitData();
  if (initData === undefined) {
    return <p role="status">{miniAppCopy.signingIn}</p>;
  }

  return initData === null ? <p role="status">{miniAppCopy.outsideTelegram}</p> : <NewPlayerForm initData={initData} />;
}
