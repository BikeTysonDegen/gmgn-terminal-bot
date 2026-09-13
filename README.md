# gmgn-terminal

solana memecoin terminal. copytrade whales, snipe launches, watch positions
from one window. data from gmgn.ai. .NET 8, WPF, windows only.

copied wallets by hand from gmgn for months. misclicks cost me more than
fees ever did. so now the terminal clicks for me.

## read this before anything else

copytrading memecoins rugs hard. one bad "smart money" wallet and the bot
copies the rug faithfully. paper wallet by default. when (if) you go live:
fresh wallet, small balance, never your main key. not financial advice,
not even close.

## install (release build)

1. download gmgn-terminal-win-x64.zip from Releases, unpack wherever.
2. run gmgn-terminal.exe. first start drops config.json next to it and
   seeds two wallets from gmgn 7d profit rank.
3. START on Main tab. feed moves, paper pnl counts. done.

self contained, no runtime install.

## install (build yourself)

    git clone https://github.com/BikeTysonDegen/gmgn-terminal-bot.git
    cd gmgn-terminal-bot/src
    dotnet run --project GmgnTerminal.App

needs .NET 8 SDK and windows 10/11 x64.

## what it does

- polls leader wallets activity on gmgn, copies real fills. buys and sells
- buy size: fixed SOL, or leader size x multiplier per wallet
- sells proportional to your bag. leader trims a third, you trim a third.
  leader dumps everything, you close
- filters: min leader sol, max our sol, max open positions, skip mints,
  random delay so you dont land in the same block as fifty other copiers
- paper broker: slippage + fee sim, realized/unrealized pnl, session stats
- offline feed: half of gmgn endpoints 403 plain http clients (cloudflare
  tls fingerprint). offline mode replays real mints locally so you can test
  the whole loop without network
- logs everything to a file next to exe, plus a log tab in the app

## tabs

Main leaders/feed/positions/stats, Trading sizing and filters, Connection
endpoint and proxy, Wallet paper balance, Logs tail.

## picking leaders

gmgn rank, smart money lists, top traders on tokens that actually ran.
early entries, staged sells, holds over 30s, 50%+ winrate over 20 trades.
one-tx buy-and-dump wallets are bundlers, skip.

## known issues

- wallet activity 403s from datacenter ips. residential proxy or offline feed
- leader bag tracked from engine start only. sells on older bags = full exit
- wpf datagrid eats first click sometimes. wpf being wpf

MIT.
