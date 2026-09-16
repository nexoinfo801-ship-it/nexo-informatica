namespace Playloud.Domain.Sales;

public readonly record struct DiscountPercent
{
    private DiscountPercent(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static DiscountPercent Create(decimal value)
    {
        if (value < 0m || value > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Discount must be between 0 and 100 percent.");
        }

        return new DiscountPercent(value);
    }

    public decimal ApplyTo(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        return amount * (1m - (Value / 100m));
    }
}
