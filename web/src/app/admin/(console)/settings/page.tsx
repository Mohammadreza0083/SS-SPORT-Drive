import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { SettingsForm } from "@/components/admin/settings/settings-form";
import { PageHeader } from "@/components/admin/shell/page-header";
import { adminGet, requireAdmin } from "@/lib/admin/server";
import type { SettingsData } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.settings.title };

/**
 * `/admin/settings`: the console's settings (A-10, canvas `AdminSettings`). The contact email, the default link expiry,
 * the digest time and the quiet hours are stored in the database and change without a restart; the sessions, the email
 * providers and the media protection are shown read-only, as they come from the deployment.
 *
 * @returns The page.
 */
export default async function SettingsPage() {
  const { token } = await requireAdmin();
  const settings = await adminGet<SettingsData>(token, "/admin/settings");
  const copy = adminCopy.settings;
  const readOnly = [
    { title: copy.sessions, rows: copy.sessionRows },
    { title: copy.email, rows: copy.emailRows },
    { title: copy.media, rows: copy.mediaRows },
  ];
  return (
    <>
      <PageHeader title={copy.title} lead={copy.lead} />
      <div className="adm-stack">
        <SettingsForm
          initial={{
            contactEmail: settings.contactEmail,
            digestAt: settings.digestAt,
            quietHoursStart: settings.quietHoursStart,
            quietHoursEnd: settings.quietHoursEnd,
            defaultLinkExpiryDays: settings.defaultLinkExpiryDays,
          }}
        />
        {readOnly.map((group) => (
          <section key={group.title} className="adm-card" aria-label={group.title}>
            <h2 className="adm-label">{group.title}</h2>
            {group.rows.map(([label, value]) => (
              <div key={label} className="adm-attn" style={{ justifyContent: "space-between" }}>
                <span>{label}</span>
                <span className="adm-mono adm-muted">{value}</span>
              </div>
            ))}
          </section>
        ))}
      </div>
    </>
  );
}
