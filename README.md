# gmgn-terminal

solana memecoin copytrade terminal. gmgn.ai data, .NET 8 + WPF, windows only.

copies leader wallets: their buys and sells become yours, paper wallet for now.
been doing this by hand from gmgn trenches for months, misclicks cost more
than fees ever did.

## warning

copytrading memecoins can nuke your bag if the wallet rugs. paper first,
small fresh wallet later, never your main key.

## run

    cd src
    dotnet run --project GmgnTerminal.App

config.json appears next to the exe on first start, seeds two wallets from
gmgn 7d profit rank. press START, watch the feed.


## status

early. tabs and paper broker work. live gmgn data is half-blocked by
cloudflare from datacenter ips, offline feed covers that.