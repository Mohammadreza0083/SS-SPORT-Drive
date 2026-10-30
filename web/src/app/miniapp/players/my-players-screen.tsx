"use client";

import { use } from "react";
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
 * The route params arrive as a promise and are read here, inside the page's `<Suspense>` boundary, so the page
 * itself never awaits request data while it is prerendered (`cacheComponents`).
 *
 * @param props - `params`: the route params with the player id.
 * @returns The screen.
 */
export function PlayerDetailScreen({ params }: { params: Promise<{ id: string }> }) {
  const playerId = use(params).id;
  const initData = useInitData();
  if (initData === undefined) {
    return <p role="status">{miniAppCopy.signingIn}</p>;
  }

  return initData === null ? <p role="status">{miniAppCopy.outsideTelegram}</p> : <PlayerDetail playerId={playerId} initData={initData} />;
}
