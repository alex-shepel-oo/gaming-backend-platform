namespace EconomyService.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid? GameId { get; }
    string Role { get; }
}
