# SETUP

Per-tab walkthrough. Screenshots land in `docs/` once the UI settles.

## First start

1. Run `gmgn-terminal.exe` (or `dotnet run --project src/GmgnTerminal.App`).
2. `config.json` is created next to the exe with defaults, and two demo
   leaders are seeded (Offline feed is on by default).
3. Logs go to `logs/GmgnTerminal-yyyyMMdd.log` next to the exe, one file per day,
   rolling at 10 MB.

## Main tab

**Leaders grid**

- Paste a Solana wallet address into the field, optional alias, press ADD.
  Address length is sanity-checked (32–44 chars), duplicates are rejected.
- Columns: enabled checkbox, wallet (alias or shortened address, full address
  in tooltip), ALIAS, MULT (per-leader size multiplier for proportional mode),
  MIN SOL (per-leader minimum; 0 = use the global filter from Trading).
- Cell edits are saved to config as soon as you leave the cell.
- REMOVE deletes the selected row. Leaders added while the engine runs start
  polling immediately; removed/disabled ones stop at their next tick.

**START / STOP** (top right) starts per-leader poll loops and the price
tracker. First START with zero leaders just tells you to add one.

**Activity feed** shows every leader trade the engine saw and what it did:
`copy buy 0.0500 SOL`, `copy sell ... +0.0123` (realized PnL), or the skip
reason (below min SOL, max positions, skip list, no bag to sell, etc.).

**Open positions** table updates on every price tick: quantity, value in SOL,
PnL %. Closed positions drop off; their result stays in REALIZED.

**Stats strip**: balance, equity (balance + open position value), unrealized,
realized, copied buys/sells, skipped/seen leader trades, win rate.

## Trading tab

- **Size mode**: `Fixed` — every copy buy is `Fixed size SOL`.
  `Proportional` — leader's SOL × leader multiplier, capped by
  `Max our SOL per trade`.
- **Min leader SOL** gates buys only. Sells are always mirrored: a leader
  exiting in dust-sized chunks still pulls you out.
- **Max open positions** counts distinct open mints; adding to an existing
  position is always allowed.
- **Skip mints**: one mint per line, exact match.
- **Copy delay**: random pause between min and max before each fill — spreads
  your entries and looks less like a bot in the feed.
- **Slippage / fee** feed the paper broker: buys fill at `price × (1 + slip)`,
  sells at `price × (1 − slip)`, fee is taken in SOL on top.
- SAVE TRADING SETTINGS persists to `config.json`. Most values are also read
  live by the engine between polls.
- RESET SESSION: balance back to configured, positions/fills/stats cleared.

## Connection tab

- **Base URL**: `https://gmgn.ai` unless you have a mirror.
- **Proxy**: full `http://user:pass@host:port` URL (http/https only, socks5 is
  not supported). Credentials are masked in logs.
- **Polling**: leader activity interval (staggered per leader) and position
  price interval. Applied on next START.
- **TEST CONNECTION** hits the rank endpoint and prints what came back. A
  `cloudflare challenge` result is expected from datacenter IPs on many
  endpoints — it does not mean the app is broken, the engine degrades
  gracefully and Offline feed keeps everything alive.
- **Offline feed** checkbox swaps the data source between the offline generator
  and the live client. Toggling it rebuilds the engine — the paper session
  resets.

## Wallet tab

- **Paper balance**: the simulated deposit. APPLY & RESET SESSION sets it and
  clears the session.
- **Live wallet** is a disabled stub: GMGN exposes no public trading API, so
  there is nothing to sign against. `ITradeExecutor` is the seam where a live
  broker would plug in later. An imported key would stay in `config.json` on
  this machine and never leave it — but again, never put a key you care about
  into any bot.

## Logs tab

Live tail of the same stream that goes to `logs/`. Level filter, autoscroll,
CLEAR VIEW (view only — files are untouched). Every gmgn request is logged
with URL, status, and a truncated body; broker fills and skip reasons are all
there. When something looks wrong, this is the first place to look.

## config.json reference

```jsonc
{
  "version": 1,
  "connection": {
    "baseUrl": "https://gmgn.ai",
    "proxy": "",
    "activityPollSec": 5,     // leader activity polling
    "pricePollSec": 5,        // open position marking
    "requestTimeoutSec": 10,
    "demoMode": true          // offline synthetic feed
  },
  "trading": {
    "paperMode": true,        // always true in v0.1
    "sizeMode": "Fixed",      // Fixed | Proportional
    "fixedSizeSol": 0.05,
    "slippagePercent": 10,
    "feePercent": 1,
    "minLeaderSol": 0.5,      // buys only
    "maxOurSolPerTrade": 0.5,
    "maxOpenPositions": 10,
    "delayMinMs": 300,
    "delayMaxMs": 2500,
    "skipMints": []
  },
  "wallet": {
    "paperBalanceSol": 10,
    "privateKey": ""          // stub, unused
  },
  "leaders": [
    {
      "address": "…",
      "alias": "whale1",
      "multiplier": 0.5,
      "minSol": 0,            // 0 = global minLeaderSol
      "enabled": true
    }
  ]
}
```

A corrupt `config.json` is renamed to `config.json.bad` and replaced with
defaults on next start — the app never dies on a bad config.
