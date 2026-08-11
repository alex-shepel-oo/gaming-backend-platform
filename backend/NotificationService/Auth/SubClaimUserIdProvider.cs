using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;

namespace NotificationService.Auth;

// SignalR's default IUserIdProvider reads ClaimTypes.NameIdentifier (the XML-schema
// URI), but MapInboundClaims = false (the same convention every other service in
// this codebase uses) keeps the token's claim under its short JWT name instead.
// Without this override, Clients.User(id) matches no connection and silently
// delivers nothing.
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
