using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

// Avatar URLs are no longer player-settable. Self-service arbitrary URLs with
// only an http(s)-scheme check were closed off rather than validated properly.
// Existing values still read back fine (see UserDto); only the write side is closed.
public sealed record UpdateProfileRequest(
    [property: StringLength(64, MinimumLength = 2)] string? DisplayName);
