namespace Playloud.Domain.Common;

public readonly record struct BusinessDate(DateOnly Value)
{
    public static BusinessDate FromInstant(DateTimeOffset instant, TimeSpan businessOffset)
    {
        if (businessOffset < TimeSpan.FromHours(-14) || businessOffset > TimeSpan.FromHours(14))
        {
            throw new ArgumentOutOfRangeException(nameof(businessOffset), "Business offset must be between UTC-14 and UTC+14.");
        }

        var local = instant.ToOffset(businessOffset);
        return new BusinessDate(DateOnly.FromDateTime(local.DateTime));
    }
}
