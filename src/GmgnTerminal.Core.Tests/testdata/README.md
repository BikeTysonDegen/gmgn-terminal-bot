# testdata

JSON fixtures for GmgnApiClient/mapper tests.

Live captures (real responses via proxy, 2026-09-14):
- `rank_swaps.json` — GET /defi/quotation/v1/rank/sol/swaps/1h?orderby=swaps&direction=desc&limit=3
- `rank_wallets.json` — GET /api/v1/rank/sol/wallets/7d?orderby=profit&direction=desc&limit=3
- `token_info.json` — GET /api/v1/token_info/sol/{mint}
- `token_stat.json` — GET /api/v1/token_stat/sol/{mint}

Synthetic samples (`*.sample.json`): endpoints below are behind Cloudflare
bot protection (TLS fingerprint challenge), live capture was not possible.
Shapes follow official gmgn docs and community clients:
- `wallet_activity.sample.json` — GET /api/v1/wallet_activity/sol?type=sell&type=buy&wallet={addr}&limit=N&cost=10
- `trades.sample.json` — GET /defi/quotation/v1/trades/sol/{mint}?limit=N
- `realtime_price.sample.json` — GET /defi/quotation/v1/sol/tokens/realtime_token_price?address={mint}
