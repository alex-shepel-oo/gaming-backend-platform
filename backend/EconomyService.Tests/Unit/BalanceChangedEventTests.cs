using System.Text.Json;
using AwesomeAssertions;
using EconomyService.Domain.Enums;
using EconomyService.Messaging.Events;
using NUnit.Framework;

namespace EconomyService.Tests.Unit;

[TestFixture]
public sealed class BalanceChangedEventTests
{
    [Test]
    public void SerializeThenDeserialize_RoundTrips_WithTypeAndVersionPresent()
    {
        var original = new BalanceChangedEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = DateTimeOffset.UtcNow,
            LedgerEntryId = Guid.CreateVersion7(),
            UserId = Guid.NewGuid(),
            CurrencyId = Guid.NewGuid(),
            Amount = 100m,
            Balance = 250m,
            TransactionType = TransactionType.Grant,
        };

        var json = JsonSerializer.Serialize(original);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("Type").GetString().Should().Be("balance.changed");
        document.RootElement.GetProperty("Version").GetInt32().Should().Be(1);

        var roundTripped = JsonSerializer.Deserialize<BalanceChangedEvent>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.Id.Should().Be(original.Id);
        roundTripped.OccurredAt.Should().Be(original.OccurredAt);
        roundTripped.LedgerEntryId.Should().Be(original.LedgerEntryId);
        roundTripped.UserId.Should().Be(original.UserId);
        roundTripped.CurrencyId.Should().Be(original.CurrencyId);
        roundTripped.Amount.Should().Be(original.Amount);
        roundTripped.Balance.Should().Be(original.Balance);
        roundTripped.TransactionType.Should().Be(original.TransactionType);
        roundTripped.Type.Should().Be("balance.changed");
        roundTripped.Version.Should().Be(1);
    }
}
