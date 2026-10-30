"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { fetchMyPlayers, PLAYER_FILTERS, type Page, type PlayerFilter, type PlayerListItem } from "@/lib/miniapp";
import { myPlayersCopy as copy } from "../copy";

/**
 * My players (T-7a): the caller's players (every player for an admin), newest first, with a search, the filter chips
 * and Next/Previous paging. Each row opens the player detail.
 *
 * @param props - `initData` for the API.
 * @returns The list screen.
 */
export function MyPlayersList({ initData }: { initData: string }) {
  const [filter, setFilter] = useState<PlayerFilter>("all");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<Page<PlayerListItem> | null | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;
    void fetchMyPlayers({ page, filter, search }, initData).then((list) => {
      if (!cancelled) {
        setResult(list);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [page, filter, search, initData]);

  /**
   * Chooses a filter chip and starts again from the first page.
   *
   * @param chip - The chip.
   */
  function choose(chip: PlayerFilter) {
    setFilter(chip);
    setPage(1);
  }

  return (
    <main>
      <h1>{copy.title}</h1>
      <input
        type="search"
        aria-label={copy.search}
        placeholder={copy.search}
        value={search}
        onChange={(event) => {
          setSearch(event.target.value);
          setPage(1);
        }}
      />
      <div>
        {PLAYER_FILTERS.map((chip) => (
          <button key={chip} type="button" aria-pressed={filter === chip} onClick={() => choose(chip)}>
            {copy.filters[chip]}
          </button>
        ))}
      </div>

      {result === undefined && <p role="status">{copy.loading}</p>}
      {result === null && <p role="alert">{copy.failed}</p>}
      {result && result.items.length === 0 && <p>{copy.empty}</p>}
      {result && result.items.length > 0 && (
        <ul>
          {result.items.map((player) => (
            <li key={player.id}>
              <Link href={`/miniapp/players/${player.id}`}>
                <strong>{player.name ?? "—"}</strong>
                <span>{[player.positions.join(" / "), player.nationalityName].filter(Boolean).join(" · ")}</span>
                <span>{statusLabel(player)}</span>
                <span>{copy.files(player.fileCount, player.processingCount)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      {result && (
        <nav>
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            {copy.previous}
          </button>
          <span>{copy.pageOf(result.page, result.totalPages)}</span>
          <button type="button" disabled={page >= result.totalPages} onClick={() => setPage(page + 1)}>
            {copy.next}
          </button>
        </nav>
      )}
    </main>
  );
}

/**
 * The status a row shows: needing attention wins, then live or waiting.
 *
 * @param player - The row.
 * @returns The label.
 */
function statusLabel(player: PlayerListItem): string {
  if (player.needsAttention) {
    return copy.needsAttention;
  }

  return player.status === "Published" ? copy.live : copy.waiting;
}
