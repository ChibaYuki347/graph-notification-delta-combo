using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Text.Json;

namespace FunctionApp.Functions;

public class CreateTestEventFunction
{
    private readonly ILogger _logger;
    private readonly GraphServiceClient _graph;

    public CreateTestEventFunction(ILoggerFactory lf, GraphServiceClient graph)
    {
        _logger = lf.CreateLogger<CreateTestEventFunction>();
        _graph = graph;
    }

    [Function("CreateTestEvent")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "graph/debug/create-test-event")] HttpRequestData req)
    {
        var queryCollection = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var room = queryCollection["room"] ?? "ConfRoom1@bbslooklab.onmicrosoft.com";
        var visitorId = queryCollection["visitorId"] ?? Guid.NewGuid().ToString();
        var subject = queryCollection["subject"] ?? "[テスト] VisitorID検証会議";

        _logger.LogInformation("Creating test event for room: {room}, visitorId: {visitorId}", room, visitorId);

        try
        {
            var startTime = DateTimeOffset.Now.AddMinutes(10);
            var endTime = startTime.AddMinutes(30);

            var newEvent = new Event
            {
                Subject = subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = startTime.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                    TimeZone = "Tokyo Standard Time"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = endTime.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                    TimeZone = "Tokyo Standard Time"
                },
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = $@"これはVisitorID検証用のテスト会議です。

VisitorID:{visitorId} ^^^^^^^^^ 【来客管理アドインからのお願い】^^^^^^^^^
メール本文に発行された番号、IDは、手動で削除しないでください。
システム側に情報が残ったままになります。
削除する場合は、アドインの「削除」ボタンから削除をお願いします。
なお、この説明は「削除」ボタンで削除されません。
お手数ですが手動で削除をお願いいたします。
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

※自動生成されたテストイベントです。"
                },
                Attendees = new List<Attendee>
                {
                    new Attendee
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = room,
                            Name = "Conference Room"
                        },
                        Type = AttendeeType.Resource
                    }
                }
            };

            var createdEvent = await _graph.Users[room].Events.PostAsync(newEvent);

            var result = new
            {
                success = true,
                eventId = createdEvent?.Id,
                subject = createdEvent?.Subject,
                start = createdEvent?.Start?.DateTime,
                end = createdEvent?.End?.DateTime,
                visitorId = visitorId,
                bodyContent = createdEvent?.Body?.Content,
                room = room
            };

            _logger.LogInformation("Test event created successfully: {eventId}", createdEvent?.Id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create test event for room {room}", room);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error creating test event: {ex.Message}");
            return errorResponse;
        }
    }
}
