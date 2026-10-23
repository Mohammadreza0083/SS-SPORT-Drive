/**
 * SS-SPORT Drive edge worker.
 * Short links (`/r/{code}`) and HMAC-signed HLS token validation are added in backlog V-5.
 */
const worker = {
  /**
   * Handles every request that reaches the edge.
   *
   * @param request - The incoming request.
   * @returns The response for the request.
   */
  fetch(request: Request): Response {
    const { pathname } = new URL(request.url);

    if (pathname === "/health") {
      return new Response("ok", { status: 200 });
    }

    return new Response("Not found", { status: 404 });
  },
} satisfies ExportedHandler;

export default worker;
