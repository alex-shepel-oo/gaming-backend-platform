namespace IdentityService.Contracts.Responses;

public sealed record GameDto(Guid Id, string Slug, string Name, bool IsActive, DateTimeOffset CreatedAt);
