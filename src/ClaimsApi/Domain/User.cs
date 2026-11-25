namespace ClaimsApi.Domain;

/// <summary>A seeded, read-only user. There is no real authentication (see DESIGN.md §2).</summary>
public sealed class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public Role Role { get; private set; }
    public Market? Market { get; private set; } // claimants only

    private User() { } // for EF Core

    public User(Guid id, string name, Role role, Market? market = null)
    {
        Id = id;
        Name = name;
        Role = role;
        Market = market;
    }
}
