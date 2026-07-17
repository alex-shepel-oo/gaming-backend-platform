namespace IdentityService.Domain;

public sealed class User
{
    public required Guid Id { get; init; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public required bool IsActive { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    public ICollection<UserGameRole> UserGameRoles { get; init; } = [];
}
