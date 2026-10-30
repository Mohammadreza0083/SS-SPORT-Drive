"use client";

import { miniAppCopy } from "../copy";
import { useInitData } from "../telegram";
import { MyPlayersList } from "./my-players-list";
import { PlayerDetail } from "./player-detail";

/**
 * Waits for Telegram's initData, then shows My players, or explains that the page needs Telegram.
 *
 * @returns The screen.
 */
export function MyPlayersScreen() {
  const initData = useInitData();
  if (initData === undefined) {
    return <p role="status">{miniAppCopy.signingIn}</p>;
  }

  return initData === null ? <p role="status">{miniAppCopy.outsideTelegram}</p> : <MyPlayersList initData={initData} />;
}

/**
 * Waits for Telegram's initData, then shows one player's detail, or explains that the page needs Telegram.
 *
 * @param props - `playerId`: the player to show.
 * @returns The screen.
 */
export function PlayerDetailScreen({ playerId }: { playerId: string }) {
  const initData = useInitData();
  if (initData === undefined) {
    return <p role="status">{miniAppCopy.signingIn}</p>;
  }

  return initData === null ? <p role="status">{miniAppCopy.outsideTelegram}</p> : <PlayerDetail playerId={playerId} initData={initData} />;
}
