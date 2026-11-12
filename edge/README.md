# SS-SPORT Drive — edge

Cloudflare Worker in front of the viewer site (route `drive.ss-sport.co/*`): signed HLS delivery (V-5a), signed document page images and short-link routing (V-5b).

## Routing (V-5b)

| Path | What the edge does |
|---|---|
| `/health` | answers `ok` |
| `/hls/{token}/…` | signed HLS delivery (below) |
| `/pages/{token}/…` | signed document page images (below) |
| `/r/{code}` and `/r/{code}/p/{playerId}` | goes on to the viewer site, but only when the code is six characters of the link alphabet (no `0 O 1 l I`) and the player is a GUID; anything else under `/r/` is a bare 404 at the edge and never reaches the site |
| anything else | goes on to the viewer site unchanged (`fetch(request)`), which answers its own 404s |

## Signed HLS (`/hls/{token}/{video folder}/{file}`)

- The API signs a 30-minute token per video and viewer (`HmacHlsTokenIssuer`, `Edge__HlsSigningKey`).
- The Worker serves a file from R2 (`MEDIA`) only when all of these hold:
  - the signature is valid (checked with the `HLS_SIGNING_KEY` secret);
  - the token has not expired;
  - the file is inside the token's video folder and is an HLS file type;
  - the viewer's `__Host-ssd_vs` session cookie matches the token.
- Anything else gets a plain 403, so a copied playlist URL plays nowhere else.

## Signed document pages (`/pages/{token}/{document folder}/{n}.webp`)

- Same token format and checks as HLS. A token works only on its own kind: `/hls/` needs a folder ending in `/hls`, `/pages/` one ending in `/pages`.
- Only `1.webp`, `2.webp`, … are served, never the original PDF. `Cache-Control: private, max-age=1800`.
- The format is pinned by [`contracts/hls-token-vectors.json`](../contracts/hls-token-vectors.json); both the .NET and the Worker tests run against it.

```bash
npm ci
npm run dev        # wrangler dev
npm run typecheck
npm test
```
