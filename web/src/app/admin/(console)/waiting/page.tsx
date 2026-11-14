import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";
import type { WaitingPlayer } from "@/components/admin/waiting/complete-details";
import { WaitingBoard } from "@/components/admin/waiting/waiting-board";
import { adminGet, firstParam, pageParam, requireAdmin } from "@/lib/admin/server";
import type { Paged, PlayerDetail, WaitingRow, WaitingSummary } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.waiting.title };

/** The details a filter can ask for. */
const FILTERS = ["volleybox", "photo", "positions"];

/**
 * `/admin/waiting`: the waiting list (A-5, canvas `Waiting`): players with files but missing details, oldest first, and a
 * drawer to complete them. Publish stays disabled until every field is valid. `?missing=` filters, `?player=` opens a
 * drawer (the first row when none is chosen).
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function WaitingPage({ searchParams }: PageProps<"/admin/waiting">) {
  const params = await searchParams;
  const requested = firstParam(params, "missing");
  const filter = FILTERS.includes(requested ?? "") ? (requested as string) : null;
  const { token } = await requireAdmin();
  const [summary, list, countries] = await Promise.all([
    adminGet<WaitingSummary>(token, "/admin/waiting/summary"),
    adminGet<Paged<WaitingRow>>(token, `/admin/waiting?page=${pageParam(params)}&pageSize=50${filter ? `&missing=${filter}` : ""}`),
    adminGet<{ code: string; name: string }[]>(token, "/admin/countries"),
  ]);
  const chosen = firstParam(params, "player");
  const open = list.items.find((row) => row.id === chosen) ?? list.items[0] ?? null;
  const detail = open ? await adminGet<PlayerDetail>(token, `/admin/players/${open.id}`) : null;
  const selected: WaitingPlayer | null =
    open && detail
      ? {
          id: detail.id,
          folderName: open.folderName ?? adminCopy.waiting.noName,
          name: detail.name ?? "",
          nationalityCode: detail.nationality?.code ?? null,
          positions: detail.positions,
          volleyboxUrl: detail.volleyboxUrl ?? "",
          hasPhoto: detail.hasPhoto,
        }
      : null;
  const tiles = [
    { label: adminCopy.waiting.tiles.waiting, value: summary.waiting },
    { label: adminCopy.waiting.tiles.noVolleybox, value: summary.noVolleybox },
    { label: adminCopy.waiting.tiles.noPhoto, value: summary.noPhoto },
    { label: adminCopy.waiting.tiles.publishedToday, value: summary.publishedToday },
  ];
  return (
    <>
      <PageHeader title={adminCopy.waiting.title} lead={adminCopy.waiting.lead} />
      <div className="adm-kpis">
        {tiles.map((tile) => (
          <div key={tile.label} className="adm-kpi">
            <div className="adm-label">{tile.label}</div>
            <div className="adm-kpi-value">{tile.value}</div>
          </div>
        ))}
      </div>
      <WaitingBoard rows={list.items} selected={selected} countries={countries} filter={filter} />
    </>
  );
}
