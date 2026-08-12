using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Configuration;
using System.Text;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for Microsoft Teams notifications via Incoming Webhooks.
/// </summary>
public sealed class TeamsService : ITeamsService
{
    private readonly TeamsSettings _teamsSettings;
    private readonly NotificationChannels _notificationChannels;
    private readonly ILogger<TeamsService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _applicationUrl;

    public TeamsService(
        IOptions<TeamsSettings> teamsSettings,
        IOptions<NotificationChannels> notificationChannels,
        ILogger<TeamsService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _teamsSettings = teamsSettings.Value;
        _notificationChannels = notificationChannels.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_teamsSettings.TimeoutSeconds);
        _applicationUrl = configuration["EmailSettings:ApplicationUrl"] ?? "https://localhost:5001";
    }

    public bool IsEnabled() => _teamsSettings.EnableTeams && !string.IsNullOrWhiteSpace(_teamsSettings.WebhookUrl);

    public bool IsEnabledForType(string notificationType)
    {
        if (!IsEnabled()) return false;

        return notificationType.ToLower() switch
        {
            "lowstock" => _teamsSettings.EnableForLowStock && _notificationChannels.LowStockAlerts.Teams,
            "expiry" => _teamsSettings.EnableForExpiry && _notificationChannels.ExpiryAlerts.Teams,
            "anomaly" => _teamsSettings.EnableForAnomalies && _notificationChannels.SecurityAlerts.Teams,
            "securityrisk" => _teamsSettings.EnableForSecurityRisks && _notificationChannels.SecurityAlerts.Teams,
            "system" => _teamsSettings.EnableForSystemAlerts && _notificationChannels.SystemAlerts.Teams,
            _ => false
        };
    }

    public async Task<bool> SendMessageAsync(string title, string message, string? themeColor = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            _logger.LogDebug("Teams notifications are disabled");
            return false;
        }

        var messageCard = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text = title,
                                size = "Large",
                                weight = "Bolder",
                                color = "Accent"
                            },
                            new
                            {
                                type = "TextBlock",
                                text = message,
                                wrap = true
                            }
                        }
                    }
                }
            }
        };

        return await SendToTeamsAsync(messageCard);
    }

    public async Task<bool> SendLowStockAlertAsync(string productName, int currentStock, int minStock, string? warehouseName = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabledForType("lowstock")) return false;

        var facts = new List<object>
        {
            new { title = "Produkt", value = productName },
            new { title = "Aktueller Bestand", value = $"{currentStock} St\u00fcck" },
            new { title = "Mindestbestand", value = $"{minStock} St\u00fcck" },
            new { title = "Fehlmenge", value = $"{minStock - currentStock} St\u00fcck" }
        };

        if (!string.IsNullOrEmpty(warehouseName))
        {
            facts.Add(new { title = "Lager", value = warehouseName });
        }

        var messageCard = CreateAdaptiveCard(
            "Niedriger Bestand",
            $"Der Bestand von **{productName}** ist unter dem Mindestbestand!",
            "Warning",
            facts,
            new[]
            {
                new
                {
                    type = "Action.OpenUrl",
                    title = "Produkt anzeigen",
                    url = $"{_applicationUrl}/products"
                },
                new
                {
                    type = "Action.OpenUrl",
                    title = "Nachbestellen",
                    url = $"{_applicationUrl}/products"
                }
            });

        return await SendToTeamsAsync(messageCard);
    }

    public async Task<bool> SendExpiryAlertAsync(string productName, DateTime expiryDate, int quantity, string? location = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabledForType("expiry")) return false;

        var daysUntilExpiry = (expiryDate - DateTime.UtcNow).Days;
        var urgency = daysUntilExpiry switch
        {
            <= 0 => ("ABGELAUFEN", "Attention"),
            <= 7 => ("Kritisch", "Warning"),
            _ => ("Warnung", "Accent")
        };

        var facts = new List<object>
        {
            new { title = "Produkt", value = productName },
            new { title = "MHD", value = expiryDate.ToString("dd.MM.yyyy") },
            new { title = "Tage bis Ablauf", value = $"{daysUntilExpiry} Tage" },
            new { title = "Menge", value = $"{quantity} St\u00fcck" }
        };

        if (!string.IsNullOrEmpty(location))
        {
            facts.Add(new { title = "Lagerort", value = location });
        }

        var messageCard = CreateAdaptiveCard(
            $"{urgency.Item1} MHD-Warnung",
            $"**{productName}** l\u00e4uft bald ab oder ist bereits abgelaufen!",
            urgency.Item2,
            facts,
            new[]
            {
                new
                {
                    type = "Action.OpenUrl",
                    title = "MHD-\u00dcberwachung \u00f6ffnen",
                    url = $"{_applicationUrl}/expiry-monitoring"
                }
            });

        return await SendToTeamsAsync(messageCard);
    }

    public async Task<bool> SendAnomalyAlertAsync(string anomalyType, double score, string description, string? affectedEntity = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabledForType("anomaly")) return false;

        var severity = score switch
        {
            >= 0.8 => ("Kritisch", "Attention"),
            >= 0.6 => ("Hoch", "Warning"),
            _ => ("Mittel", "Accent")
        };

        var facts = new List<object>
        {
            new { title = "Anomalie-Typ", value = anomalyType },
            new { title = "Score", value = $"{score:F2}" },
            new { title = "Beschreibung", value = description }
        };

        if (!string.IsNullOrEmpty(affectedEntity))
        {
            facts.Add(new { title = "Betroffenes Objekt", value = affectedEntity });
        }

        var messageCard = CreateAdaptiveCard(
            $"{severity.Item1} Anomalie erkannt",
            $"Das ML-System hat eine **{anomalyType}**-Anomalie erkannt.",
            severity.Item2,
            facts,
            new[]
            {
                new
                {
                    type = "Action.OpenUrl",
                    title = "Security Center \u00f6ffnen",
                    url = $"{_applicationUrl}/admin/security-center"
                }
            });

        return await SendToTeamsAsync(messageCard);
    }

    public async Task<bool> SendSecurityRiskAlertAsync(string username, string riskLevel, double riskScore, List<string> riskFactors, CancellationToken cancellationToken = default)
    {
        if (!IsEnabledForType("securityrisk")) return false;

        var severity = riskLevel.ToLower() switch
        {
            "critical" => ("KRITISCH", "Attention"),
            "high" => ("Hoch", "Warning"),
            _ => ("Mittel", "Accent")
        };

        var facts = new List<object>
        {
            new { title = "Benutzer", value = username },
            new { title = "Risiko-Level", value = riskLevel },
            new { title = "Risiko-Score", value = $"{riskScore:F1}" },
            new { title = "Risikofaktoren", value = string.Join(", ", riskFactors.Take(3)) }
        };

        var messageCard = CreateAdaptiveCard(
            $"{severity.Item1} Sicherheitsrisiko",
            $"Der Benutzer **{username}** wurde als **{riskLevel}** Risk eingestuft!",
            severity.Item2,
            facts,
            new[]
            {
                new
                {
                    type = "Action.OpenUrl",
                    title = "Security Center \u00f6ffnen",
                    url = $"{_applicationUrl}/admin/security-center"
                },
                new
                {
                    type = "Action.OpenUrl",
                    title = "Benutzer sperren",
                    url = $"{_applicationUrl}/admin/users"
                }
            });

        return await SendToTeamsAsync(messageCard);
    }

    public async Task<bool> SendSystemAlertAsync(string title, string message, string severity, CancellationToken cancellationToken = default)
    {
        if (!IsEnabledForType("system")) return false;

        var colorMap = severity.ToLower() switch
        {
            "error" => "Attention",
            "warning" => "Warning",
            "info" => "Accent",
            _ => "Default"
        };

        var messageCard = CreateAdaptiveCard(
            title,
            message,
            colorMap,
            new List<object>
            {
                new { title = "Schweregrad", value = severity },
                new { title = "Zeitpunkt", value = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") }
            },
            new[]
            {
                new
                {
                    type = "Action.OpenUrl",
                    title = "Dashboard \u00f6ffnen",
                    url = $"{_applicationUrl}/dashboard"
                }
            });

        return await SendToTeamsAsync(messageCard);
    }

    public async Task<bool> SendAdaptiveCardAsync(object adaptiveCard, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled()) return false;

        var messageCard = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = adaptiveCard
                }
            }
        };

        return await SendToTeamsAsync(messageCard);
    }

    private object CreateAdaptiveCard(string title, string message, string color, List<object> facts, object[]? actions = null)
    {
        var body = new List<object>
        {
            new
            {
                type = "Container",
                style = color,
                items = new object[]
                {
                    new
                    {
                        type = "TextBlock",
                        text = title,
                        size = "Large",
                        weight = "Bolder",
                        color = "Light"
                    }
                }
            },
            new
            {
                type = "TextBlock",
                text = message,
                wrap = true,
                spacing = "Medium"
            },
            new
            {
                type = "FactSet",
                facts = facts.ToArray(),
                spacing = "Medium"
            }
        };

        var card = new
        {
            type = "AdaptiveCard",
            version = "1.4",
            body = body.ToArray(),
            actions = actions ?? Array.Empty<object>()
        };

        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = card
                }
            }
        };
    }

    private async Task<bool> SendToTeamsAsync(object messageCard, CancellationToken cancellationToken = default)
    {
        var retries = 0;
        while (retries < _teamsSettings.MaxRetries)
        {
            try
            {
                var json = JsonSerializer.Serialize(messageCard);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("Sending message to Teams webhook");

                var response = await _httpClient.PostAsync(_teamsSettings.WebhookUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Teams notification sent successfully");
                    return true;
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Teams webhook returned {StatusCode}: {Error}", response.StatusCode, errorBody);

                retries++;
                if (retries < _teamsSettings.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Teams notification (attempt {Attempt}/{Max})", retries + 1, _teamsSettings.MaxRetries);
                retries++;

                if (retries < _teamsSettings.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                }
            }
        }

        _logger.LogError("Failed to send Teams notification after {Retries} attempts", _teamsSettings.MaxRetries);
        return false;
    }
}
