import { isValidElement, type ReactElement } from "react";
import { describe, expect, it, vi } from "vitest";

vi.mock("next/headers", () => ({ cookies: async () => ({ get: () => undefined }) }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));
vi.mock("@/lib/auth/sign-in-server", async (original) => ({
  ...(await original<typeof import("@/lib/auth/sign-in-server")>()),
  readAdmin: async () => null,
}));

/**
 * Runs a sign-in page the way Next.js does on the server: the page returns `<Suspense>` around an async server
 * component, which renders the client `SignInPanel`.
 *
 * @param page - The page's default export.
 * @param query - The search params.
 * @returns The props the server hands to the client panel.
 */
async function panelProps(
  page: (props: { searchParams: Promise<Record<string, string>> }) => ReactElement,
  query: Record<string, string> = {},
): Promise<Record<string, unknown>> {
  const suspense = page({ searchParams: Promise.resolve(query) }) as ReactElement<{ children: ReactElement }>;
  const inner = suspense.props.children as ReactElement<Record<string, unknown>> & { type: (props: unknown) => Promise<ReactElement> };
  const panel = await inner.type(inner.props);
  expect(isValidElement(panel)).toBe(true);
  return (panel as ReactElement<Record<string, unknown>>).props;
}

describe("sign-in pages (W-1): only serializable props cross to the client panel", () => {
  // A function prop from a server component to a client component fails the production render ("Functions cannot be
  // passed directly to Client Components"), which broke both sign-in pages.
  it("the admin page passes no functions", async () => {
    const { default: AdminSignInRoute } = await import("./admin/sign-in/page");

    const props = await panelProps(AdminSignInRoute as never);

    expect(props.audience).toBe("admin");
    expect(Object.entries(props).filter(([, value]) => typeof value === "function" || (typeof value === "object" && value !== null && Object.values(value).some((inner) => typeof inner === "function")))).toEqual([]);
  });

  it("the viewer page passes no functions", async () => {
    const { default: ViewerSignInRoute } = await import("./(viewer)/sign-in/page");

    const props = await panelProps(ViewerSignInRoute as never, { token: "t0k3n" });

    expect(props.audience).toBe("viewer");
    expect(props.token).toBe("t0k3n");
    expect(Object.entries(props).filter(([, value]) => typeof value === "function" || (typeof value === "object" && value !== null && Object.values(value).some((inner) => typeof inner === "function")))).toEqual([]);
  });
});
