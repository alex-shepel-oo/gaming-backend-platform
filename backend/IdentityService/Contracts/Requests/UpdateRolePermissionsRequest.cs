using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record UpdateRolePermissionsRequest([property: Required] string[] Permissions);
