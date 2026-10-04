# XBT on-chain receiving for BTCPay Server

Accept Bitcoin BLAKE2b (**XBT/BTCB2**) on-chain with BTCPay Server using a watch-only merchant wallet and your own XBT Knots node.

This build is deliberately narrow:

- Mainnet only.
- On-chain receiving only; no Lightning.
- No server-side spending, payouts, or spending refunds from the BTCPay wallet.
- No private keys stored by BTCPay Server or NBXplorer.
- No PayJoin or hardware-wallet signing.

It is **not** a drop-in `.btcpay` plugin for stock BTCPay Server. XBT uses an 80-byte header before block 961640 and a 164-byte header-v2 with BLAKE2b block IDs from block 961640. The included NBXplorer and client patches are therefore required.

## Payment flow

```text
Customer creates order in USD
        ↓
BTCPay gets XBT/USD from NeoxEX XBT/USDC × Kraken USDC/USD
        ↓
BTCPay creates an XBT on-chain invoice
        ↓
fresh address is derived from the merchant account public key
        ↓
customer broadcasts the XBT payment
        ↓
NBXplorer detects the transaction in the mempool
        ↓
BTCPay shows Processing
        ↓
required confirmation count is reached
        ↓
BTCPay shows Settled
```

Merchant invoice denominations include USD, XBT, BTCB2 (a 1:1 pricing alias for XBT), and XBTSATS (exactly 100,000,000 per XBT). USDC is only an internal pricing bridge. There is no BTC pricing fallback.

## Requirements

- Docker Compose for deployment and Docker for the build.
- A synced, externally operated XBT Knots mainnet node reachable by authenticated RPC and P2P. A SHA-256 BTC backend is rejected at the fork checkpoint.
- A dedicated XBT merchant account public key/xpub backed up in compatible external wallet software.
- PostgreSQL, supplied by the included Compose stack.

## Build

```sh
sh scripts/build.sh
```

The build fetches pinned BTCPay Server and NBXplorer revisions into `.build`, applies the patches, runs the focused XBT tests, packages the custom NBXplorer client, and builds two images:

- `paperclip-btcpay:xbt-beta`
- `paperclip-nbxplorer:xbt-beta`

Use `sh scripts/build.sh --test-only` to stop after the test/package phase.

## Configure and run

```sh
cp .env.example .env
```

Set a strong PostgreSQL password and the RPC/P2P details for the external XBT node.

Before the first start, create the bind-mount directories with ownership matching the application containers:

```sh
mkdir -p data/btcpay data/nbxplorer
sudo chown -R 1000:1000 data/btcpay data/nbxplorer
docker compose up -d
```

BTCPay is published at `127.0.0.1:23000`. PostgreSQL and NBXplorer are not published to the host.

In BTCPay, create a store, open **Integrations → XBT payments**, enable on-chain XBT, and connect the dedicated wallet using its account public key. Verify the first derived receiving address against the same derivation in known-compatible XBT wallet or Knots tooling before accepting funds.

## Security boundary

BTCPay and NBXplorer remain watch-only for XBT. The integration refuses hot-wallet/private-key generation and NBXplorer private-key storage/import. It registers no XBT payout handler and no XBT Lightning payment method. External XBT-compatible wallet software is responsible for signing and spending, including any merchant refund.

The backend identity check reads block 961640 and requires:

```text
0000000000000050c1e5f69672f459293be14f46e5a494e7a8c8541396f18eeb
```

## Repository layout

- `src/`: XBT BTCPay plugin and focused tests copied into the pinned source tree.
- `patches/nbxplorer.patch`: XBT header serialization/hashing, chain identity, watch-only restrictions, and tests.
- `patches/btcpay.patch`: BTCPay integration and receive-only safety changes.
- `scripts/prepare.py`: reproducible pinned-source preparation.
- `scripts/build.sh`: tests, package creation, and the two application images.
- `docs/REVIEW.md`: validation record and remaining acceptance work.
