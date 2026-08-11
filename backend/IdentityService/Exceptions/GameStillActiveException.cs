namespace IdentityService.Exceptions;

public sealed class GameStillActiveException() : Exception("Deactivate the game before deleting it.");
