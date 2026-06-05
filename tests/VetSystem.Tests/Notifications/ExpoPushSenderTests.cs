using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VetSystem.Application.Notifications;
using VetSystem.Infrastructure.Notifications;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// M21 task 2 — <see cref="ExpoPushSender"/> over a fake handler: ≤100-message batching, the exact
/// Expo wire shape (camelCase, channelId "default", priority high), positional
/// <c>DeviceNotRegistered</c> ticket → prune list, the optional access-token bearer, and the
/// best-effort contract (5xx / transport faults are swallowed, never thrown).
/// </summary>
public sealed class ExpoPushSenderTests
{
    private const string BaseUrl = "http://expo.test/push/send";

    [Fact]
    public async Task Batches_at_most_100_messages_per_request_and_serializes_the_expo_shape()
    {
        var handler = new RecordingHandler(_ => OkTickets(100));
        var sender = CreateSender(handler);

        var messages = Enumerable.Range(0, 250).Select(Message).ToList();
        var dead = await sender.SendAsync(messages, CancellationToken.None);

        dead.Should().BeEmpty();
        handler.Requests.Should().HaveCount(3);
        var batchSizes = handler.RequestBodies
            .Select(b => JsonDocument.Parse(b).RootElement.GetArrayLength());
        batchSizes.Should().Equal(100, 100, 50);

        var first = JsonDocument.Parse(handler.RequestBodies[0]).RootElement[0];
        first.GetProperty("to").GetString().Should().Be("ExponentPushToken[0]");
        first.GetProperty("title").GetString().Should().Be("عنوان");
        first.GetProperty("body").GetString().Should().Be("نص");
        first.GetProperty("channelId").GetString().Should().Be("default");
        first.GetProperty("priority").GetString().Should().Be("high");
        // The deeplink data mirrors SignalR's { notificationId, type, payload } — keys verbatim.
        first.GetProperty("data").GetProperty("notificationId").GetString().Should().NotBeNull();
        first.GetProperty("data").GetProperty("type").GetString().Should().Be("low_stock");
    }

    [Fact]
    public async Task DeviceNotRegistered_tickets_mark_their_message_token_for_pruning()
    {
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, """
            {"data":[
              {"status":"ok","id":"t1"},
              {"status":"error","message":"gone","details":{"error":"DeviceNotRegistered"}},
              {"status":"error","message":"odd","details":{"error":"MessageTooBig"}}
            ]}
            """));
        var sender = CreateSender(handler);

        var dead = await sender.SendAsync([Message(0), Message(1), Message(2)], CancellationToken.None);

        // Positional: only message 1's token is dead; the non-registration error keeps its token.
        dead.Should().Equal("ExponentPushToken[1]");
    }

    [Fact]
    public async Task Access_token_header_is_sent_only_when_configured()
    {
        var bare = new RecordingHandler(_ => OkTickets(1));
        await CreateSender(bare).SendAsync([Message(0)], CancellationToken.None);
        bare.Requests.Single().Headers.Authorization.Should().BeNull();

        var authed = new RecordingHandler(_ => OkTickets(1));
        await CreateSender(authed, accessToken: "expo-secret").SendAsync([Message(0)], CancellationToken.None);
        authed.Requests.Single().Headers.Authorization!.ToString().Should().Be("Bearer expo-secret");
    }

    [Fact]
    public async Task Non_success_and_transport_faults_are_swallowed_per_best_effort_contract()
    {
        var serverError = new RecordingHandler(_ => Response(HttpStatusCode.InternalServerError, "boom"));
        var act5xx = () => CreateSender(serverError).SendAsync([Message(0)], CancellationToken.None);
        (await act5xx.Should().NotThrowAsync()).Subject.Should().BeEmpty();

        var faulting = new RecordingHandler(_ => throw new HttpRequestException("offline"));
        var actFault = () => CreateSender(faulting).SendAsync([Message(0)], CancellationToken.None);
        (await actFault.Should().NotThrowAsync()).Subject.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_message_set_short_circuits_without_a_request()
    {
        var handler = new RecordingHandler(_ => OkTickets(0));
        var dead = await CreateSender(handler).SendAsync([], CancellationToken.None);

        dead.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    private static PushMessage Message(int i) => new(
        Token: $"ExponentPushToken[{i}]",
        Title: "عنوان",
        Body: "نص",
        Data: new Dictionary<string, object?>
        {
            ["notificationId"] = Guid.CreateVersion7().ToString(),
            ["type"] = "low_stock",
            ["payload"] = null,
        });

    private static ExpoPushSender CreateSender(RecordingHandler handler, string? accessToken = null)
        => new(
            new HttpClient(handler),
            Options.Create(new ExpoPushOptions { BaseUrl = BaseUrl, AccessToken = accessToken }),
            NullLogger<ExpoPushSender>.Instance);

    private static HttpResponseMessage OkTickets(int count)
    {
        var tickets = string.Join(",", Enumerable.Range(0, count).Select(i => $$"""{"status":"ok","id":"t{{i}}"}"""));
        return Response(HttpStatusCode.OK, $$"""{"data":[{{tickets}}]}""");
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>Captures every request (URL, headers, body) and answers from the provided factory.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _respond(request);
        }
    }
}
