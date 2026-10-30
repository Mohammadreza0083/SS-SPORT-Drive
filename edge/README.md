# SS-SPORT Drive — edge

Cloudflare Worker in front of the viewer site: signed HLS delivery (V-5a) and, with V-5b, short links.

## Signed HLS (`/hls/{token}/{video folder}/{file}`)

- The API signs a 30-minute token per video and viewer (`HmacHlsTokenIssuer`, `Edge__HlsSigningKey`).
- The Worker serves a file from R2 (`MEDIA`) only when all of these hold:
  - the signature is valid (checked with the `HLS_SIGNING_KEY` secret);
  - the token has not expired;
  - the file is inside the token's video folder and is an HLS file type;
  - the viewer's `ssd_vs` session cookie matches the token.
- Anything else gets a plain 403, so a copied playlist URL plays nowhere else.
- The format is pinned by [`contracts/hls-token-vectors.json`](../contracts/hls-token-vectors.json); both the .NET and the Worker tests run against it.

```bash
npm ci
npm run dev        # wrangler dev
npm run typecheck
npm test
```
