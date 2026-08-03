using System.Text.Json;
using BuildingBlocks.Messaging.Outbox;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests.Integration.Fixtures;

// Now that IdentityService writes email side effects to the outbox instead of calling IEmailSender
// directly (RecordingEmailSender/Factory.EmailSender used to intercept that call, before this
// extraction), tests assert on outbox row contents instead -- same query shape
// ConfirmEmailOutboxTests already established for UserEmailConfirmedEvent, factored here since five
// other test classes need the same thing for the three new email events.
public static class OutboxTestExtensions
{
    public static async Task<List<TEvent>> GetOutboxEventsAsync<TEvent>(
        this IdentityApiFactory factory, string type, CancellationToken cancellationToken = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var messages = await dbContext.Set<OutboxMessage>()
            .Where(m => m.Type == type)
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(cancellationToken);

        return messages.Select(m => JsonSerializer.Deserialize<TEvent>(m.Payload)!).ToList();
    }
}
