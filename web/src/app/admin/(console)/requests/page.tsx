import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";

export const metadata: Metadata = { title: adminCopy.requests.title };

/**
 * `/admin/requests`: the access-request inbox belongs to P-4 (Epic 6). Until it lands, this page only says so, so the
 * navigation's link and the dashboard's counts have somewhere to go.
 *
 * @returns The page.
 */
export default function RequestsPage() {
  return (
    <>
      <PageHeader title={adminCopy.requests.title} lead={adminCopy.requests.lead} />
      <p style={{ marginTop: 24 }}>
        <a href="/admin">{adminCopy.requests.back}</a>
      </p>
    </>
  );
}
