import type { Metadata } from "next";
import Link from "next/link";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";
import { formatPercent, formatRelative } from "@/lib/admin/format";
import { adminGet, firstParam, requireAdmin } from "@/lib/admin/server";
import type { ActivityRow, Cursored, DashboardData } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.dashboard.title };

/** The periods of the chips, in order. */
const PERIODS = ["7d", "30d", "90d"] as const;

/**
 * `/admin`: the dashboard (A-2, canvas `AdminDashboard`): KPIs for a period (`?period=`), views per day, what needs
 * attention, the live activity feed and the top players.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function DashboardPage({ searchParams }: PageProps<"/admin">) {
  const params = await searchParams;
  const requested = firstParam(params, "period");
  const period = (PERIODS as readonly string[]).includes(requested ?? "") ? (requested as string) : "30d";
  const { token } = await requireAdmin();
  const [data, activity] = await Promise.all([
    adminGet<DashboardData>(token, `/admin/dashboard?period=${period}`),
    adminGet<Cursored<ActivityRow>>(token, "/admin/activity?pageSize=5"),
  ]);
  const copy = adminCopy.dashboard;
  const now = new Date();
  const peak = Math.max(1, ...data.viewsByDay.map((day) => day.views));
  const attention = data.needsAttention;
  const rows: { tone: "red" | "amber"; text: string; href: string }[] = [
    ...(attention.accessRequests > 0 ? [{ tone: "red" as const, text: copy.attentionRows.requests(attention.accessRequests), href: "/admin/requests" }] : []),
    ...(attention.waitingPlayers > 0 ? [{ tone: "amber" as const, text: copy.attentionRows.waiting(attention.waitingPlayers), href: "/admin/waiting" }] : []),
    ...(attention.failedFiles > 0 ? [{ tone: "red" as const, text: copy.attentionRows.failedFiles(attention.failedFiles), href: "/admin/jobs?state=failed" }] : []),
    ...(attention.failedEmails > 0 ? [{ tone: "red" as const, text: copy.attentionRows.failedEmails(attention.failedEmails), href: "/admin/jobs?tab=email" }] : []),
    ...(attention.retryingEmails > 0 ? [{ tone: "amber" as const, text: copy.attentionRows.retryingEmails(attention.retryingEmails), href: "/admin/jobs?tab=email" }] : []),
    ...(attention.lostFolders > 0 ? [{ tone: "red" as const, text: copy.attentionRows.lostFolders(attention.lostFolders), href: "/admin/jobs" }] : []),
  ];
  const kpis = [
    { label: copy.kpi.live, value: String(data.kpis.livePlayers), delta: "", href: "/admin/players?status=live" },
    { label: copy.kpi.views, value: String(data.kpis.views), delta: copy.vsPrevious(data.kpis.views, data.kpis.previousViews), href: "/admin/viewers" },
    { label: copy.kpi.unique, value: String(data.kpis.uniqueViewers), delta: "", href: "/admin/viewers" },
    { label: copy.kpi.completion, value: formatPercent(data.kpis.averageCompletion), delta: "", href: "/admin/viewers" },
    { label: copy.kpi.requests, value: String(data.kpis.pendingAccessRequests), delta: "", href: "/admin/requests" },
    { label: copy.kpi.waiting, value: String(data.kpis.waitingPlayers), delta: "", href: "/admin/waiting" },
  ];

  return (
    <>
      <PageHeader
        title={copy.title}
        lead={copy.lead}
        actions={
          <div className="adm-chips">
            {PERIODS.map((item) => (
              <a key={item} className="adm-chip" href={item === "30d" ? "/admin" : `/admin?period=${item}`} aria-current={item === period ? "true" : undefined}>
                {copy.periods[item]}
              </a>
            ))}
          </div>
        }
      />
      <div className="adm-kpis">
        {kpis.map((kpi) => (
          <a key={kpi.label} href={kpi.href} className="adm-kpi">
            <div className="adm-label">{kpi.label}</div>
            <div className="adm-kpi-value">{kpi.value}</div>
            {kpi.delta ? <div className="adm-kpi-delta">{kpi.delta}</div> : null}
          </a>
        ))}
      </div>

      <div className="adm-wrap">
        <section className="adm-card" aria-label={copy.viewsChart} style={{ flex: "999 1 520px" }}>
          <div className="adm-head">
            <div className="adm-label">
              {copy.viewsChart} · {copy.periodLabel[period]}
            </div>
            <span className="adm-mono adm-muted">{copy.total(data.kpis.views)}</span>
          </div>
          <div className="adm-bars">
            {data.viewsByDay.map((day) => (
              <span key={day.date} className="adm-bar" title={`${day.date}: ${day.views}`} style={{ height: `${Math.max(2, Math.round((day.views / peak) * 100))}%` }} />
            ))}
          </div>
          <div className="adm-axis">
            <span>{data.viewsByDay[0]?.date}</span>
            <span>{copy.today}</span>
          </div>
        </section>
        <section className="adm-card" aria-label={copy.attention} style={{ flex: "1 1 340px" }}>
          <div className="adm-label">{copy.attention}</div>
          {rows.length === 0 ? <p className="adm-muted">{copy.nothing}</p> : null}
          {rows.map((row) => (
            <a key={row.text} href={row.href} className="adm-attn">
              <span className={row.tone === "red" ? "adm-dot adm-dot-red" : "adm-dot adm-dot-amber"} />
              {row.text}
            </a>
          ))}
        </section>
      </div>

      <div className="adm-wrap">
        <section className="adm-table" aria-label={copy.live} style={{ flex: "999 1 560px", marginTop: 0 }}>
          <div className="adm-grid">
            <div className="adm-label">{copy.live}</div>
            <Link href="/admin/viewers" style={{ justifySelf: "end" }}>
              {copy.allViewers} ›
            </Link>
          </div>
          {activity.items.length === 0 ? <div className="adm-empty">{copy.noActivity}</div> : null}
          <div className="adm-scroll">
            {activity.items.map((row) => (
              <a key={row.watchId} href={row.viewerId ? `/admin/viewers/${row.viewerId}` : "/admin/viewers"} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: "2.2fr 1.3fr 1.6fr .9fr", minWidth: 620 }}>
                <span className="adm-inline">
                  <span className={row.isLive ? "adm-dot adm-dot-live" : "adm-dot"} />
                  <span>
                    {row.email}
                    <span className="adm-sub">{row.organisation}</span>
                  </span>
                </span>
                <span>{row.playerName}</span>
                <span>
                  {row.videoTitle} · {formatPercent(row.completion)}
                </span>
                <span className="adm-mono adm-muted">{row.isLive ? "now" : formatRelative(row.lastSeenAt, now)}</span>
              </a>
            ))}
          </div>
        </section>
        <section className="adm-card" aria-label={copy.top} style={{ flex: "1 1 320px" }}>
          <div className="adm-label">
            {copy.top} · {copy.periodLabel[period]}
          </div>
          {data.topPlayers.length === 0 ? <p className="adm-muted">{copy.noTop}</p> : null}
          {data.topPlayers.map((player, index) => (
            <a key={player.playerId} href={`/admin/players/${player.playerId}`} className="adm-attn">
              <span className="adm-mono adm-muted" style={{ width: 16 }}>
                {index + 1}
              </span>
              <span className="adm-name" style={{ flex: 1 }}>
                {player.name}
              </span>
              <span className="adm-mono">
                {copy.views(player.views)} · {formatPercent(player.averageCompletion)}
              </span>
            </a>
          ))}
        </section>
      </div>
    </>
  );
}
