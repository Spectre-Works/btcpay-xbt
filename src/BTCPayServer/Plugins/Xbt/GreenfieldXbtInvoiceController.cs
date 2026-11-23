#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.Models;

namespace BTCPayServer.Plugins.Xbt;

[ApiController]
public sealed class GreenfieldXbtInvoiceController(ExplorerClientProvider explorers, PaymentMethodHandlerDictionary handlers) : Controller
{
    private static readonly PaymentMethodId XbtOnChain = PaymentTypes.CHAIN.GetPaymentMethodId("XBT");

    [Authorize(Policy = Policies.CanViewInvoices, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpGet("~/api/v1/stores/{storeId}/invoices/{invoiceId}/xbt-chain")]
    public async Task<IActionResult> GetObservations(string storeId, string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = HttpContext.GetInvoiceDataOrNull();
        if (invoice is null || invoice.Id != invoiceId || invoice.StoreId != storeId ||
            invoice.GetPaymentPrompt(XbtOnChain) is not { Activated: true } prompt)
            return NotFound();

        var network = explorers.GetNetwork("XBT");
        var client = explorers.GetExplorerClient("XBT");
        if (network is null || network.NBitcoinNetwork.ChainName != ChainName.Mainnet || client is null ||
            handlers.TryGet(XbtOnChain) is not BitcoinLikePaymentHandler handler ||
            handler.ParsePaymentPromptDetails(prompt.Details) is not { AccountDerivation: { } derivation })
            return StatusCode(503);

        var status = await client.GetStatusAsync(cancellationToken);
        if (!IsCurrent(status))
            return StatusCode(503);

        // Search the originally tracked account, including after BTCPay stops
        // monitoring this invoice. Return evidence only for this invoice's script.
        var address = BitcoinAddress.Create(prompt.Destination, network.NBitcoinNetwork);
        var history = await client.GetTransactionsAsync(derivation, from: invoice.InvoiceTime.AddMinutes(-2), cancellation: cancellationToken);
        var after = await client.GetStatusAsync(cancellationToken);
        if (!IsCurrent(after) || history.Height != after.ChainHeight)
            return StatusCode(503);

        var outputs = history.ConfirmedTransactions.Transactions
            .Concat(history.UnconfirmedTransactions.Transactions)
            .Concat(history.ImmatureTransactions.Transactions)
            .SelectMany(tx => tx.Outputs.Where(output => output.ScriptPubKey == address.ScriptPubKey && output.Value is Money)
                .Select(output => new
                {
                    outpoint = $"{tx.TransactionId}:{output.Index}",
                    units = ((Money)output.Value).Satoshi,
                    confirmations = tx.Confirmations,
                    receivedAt = tx.Timestamp.ToUnixTimeSeconds()
                }))
            .DistinctBy(output => output.outpoint)
            .ToArray();
        return Ok(new { height = after.ChainHeight, outputs });
    }

    private static bool IsCurrent(StatusResult status) =>
        status.CryptoCode == "XBT" && status.NetworkType == ChainName.Mainnet &&
        status.IsFullySynched && status.ChainHeight > 0 && status.SyncHeight == status.ChainHeight &&
        status.BitcoinStatus is { IsSynched: true } node && node.Blocks == status.ChainHeight && node.Headers == node.Blocks;
}
