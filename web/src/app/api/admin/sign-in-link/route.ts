import { requestAdminLink } from "@/lib/admin/session";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * `POST /api/admin/sign-in-link`: asks for an emailed sign-in link; always answers 202 so nobody can probe for admins.
 *
 * @param request - The sign-in page's JSON post with `email`.
 * @returns 202, or 429 when the API rate-limits the address.
 */
export async function POST(request: Request): Promise<Response> {
  return requestAdminLink(request, apiBaseUrl());
}
