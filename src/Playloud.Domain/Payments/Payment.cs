namespace Playloud.Domain.Payments;

public enum PaymentMethod
{
    Cash = 1,
    Pix = 2,
    DebitCard = 3,
    CreditCard = 4,
    Other = 5
}

public sealed record Payment
{
    private Payment(PaymentMethod method, decimal amount)
    {
        Method = method;
        Amount = amount;
    }

    public PaymentMethod Method { get; }

    public decimal Amount { get; }

    public static Payment Create(PaymentMethod method, decimal amount)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), "Unsupported payment method.");
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        }

        return new Payment(method, amount);
    }
}
