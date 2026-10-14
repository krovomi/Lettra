using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>
/// Taux de TVA identifié par un code stable (« FR-20 », « FR-5.5 »…), partagé avec l'UI via les contrats.
/// Les exonérations et l'autoliquidation (taux 0 avec mention légale obligatoire) arrivent avec
/// la facturation (phase 3) et seront relues par l'expert-comptable référent.
/// </summary>
public sealed record VatRate
{
    public static readonly VatRate Normal = new("FR-20", 20m);
    public static readonly VatRate Intermediate = new("FR-10", 10m);
    public static readonly VatRate Reduced = new("FR-5.5", 5.5m);
    public static readonly VatRate SuperReduced = new("FR-2.1", 2.1m);
    public static readonly VatRate Zero = new("FR-0", 0m);

    private static readonly IReadOnlyDictionary<string, VatRate> Known =
        new[] { Normal, Intermediate, Reduced, SuperReduced, Zero }.ToDictionary(r => r.Code, StringComparer.Ordinal);

    private VatRate(string code, decimal percent)
    {
        Code = code;
        Percent = percent;
    }

    public string Code { get; }

    public decimal Percent { get; }

    public static IReadOnlyCollection<VatRate> All => [.. Known.Values];

    public static Result<VatRate> FromCode(string? code) =>
        code is not null && Known.TryGetValue(code, out VatRate? rate) ? rate : SharedKernelErrors.VatRateUnknown;

    public override string ToString() => Code;
}
