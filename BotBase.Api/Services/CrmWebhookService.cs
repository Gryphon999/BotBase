using BotBase.Api.Data.Entities;
using System.Text;
using System.Text.Json;

namespace BotBase.Api.Services;

public class CrmWebhookService(IHttpClientFactory httpFactory, ILogger<CrmWebhookService> logger)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task FireAsync(string webhookUrl, string eventName, Appointment appt)
    {
        var payload = new
        {
            @event = eventName,
            appointment = new
            {
                appt.Id,
                appt.BusinessId,
                appt.ProcedureName,
                appt.ClientName,
                appt.ClientPhone,
                appt.ScheduledAt,
                appt.DurationMinutes,
                Status = appt.Status.ToString(),
                appt.CreatedAt,
                appt.UpdatedAt
            }
        };

        try
        {
            var client = httpFactory.CreateClient();
            var body = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.PostAsync(webhookUrl, body, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning("CRM webhook failed ({Url}): {Error}", webhookUrl, ex.Message);
        }
    }
}
