namespace IdentityService.Services;

public interface IVerificationCodeGenerator
{
    string Generate();

    string Hash(string code);

    bool Verify(string code, string hash);
}
