namespace IdentityService.Domain;

public sealed class Game
{
    public required Guid Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; set; }
    public required bool IsActive { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }

    public ICollection<UserGameRole> UserGameRoles { get; init; } = [];
}
