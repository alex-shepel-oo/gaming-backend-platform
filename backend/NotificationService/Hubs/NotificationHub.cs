using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

// Server-to-client only: clients never invoke methods on this hub, they just
// listen for pushes (e.g. balanceChanged), so no methods are needed.
[Authorize]
public sealed class NotificationHub : Hub;
