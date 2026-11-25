namespace ClaimsApi.Domain;

public static class Markets
{
    public static string CurrencyFor(Market market) => market switch
    {
        Market.SG => "SGD",
        Market.MY => "MYR",
        Market.HK => "HKD",
        Market.TH => "THB",
        Market.ID => "IDR",
        Market.AU => "AUD",
        _ => throw new ArgumentOutOfRangeException(nameof(market), market, "Unknown market"),
    };
}
