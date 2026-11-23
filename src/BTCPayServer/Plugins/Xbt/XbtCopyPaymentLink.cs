#nullable enable
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Xbt;

public sealed class XbtCopyPaymentLink(PaymentMethodId paymentMethodId) : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId => paymentMethodId;

    // XBT and SHA-256 BTC share address encodings and the bitcoin URI scheme.
    // Suppress the URI in both hosted checkout and API payment-method responses.
    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper) => null;
}
