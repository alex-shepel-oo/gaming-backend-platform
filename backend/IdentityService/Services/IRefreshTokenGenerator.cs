namespace IdentityService.Services;

public interface IRefreshTokenGenerator
{
    string GenerateRaw();

    byte[] Hash(string rawToken);
}
