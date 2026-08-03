using System.Text.Json;
using AwesomeAssertions;
using BuildingBlocks.Messaging;
using EmailService.Messaging;
using EmailService.Options;
using EmailService.Services.Email.Templates;
using EmailService.Tests.Integration.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EmailService.Tests.Integration;

[Collection(nameof(EmailServiceRabbitMqCollectionDefinition))]
public sealed class DuplicateRegistrationNoticeRequestedConsumerTests(RabbitMqFixture rabbitMq) : IDisposable
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(15);

    private readonly string _templatesDirectory =
        Directory.CreateTempSubdirectory("duplicate-registration-notice-consumer-tests-").FullName;

    public void Dispose() => Directory.Delete(_templatesDirectory, recursive: true);

    [Fact]
    public async Task DuplicateRegistrationNoticeRequested_Delivered_RendersTemplateAndSendsWithExpectedContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        File.WriteAllText(
            Path.Combine(_templatesDirectory, "DuplicateRegistrationNotice.html"),
            "<p>Attempt for {{GameName}}</p>");

        var emailSender = new RecordingEmailSender();
        var rabbitMqOptions = BuildRabbitMqOptions();
        var queueName = $"gbp.email.duplicate-registration-notice-requested.test.{Guid.NewGuid():N}";

        await using var connection = new RabbitMqConnection(MsOptions.Create(rabbitMqOptions));
        using var consumer = new DuplicateRegistrationNoticeRequestedConsumer(
            connection,
            MsOptions.Create(rabbitMqOptions),
            new EmailTemplateRenderer(MsOptions.Create(new EmailOptions { TemplatesPath = _templatesDirectory })),
            emailSender,
            NullLogger<DuplicateRegistrationNoticeRequestedConsumer>.Instance,
            queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(rabbitMqOptions, queueName, cancellationToken);

            await PublishAsync(
                rabbitMqOptions,
                "duplicate_registration_notice.requested",
                new { Email = "player@example.com", GameName = "Test Game" },
                cancellationToken);

            await WaitUntilAsync(() => emailSender.Sent.Count > 0, DeliveryTimeout);
        }
        finally
        {
            await consumer.StopAsync(cancellationToken);
        }

        var sent = emailSender.Sent.Single();
        sent.To.Should().Be("player@example.com");
        sent.Subject.Should().Be("Registration attempt on your email address");
        sent.HtmlBody.Should().Contain("Test Game");
        sent.TextBody.Should().Contain("Test Game");
    }

    private RabbitMqOptions BuildRabbitMqOptions() => new()
    {
        Host = rabbitMq.Container.Hostname,
        Port = rabbitMq.Container.GetMappedPublicPort(5672),
        Username = "guest",
        Password = "guest",
        ExchangeName = "gbp.identity",
    };

    private static async Task PublishAsync(RabbitMqOptions options, string type, object payload, CancellationToken cancellationToken)
    {
        await using var connection = new RabbitMqConnection(MsOptions.Create(options));
        var eventBus = new RabbitMqEventBus(connection, MsOptions.Create(options));

        await eventBus.PublishAsync(new EventEnvelope(type, 1, JsonSerializer.Serialize(payload)), headers: null, cancellationToken);
    }

    private static async Task WaitForConsumerReadyAsync(RabbitMqOptions options, string queueName, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        await using var connection = new RabbitMqConnection(MsOptions.Create(options));

        while (!cts.IsCancellationRequested)
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync(cts.Token);
                await channel.QueueDeclarePassiveAsync(queueName, cts.Token);
                return;
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }

        Assert.Fail($"Consumer for queue '{queueName}' was not attached within the timeout.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }
}
