namespace IdentityService.Exceptions;

public sealed class GameSlugAlreadyExistsException() : Exception("A game with this slug already exists.");
