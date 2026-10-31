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
    <main className="ma-players">
      <h1>{copy.title}</h1>
      <input
        className="ma-search"
        type="search"
        aria-label={copy.search}
        placeholder={copy.search}
        value={search}
        onChange={(event) => {
          setSearch(event.target.value);
          setPage(1);
        }}
      />
      <div className="ma-chips">
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
        <ul className="ma-list">
          {result.items.map((player) => (
            <li key={player.id}>
              <Link href={`/miniapp/players/${player.id}`} className="ma-row">
                <span className="ma-avatar" aria-hidden="true">
                  {initials(player.name)}
                </span>
                <span className="ma-row-text">
                  <strong>{player.name ?? "—"}</strong>
                  <span className="ma-meta">
                    {[player.positions.join(" / "), player.nationalityName].filter(Boolean).join(" · ")} ·{" "}
                    {copy.files(player.fileCount, player.processingCount)}
                  </span>
                </span>
                <span className={`ma-pill ${statusTone(player)}`}>{statusLabel(player)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      {result && (
        <nav className="ma-pager">
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

/**
 * The colour class of a row's status pill: red for attention, green for live, amber while waiting.
 *
 * @param player - The row.
 * @returns The pill's tone class.
 */
function statusTone(player: PlayerListItem): string {
  if (player.needsAttention) {
    return "ma-pill-attention";
  }

  return player.status === "Published" ? "ma-pill-live" : "ma-pill-waiting";
}

/**
 * Up to two initials for the row's avatar.
 *
 * @param name - The player's name.
 * @returns For example `AT`, or `?` without a name.
 */
function initials(name: string | null): string {
  const letters = (name ?? "")
    .split(/\s+/)
    .filter(Boolean)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("");
  return letters.slice(0, 2) || "?";
}
