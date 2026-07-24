using IdentityService.Domain.Enums;

namespace IdentityService.Domain;

// CA1711 flags the "Permission" suffix as if this were a Flags enum; it's a plain
// entity mapped to the role_permissions table, named to match.
#pragma warning disable CA1711
public sealed class RolePermission
{
    public required Guid Id { get; init; }
    public required PlatformRole Role { get; init; }
    public Guid? GameId { get; init; }
    public required string Permission { get; init; }
    public required DateTimeOffset GrantedAt { get; init; }
    public Guid? GrantedBy { get; init; }

    public Game? Game { get; init; }
}
#pragma warning restore CA1711
