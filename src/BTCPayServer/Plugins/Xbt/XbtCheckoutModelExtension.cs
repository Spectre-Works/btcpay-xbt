using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;

namespace BTCPayServer.Plugins.Xbt;

public sealed class XbtCheckoutModelExtension(BitcoinCheckoutModelExtension bitcoin) : ICheckoutModelExtension
{
    public const string BodyComponent = "XbtCheckoutBody";

    public PaymentMethodId PaymentMethodId => bitcoin.PaymentMethodId;
    public string Image => bitcoin.Image;
    public string Badge => bitcoin.Badge;

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        bitcoin.ModifyCheckoutModel(context);
        if (context.Model.CheckoutBodyComponentName == BitcoinCheckoutModelExtension.CheckoutBodyComponentName)
            context.Model.CheckoutBodyComponentName = BodyComponent;
    }
}
