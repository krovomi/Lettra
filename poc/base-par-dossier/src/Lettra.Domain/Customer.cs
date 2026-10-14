namespace Lettra.Domain;

/// Client (tiers) d'un dossier. Volontairement minimal : le POC porte sur la persistance, pas sur le métier.
public sealed class Customer
{
    private Customer() { }

    public Customer(string name, string? siren)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Guid.CreateVersion7();
        Name = name;
        Siren = siren;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Siren { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
