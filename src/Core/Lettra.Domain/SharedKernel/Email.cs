using System.Net.Mail;
using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>Adresse e-mail (contrôle de forme seulement).</summary>
public sealed record Email
{
    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 254)
        {
            return SharedKernelErrors.EmailInvalid;
        }

        string value = raw.Trim();
        return MailAddress.TryCreate(value, out MailAddress? parsed) && parsed.Address == value && value.Contains('.', StringComparison.Ordinal)
            ? new Email(value)
            : SharedKernelErrors.EmailInvalid;
    }

    public override string ToString() => Value;
}
