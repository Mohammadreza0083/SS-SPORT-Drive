import type { ReactNode } from "react";
import { adminCopy } from "../copy";

/** Props of {@link PageHeader}. */
type PageHeaderProps = {
  /** The page's title. */
  title: string;
  /** The one-line description under it. */
  lead?: string;
  /** Buttons on the right. */
  actions?: ReactNode;
};

/**
 * The block every console board starts with: the eyebrow, the title, a lead and the page's actions.
 *
 * @param props - See {@link PageHeaderProps}.
 * @returns The header.
 */
export function PageHeader({ title, lead, actions }: PageHeaderProps) {
  return (
    <header className="adm-head">
      <div>
        <div className="adm-eyebrow">{adminCopy.eyebrow}</div>
        <h1 className="adm-title">{title}</h1>
        {lead ? <p className="adm-lead">{lead}</p> : null}
      </div>
      {actions ? <div className="adm-actions-top">{actions}</div> : null}
    </header>
  );
}

/** The tones of a status pill. */
export type PillTone = "green" | "amber" | "blue" | "red" | "grey";

/**
 * A small coloured status pill.
 *
 * @param props - The pill's props.
 * @param props.tone - The colour.
 * @param props.children - The label.
 * @returns The pill.
 */
export function StatusPill({ tone, children }: { tone: PillTone; children: ReactNode }) {
  return <span className={tone === "grey" ? "adm-pill" : `adm-pill adm-pill-${tone}`}>{children}</span>;
}
