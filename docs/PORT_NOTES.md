# Historical XBT on-chain port notes

This is the original implementation brief for the on-chain port. See the
[README](../README.md) for current setup and supported behavior.

## Objective

Continue development from our fork of `connorslab/paperclip-btcpay-xbt`.

Convert the current integration into a focused **XBT on-chain receiving port for BTCPay Server**.

The initial supported product is:

> BTCPay Server → XBT plugin → NBXplorer → Bitcoin Knots/XBT mainnet node

Customers must be able to receive normal BTCPay invoices priced in USD and pay them on-chain with XBT.

Do not support Lightning in this phase.

Do not implement server-side XBT spending in this phase.

Do not redesign the existing XBT block-header/indexing implementation unless tests demonstrate that it is incorrect.

## Supported scope

The finished implementation should support:

- XBT mainnet only.
- On-chain XBT checkout.
- Per-invoice receiving addresses.
- Watch-only wallets imported by account public key/xpub.
- Mempool payment detection.
- Confirmation tracking.
- Invoice settlement after configured confirmations.
- USD-priced merchant invoices converted to XBT.
- XBT as an invoice denomination.
- BTCB2 as a 1:1 pricing alias for XBT.
- XBTSATS as an exact 100,000,000-sats-per-XBT pricing unit.
- Existing XBT 80-byte pre-fork and 164-byte v2 block headers.
- The mainnet fork boundary at block 961640.
- Fail-closed behavior if an XBT backend is actually a BTC/SHA-256 backend.

The implementation must remain receive-only inside BTCPay.

## Explicit non-goals

Do not implement or expose:

- Core Lightning.
- LND.
- BOLT11.
- BOLT12.
- Lightning payment handlers.
- Lightning settings.
- Lightning Docker services.
- Lightning helper scripts.
- Lightning feature-bit detection.
- Server-side XBT transaction signing.
- `SIGHASH_UNIFIED`.
- BTCPay payouts in XBT.
- BTCPay refunds that construct/spend XBT transactions.
- PayJoin.
- Hot-wallet private-key storage.
- Hardware-wallet signing.
- XBT testnet or regtest support in this phase.
- BTC pricing fallback for XBT.

External XBT-compatible wallet software remains responsible for spending funds.

# 1. Preserve the working XBT chain implementation

Keep the important portions of `patches/nbxplorer.patch`.

In particular, preserve:

- `NBXplorer.Client/XbtNetwork.cs`
- `XbtConsensusFactory`
- `XbtBlock`
- `XbtBlockHeader`
- 80-byte legacy-header parsing
- 164-byte header-v2 parsing
- the BLAKE2b/header-v2 block-ID implementation
- `NBXplorerNetworkProvider.Xbt.cs`
- the block 961640 chain-identity check in `RPCArgs.cs`
- NBXplorer rejection of XBT private-key storage/import
- the current real-chain block/header fixtures

Do not replace this with a full NBitcoin fork merely for architectural purity.

The current design intentionally uses Knots as the trusted consensus authority while teaching NBitcoin/NBXplorer enough about XBT serialization and block IDs to index the chain.

That is acceptable for the receive-only BTCPay integration.

# 2. Make XBT explicitly mainnet-only

The current `XbtNetwork` incorrectly derives `CreateTestnet()` from Bitcoin testnet3.

Do not ship a fake or misleading XBT testnet.

Change `XbtNetwork` so only mainnet is constructed.

Conceptually:

```csharp
protected override NetworkBuilder CreateMainnet()
    => Create(Network.Main, "xbt-main");

protected override NetworkBuilder CreateTestnet()
    => null;

protected override NetworkBuilder CreateRegtest()
    => null;
```

Also change `NBXplorerNetworkProvider.InitXbt()` so XBT is registered only when:

```csharp
networkType == ChainName.Mainnet
```

For any other chain, do not add the XBT network.

Review the BTCPay change in:

```text
BTCPayServer/Configuration/DefaultConfiguration.cs
```

The current patch adds XBT unconditionally to `SelectedChains` in its helper.

Only inject/execute the XBT network there for `ChainName.Mainnet`.

Non-mainnet BTCPay/NBXplorer configurations must not accidentally construct a Bitcoin testnet3 network and call it XBT.

Add tests proving:

```text
XBT mainnet exists
XBT testnet is unavailable
XBT regtest is unavailable
NBXplorer mainnet provider contains XBT
NBXplorer testnet provider does not contain XBT
```

# 3. Remove Lightning from the XBT BTCPay plugin

Edit:

```text
src/BTCPayServer/Plugins/Xbt/XbtPlugin.cs
```

Remove:

```csharp
using BTCPayServer.Payments.Lightning;
```

Change:

```csharp
SupportLightning = true
```

to:

```csharp
SupportLightning = false
```

Remove `LightningImagePath` unless BTCPay requires it to be populated despite Lightning being disabled.

Delete all registration of:

```csharp
PaymentTypes.LN.GetPaymentMethodId("XBT")
LightningLikePaymentHandler
LightningPaymentLinkExtension
LNCheckoutModelExtension
LightningPaymentMethodBitpayAPIExtension
```

The plugin should register only `XBT-CHAIN`.

Keep:

```csharp
WalletSupported = true
ReadonlyWallet = true
SupportLightning = false
SupportPayJoin = false
SupportRBF = false
VaultSupported = false
```

Do not register an XBT payout handler.

Do not make XBT writable merely because normal Bitcoin BTCPay functionality expects a wallet.

Update the plugin metadata to describe what it actually does.

For example:

```csharp
public override string Name => "XBT";
public override string Description =>
    "Bitcoin BLAKE2b on-chain receiving. Watch-only; server-side spending is not supported.";
```

Do not rename the plugin `Identifier` in this pass unless there is a strong technical reason. Identifier changes can create avoidable plugin-state/migration concerns.

# 4. Simplify the XBT settings UI to on-chain only

Edit:

```text
src/BTCPayServer/Plugins/Xbt/UIXbtController.cs
src/BTCPayServer/Plugins/Xbt/Views/Settings.cshtml
```

Remove `LightningEnabled` entirely.

`XbtSettings` should contain only:

```csharp
public bool OnChainEnabled { get; set; }
```

`From()` should read only:

```csharp
PaymentTypes.CHAIN.GetPaymentMethodId("XBT")
```

`Apply()` should modify only that payment method.

The settings UI should have one switch:

```text
On-chain XBT
```

Keep the watch-only explanation and existing-wallet import link.

Make the limitations explicit without overloading the page:

```text
Receive payments directly into a watch-only XBT wallet. Each invoice receives a fresh address and confirmations are tracked by your XBT node. Spending keys remain in your external XBT-compatible wallet.
```

Remove all Lightning descriptions, switches and node references.

# 5. Fix USD → XBT pricing

This is required before using the integration for a normal USD-priced storefront.

The current rule:

```csharp
"XBT_X = neoxex(XBT_X);"
```

is incorrect as a generic rule because NeoxEX supplies the actual market as:

```text
BTCB2_USDC
```

which the provider correctly exposes internally as:

```text
XBT_USDC
```

Do not ask NeoxEX for `XBT_USD`.

Do not silently treat USDC as USD.

Use an explicit two-market conversion:

```text
XBT → USDC through NeoxEX
USDC → USD through BTCPay's existing Kraken provider
```

Change `XbtPlugin.RateRules` to approximately:

```csharp
public static readonly string[] RateRules =
{
    "XBT_BTCB2 = 1;",
    "BTCB2_XBT = 1;",

    "XBT_XBTSATS = 100000000;",

    "XBT_USDC = neoxex(XBT_USDC);",
    "XBT_USD = XBT_USDC * kraken(USDC_USD);",

    "BTCB2_X = BTCB2_XBT * XBT_X;",
    "XBTSATS_X = XBTSATS_XBT * XBT_X;"
};
```

Verify the exact BTCPay `RateRules` syntax against the pinned BTCPay 2.4.4 source rather than assuming this snippet blindly.

The important behavior is:

```text
USD invoice
    ↓
XBT_USD
    ↓
NeoxEX XBT/USDC
    ×
Kraken USDC/USD
    ↓
XBT amount due
```

There must be no path through `BTC_USD`.

Add unit tests with injected/mock rate values.

For example, with:

```text
NeoxEX XBT_USDC = 400
Kraken USDC_USD = 0.999 / 1.001
```

the rule must successfully produce an XBT/USD bid/ask without querying a BTC market.

Add an explicit regression test that `XBT_BTC` cannot silently obtain the BTC/XBT alias behavior that upstream BTCPay historically used.

Keep the existing BTCPay patch that removes:

```csharp
if (cryptoCode == "XBT")
    return GetNetwork<T>("BTC");
```

A missing XBT network must continue returning `null`, never BTC.

# 6. Keep NeoxEX as an XBT/USDC source only

Keep:

```text
src/BTCPayServer/Plugins/Xbt/NeoxExRateProvider.cs
```

Keep its important validation:

- API success must be true.
- returned pair must be `BTCB2_USDC`.
- stale quotes are rejected.
- significantly future-dated quotes are rejected.
- zero or negative prices are rejected.
- output currency pair is `XBT_USDC`.

Do not modify the provider to return `XBT_USD`.

USD conversion belongs in BTCPay rate rules using an independent USDC/USD source.

This makes the pricing provenance explicit.

# 7. Do not expose USDC as a merchant invoice currency unless needed

USDC is currently being registered and explicitly added to the currency dropdown.

For this initial XBT port, USDC is an internal pricing bridge, not one of the intended merchant denomination options.

Prefer exposing:

```text
USD
XBT
BTCB2
XBTSATS
```

Do not add USDC to the custom currency dropdown merely because it participates in the rate calculation.

Review:

```text
BTCPayServer/TagHelpers/CurrenciesSuggestionsTagHelper.cs
```

and remove `USDC` from the plugin-specific appended currencies.

Update the existing currency-dropdown test accordingly.

If `CurrencyData` registration for USDC is unnecessary for rate evaluation, remove that registration as well.

Do not break the ability of `CurrencyPair("USDC", "USD")` to be queried from Kraken.

# 8. Preserve all receive-only protections

The following safety boundaries are requirements, not temporary hacks.

Keep the BTCPay network:

```csharp
ReadonlyWallet = true
```

Keep PayJoin disabled:

```csharp
SupportPayJoin = false
```

Keep Vault/hardware signing disabled:

```csharp
VaultSupported = false
```

Keep the BTCPay hot-wallet creation restriction for XBT.

Keep the Greenfield API check preventing private-key wallet generation for read-only networks.

Keep the NBXplorer check rejecting:

```text
SavePrivateKeys = true
ImportKeysToRPC = true
```

for XBT.

Add regression coverage showing that an XBT configuration cannot:

- create a server-side hot wallet containing private keys;
- ask NBXplorer to save private keys;
- import private keys into the RPC node;
- access BTCPay's normal XBT send surface;
- register an XBT chain payout handler;
- register PayJoin;
- create an XBT Lightning payment method.

Do not implement `SIGHASH_UNIFIED` merely to make any of these features work.

That is a separate future project.

# 9. Strengthen XBT header-v2 testing

The current real-chain fixtures are useful and must remain:

```text
height 961639 — legacy 80-byte header
height 961640 — first XBT v2 header
height 975000
height 975200
```

They prove the real mainnet boundary and current-style serialization.

However, the current v2 fixtures mostly contain zero/default values for the newer header fields.

Add the canonical synthetic header-v2 vectors from the Knots header-v2 test data.

Create something like:

```text
NBXplorer.Tests/Data/xbt-header-v2-vectors.json
```

Cover at least these upstream vector cases:

```text
profile_0_time_offset
profile_1_time_offset_nonzero_key
profile_2_time_offset_selector_7
profile_3_time_offset_selector_8
profile_0_time_offset_disabled_selector_255
```

These vectors exercise:

- all four ASIC profiles;
- nonzero `time_offset`;
- nonzero XOR keys;
- XOR mask behavior;
- mask-clear selectors;
- merge-mining RHS;
- nonce2;
- nonce3;
- extranonce;
- tx count;
- flags.

For every vector assert:

```text
deserialize bytes
serialize back to identical bytes
GetHash() equals canonical Knots block_hash
```

Also add one P2P/header payload regression test containing:

```text
block 961639 header
immediately followed by
block 961640 header
```

in the same header stream.

The parser must consume exactly:

```text
80 bytes
then
164 bytes
```

without misalignment.

This verifies the actual transition behavior rather than only deserializing individual headers.

# 10. Keep the full-block fork-boundary test

Preserve:

```text
NBXplorer.Tests/Data/xbt-block.hex
```

and the test that verifies:

```text
Block.Load(...)
GetHash()
CheckMerkleRoot()
round-trip serialization
```

for block 961640.

This is an important integration test because it demonstrates that the custom header and ordinary Bitcoin-like transaction parsing coexist correctly in the same XBT block.

# 11. Strip Lightning from Docker deployment

Edit:

```text
docker-compose.yml
```

Remove the entire:

```yaml
gateway:
```

service.

Remove from `web.depends_on`:

```yaml
gateway
```

Remove:

```yaml
BTCPAY_XBTLIGHTNING
```

Remove the gateway volume from the web container.

Remove:

```yaml
gateway-rpc
```

from top-level volumes.

Remove web-container `extra_hosts` if it is only present for Lightning/LND.

Do not remove `extra_hosts` from NBXplorer if it is required for:

```text
host.docker.internal
```

to reach the user's Knots RPC/P2P endpoints.

The resulting deployment should contain only the services required by this repository:

```text
web
nbxplorer
postgres
```

The XBT full node remains externally operated and is reached over the configured RPC/P2P endpoints.

# 12. Simplify `.env.example`

Remove:

```text
COMPOSE_PROFILES
XBT_LIGHTNING
CLN_SOCKET
CLN_NODE_ID
CLN_RPC_GID
CLN_ADVERTISE_HOST
CLN_ADVERTISE_PORT
```

Keep only configuration relevant to on-chain operation, approximately:

```text
POSTGRES_PASSWORD=

XBT_RPC_URL=http://host.docker.internal:9332/
XBT_RPC_USER=
XBT_RPC_PASSWORD=
XBT_P2P_ENDPOINT=host.docker.internal:9333
```

Do not include working credentials.

# 13. Remove Lightning-only source

Delete the Lightning-specific implementation from our fork:

```text
gateway/
scripts/lnd-connection.sh
scripts/test_lnd_connection.py
```

Also search the repository for:

```text
Lightning
lightning
CLN
cln
LND
lnd
BOLT11
BOLT12
gateway
XBT_LIGHTNING
PaymentTypes.LN
```

Classify every occurrence.

Remove occurrences that belong to the XBT integration's functionality, tests, deployment or documentation.

Do not modify unrelated upstream BTCPay Lightning code inside prepared source. The goal is to remove XBT Lightning support, not to remove Lightning from BTCPay Server generally.

# 14. Simplify the build

Edit:

```text
scripts/build.sh
```

Remove:

```sh
python3 scripts/test_lnd_connection.py
python3 -m unittest discover -s gateway -p test_gateway.py
```

Remove:

```sh
docker build -t paperclip-cln-gateway:xbt-beta gateway
```

Keep:

```text
prepare pinned upstream source
NBXplorer XBT tests
pack custom NBXplorer.Client
BTCPay XBT tests
build NBXplorer image
build BTCPay image
```

The normal build should produce two application images, not three:

```text
BTCPay XBT image
NBXplorer XBT image
```

Image renaming/rebranding is not necessary for this functional pass unless required to prevent collisions with an existing local deployment.

# 15. Update CI

`.github/workflows/build.yml` can continue invoking:

```sh
sh scripts/build.sh
```

The build script itself should now exercise only the on-chain port.

CI must fail on:

- patch application errors;
- NBXplorer XBT test failures;
- BTCPay XBT test failures;
- custom NBXplorer package build failure;
- Docker image build failure.

Do not leave stale CI references to gateway or Lightning tests.

# 16. Rewrite the README around the actual supported product

The README should begin with something equivalent to:

> Accept Bitcoin BLAKE2b (XBT/BTCB2) on-chain with BTCPay Server using a watch-only merchant wallet and your own XBT Knots node.

State clearly:

```text
Mainnet only.
On-chain receiving only.
No Lightning.
No server-side spending.
No payouts/refunds from the BTCPay wallet.
No private keys stored by BTCPay/NBXplorer.
```

Document the flow:

```text
Customer creates order in USD
        ↓
BTCPay gets XBT/USD rate
        ↓
BTCPay creates XBT invoice
        ↓
fresh address derived from merchant xpub
        ↓
customer broadcasts XBT payment
        ↓
NBXplorer detects transaction
        ↓
BTCPay shows Processing
        ↓
required confirmation count reached
        ↓
BTCPay shows Settled
```

Remove all CLN/LND setup and compatibility documentation.

Preserve the warning that this is not just a `.btcpay` plugin that can be dropped into stock BTCPay: the NBXplorer/client changes are required because XBT has its own post-fork block-header format and identity.

# 17. Update `docs/REVIEW.md`

Retain useful historical statements about the XBT on-chain validation.

Remove or clearly archive material that describes Lightning as part of the current supported build.

The active review should cover:

- fork-boundary parsing;
- real block parsing;
- XBT backend identity verification;
- watch-only restrictions;
- XBT/BTC alias protection;
- USD rate conversion;
- actual on-chain invoice lifecycle;
- remaining lack of unified-signature spending;
- remaining lack of reorg testing if still untested.

Do not claim a feature is supported merely because it existed in the source before this fork was narrowed.

# 18. BTCPay test requirements

Replace the existing independent on-chain/Lightning toggle matrix with on-chain-only tests.

At minimum test:

### Network identity

```text
BTC resolves to BTC
XBT resolves only if actual XBT network is registered
missing XBT never resolves to BTC
lowercase xbt never falls back to BTC
```

### Payment-method registration

With XBT selected:

```text
XBT-CHAIN exists
XBT-LN does not exist
SupportLightning == false
ReadonlyWallet == true
SupportPayJoin == false
```

### Settings

```text
OnChainEnabled=true enables only XBT-CHAIN
OnChainEnabled=false disables only XBT-CHAIN
unrelated BTC settings are untouched
```

### Pricing

Test:

```text
XBT_BTCB2 = 1
BTCB2_XBT = 1
XBT_XBTSATS = 100000000
XBTSATS_XBT = 0.00000001
```

Test a complete mocked:

```text
XBT_USDC
×
USDC_USD
=
XBT_USD
```

Test stale/wrong/zero NeoxEX responses.

Test that no BTC market is used to price XBT.

### Wallet restrictions

Test that private-key/hot-wallet creation for XBT is refused.

Test that no XBT payout handler is registered.

# 19. NBXplorer test requirements

Keep all existing XBT tests and add:

```text
mainnet-only registration
full Knots v2 synthetic vector suite
80→164-byte mixed header stream
backend chain-identity rejection
private-key storage rejection
```

The block 961640 hash remains a critical invariant:

```text
0000000000000050c1e5f69672f459293be14f46e5a494e7a8c8541396f18eeb
```

A SHA-256 BTC backend must fail the XBT startup check.

# 20. Manual mainnet acceptance test

After automated tests pass, perform a real small-value mainnet test against an XBT Knots node.

Start with a fresh isolated BTCPay database.

Configure only:

```text
BTCPAY_CHAINS=XBT
XBT RPC
XBT P2P
PostgreSQL
```

Import a dedicated XBT merchant account public key.

Verify that a derived receiving address matches the same derivation in known-compatible XBT wallet/Knots tooling.

Create a USD-priced invoice.

Verify:

```text
invoice offers XBT on-chain
invoice does not offer XBT Lightning
XBT amount is based on NeoxEX XBT/USDC × Kraken USDC/USD
address belongs to the imported merchant xpub
QR/payment URI contains the expected XBT address and amount
```

Send a very small XBT payment.

Verify:

```text
mempool detection
Processing state
confirmation increment
Settled state at required confirmations
correct txid
correct amount
```

Then verify the server has no available XBT send/payout/hot-wallet path.

# 21. Definition of done

Do not consider this work complete merely because it compiles.

The on-chain port is complete when all of the following are true:

```text
[ ] Lightning-specific XBT code is absent from the supported build.
[ ] Docker contains no CLN/LND/gateway service.
[ ] XBT plugin registers only XBT-CHAIN.
[ ] SupportLightning is false.
[ ] XBT remains watch-only.
[ ] XBT private keys cannot be generated/stored by the server.
[ ] XBT payouts are not registered.
[ ] PayJoin is disabled.
[ ] XBT is mainnet-only.
[ ] BTC can never satisfy a missing XBT network request.
[ ] The node identity check rejects SHA-256 BTC.
[ ] Block 961639 parses as an 80-byte header.
[ ] Block 961640 parses as a 164-byte header.
[ ] Block 961640 hashes to the canonical XBT hash.
[ ] Canonical Knots header-v2 vectors pass.
[ ] Mixed 80/164-byte header-stream parsing passes.
[ ] USD invoices obtain an XBT price without using BTC.
[ ] XBT/BTCB2/XBTSATS exact conversions pass.
[ ] Clean CI build passes.
[ ] Both Docker images build.
[ ] A real small on-chain mainnet invoice is detected and settles.
```

# Important implementation constraints

Do not broaden the task into a rewrite.

Preserve working code where possible.

Prefer deleting unsupported Lightning surface area over leaving it dormant and configurable.

Fail closed whenever chain identity, rates or wallet capabilities are ambiguous.

Do not substitute BTC behavior for missing XBT behavior.

Do not implement transaction signing as a shortcut.

Do not weaken the watch-only boundary to make upstream BTCPay wallet APIs happier.

If a normal BTCPay component assumes writable Bitcoin-like wallets, adapt or disable that component for XBT rather than pretending XBT has Bitcoin-compatible signing.

Run the focused test suite after each logical group of changes and finish with a clean build from an empty `.build` directory.
