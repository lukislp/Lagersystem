using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;

namespace LagersystemLVHome.Application.Services;

public sealed class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;
    private readonly IInventoryService _inventoryService;
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly string _ollamaBaseUrl;

    public OllamaService(
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaService> logger,
        IInventoryService inventoryService,
        IDbContextFactory<InventoryDbContext> contextFactory,
        IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient("Ollama");
        _logger = logger;
        _inventoryService = inventoryService;
        _contextFactory = contextFactory;
        _ollamaBaseUrl = configuration["OllamaSettings:BaseUrl"] ?? "http://localhost:11434";
        _httpClient.BaseAddress = new Uri(_ollamaBaseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }


    public async Task<string> ChatAsync(string prompt, string model = "llama3.2", string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
        }

        messages.Add(new ChatMessage { Role = "user", Content = prompt });

        return await ChatWithHistoryAsync(messages, model);
    }

    public async Task<string> ChatWithHistoryAsync(List<ChatMessage> messages, string model = "llama3.2", CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new OllamaChatRequest
            {
                Model = model,
                Messages = messages,
                Stream = false
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/chat", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Ollama API error: {StatusCode}", response.StatusCode);
                return $"Error: Ollama API returned {response.StatusCode}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return chatResponse?.Message?.Content ?? "No response from Ollama";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Ollama. Is it running?");
            return "Error: Cannot connect to Ollama. Please ensure Ollama is running on " + _ollamaBaseUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Ollama chat");
            return $"Error: {ex.Message}";
        }
    }



    public async Task<string> AskInventoryQuestionAsync(string question, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Collect complete inventory context
        var products = await _inventoryService.GetAllProductsAsync();
        var productsList = products.ToList();
        var categories = await _inventoryService.GetAllCategoriesAsync();
        var categoriesList = categories.ToList();
        var lowStockProducts = await _inventoryService.GetLowStockProductsAsync();
        var lowStockList = lowStockProducts.ToList();

        // Load storage locations with products and rooms
        var storageLocations = await context.ProductStorageLocations
            .Include(psl => psl.Product)
            .ThenInclude(p => p.Category)
            .Include(psl => psl.StorageLocation)
            .Where(psl => psl.Quantity > 0)
            .ToListAsync(cancellationToken);

        // Load product batches with expiry data
        var allBatches = await context.ProductBatches
            .Include(pb => pb.Product)
            .ThenInclude(p => p.Category)
            .Where(pb => pb.Quantity > 0)
            .OrderBy(pb => pb.ExpiryDate)
            .ToListAsync(cancellationToken);

        var currentDate = DateTime.UtcNow;
        var expiredBatches = allBatches.Where(pb => pb.ExpiryDate.HasValue && pb.ExpiryDate.Value < currentDate).ToList();
        var expiringSoon = allBatches.Where(pb => pb.ExpiryDate.HasValue && pb.ExpiryDate.Value >= currentDate && pb.ExpiryDate.Value <= currentDate.AddDays(7)).ToList();
        var expiringMonth = allBatches.Where(pb => pb.ExpiryDate.HasValue && pb.ExpiryDate.Value > currentDate.AddDays(7) && pb.ExpiryDate.Value <= currentDate.AddDays(30)).ToList();
        var batchesWithoutExpiry = allBatches.Where(pb => !pb.ExpiryDate.HasValue).ToList();

        // Build complete product list with all details including storage locations and batches
        var allProducts = string.Join("\n", productsList.Select(p =>
        {
            var storageInfo = storageLocations
                .Where(sl => sl.ProductId == p.Id)
                .Select(sl => $"{sl.StorageLocation.Code} ({sl.StorageLocation.Name}, Raum: {sl.StorageLocation.Room ?? "Unbekannt"}, Menge: {sl.Quantity} St\u00fcck)")
                .ToList();

            var storageText = storageInfo.Any()
                ? string.Join(" | ", storageInfo)
                : "Kein Lagerplatz zugewiesen";

            var productBatches = allBatches.Where(b => b.ProductId == p.Id).ToList();
            var batchText = productBatches.Any()
                ? string.Join(" | ", productBatches.Select(b =>
                    $"Charge: {b.BatchNumber}, Menge: {b.Quantity}, MHD: {(b.ExpiryDate.HasValue ? b.ExpiryDate.Value.ToString("dd.MM.yyyy") : "Kein MHD")}, Tage bis Ablauf: {b.DaysUntilExpiry}"))
                : "Keine Chargen";

            return $"- ID: {p.Id} | Name: {p.Name} | Bestand: {p.Quantity} St\u00fcck | " +
                $"Mindestbestand: {p.MinQuantity} | Preis: {p.Price:C} | " +
                $"Kategorie: {p.Category?.Name ?? "Keine"} | " +
                $"Barcode: {p.Barcode ?? "Kein"} | " +
                $"Lagerpl\u00e4tze: {storageText} | " +
                $"Chargen/MHD: {batchText} | " +
                $"Status: {(p.Quantity <= p.MinQuantity ? "KRITISCH - Nachbestellen!" : p.Quantity <= p.MinQuantity * 1.5 ? "Niedrig" : "OK")}";
        }));

        // Build batch/expiry overview
        var batchSummary = $@"
Bereits abgelaufen: {expiredBatches.Count} Chargen
Laufen in 7 Tagen ab: {expiringSoon.Count} Chargen
Laufen im n{"\u00e4"}chsten Monat ab: {expiringMonth.Count} Chargen
Ohne MHD: {batchesWithoutExpiry.Count} Chargen
Gesamt: {allBatches.Count} Chargen im System";

        var expiredBatchDetails = expiredBatches.Any()
            ? string.Join("\n", expiredBatches.Select(b =>
                $"- {b.Product?.Name} | Charge: {b.BatchNumber} | Menge: {b.Quantity} | Abgelaufen seit: {Math.Abs(b.DaysUntilExpiry)} Tagen | MHD: {b.ExpiryDate:dd.MM.yyyy}"))
            : "Keine abgelaufenen Chargen";

        var expiringSoonDetails = expiringSoon.Any()
            ? string.Join("\n", expiringSoon.Select(b =>
                $"- {b.Product?.Name} | Charge: {b.BatchNumber} | Menge: {b.Quantity} | L{"\u00e4"}uft ab in: {b.DaysUntilExpiry} Tagen | MHD: {b.ExpiryDate:dd.MM.yyyy}"))
            : "Keine Chargen laufen in den n\u00e4chsten 7 Tagen ab";

        var expiringMonthDetails = expiringMonth.Any()
            ? string.Join("\n", expiringMonth.Take(20).Select(b =>
                $"- {b.Product?.Name} | Charge: {b.BatchNumber} | Menge: {b.Quantity} | L{"\u00e4"}uft ab in: {b.DaysUntilExpiry} Tagen | MHD: {b.ExpiryDate:dd.MM.yyyy}"))
            : "Keine Chargen laufen im n\u00e4chsten Monat ab";

        // Build storage location overview
        var storageLocationsSummary = string.Join("\n", storageLocations
            .GroupBy(sl => new { sl.StorageLocation.Code, sl.StorageLocation.Name, sl.StorageLocation.Room })
            .Select(g => $"- {g.Key.Code} ({g.Key.Name}, Raum: {g.Key.Room ?? "Unbekannt"}): " +
                $"{g.Count()} Produkte, Gesamtmenge: {g.Sum(sl => sl.Quantity)} St\u00fcck"));

        // Build detailed category overview
        var categoryDetails = string.Join("\n", categoriesList.Select(c =>
        {
            var productsInCategory = productsList.Where(p => p.CategoryId == c.Id).ToList();
            var totalValue = productsInCategory.Sum(p => p.Quantity * p.Price);
            var totalQuantity = productsInCategory.Sum(p => p.Quantity);
            return $"- {c.Name}: {productsInCategory.Count} Produkte | Gesamtmenge: {totalQuantity} | Gesamtwert: {totalValue:C}";
        }));

        // Build critical stock list
        var criticalStockDetails = string.Join("\n", lowStockList.Select(p =>
            $"- {p.Name} (ID: {p.Id}): Aktuell {p.Quantity} St\u00fcck (Min: {p.MinQuantity}) | " +
            $"Kategorie: {p.Category?.Name ?? "Keine"} | " +
            $"Differenz: {p.MinQuantity - p.Quantity} St\u00fcck fehlen | " +
            $"Empfohlen nachbestellen: {Math.Max(p.MinQuantity * 2 - p.Quantity, 0)} St\u00fcck"));

        // Calculate warehouse statistics
        var totalProducts = productsList.Count;
        var totalQuantity = productsList.Sum(p => p.Quantity);
        var totalValue = productsList.Sum(p => p.Quantity * p.Price);
        var criticalCount = lowStockList.Count;
        var lowCount = productsList.Count(p => p.Quantity <= p.MinQuantity * 1.5 && p.Quantity > p.MinQuantity);
        var okCount = totalProducts - criticalCount - lowCount;
        var totalStorageLocations = storageLocations.Select(sl => sl.StorageLocationId).Distinct().Count();
        var totalBatches = allBatches.Count;

        var systemPrompt = $@"Du bist ein KI-Assistent f{"\u00fc"}r ein Lagerverwaltungssystem mit vollst{"\u00e4"}ndigem Zugriff auf alle Inventardaten, Lagerpositionen und Chargen/MHD-Informationen.
Du erh{"\u00e4"}ltst die kompletten Daten ohne Einschr{"\u00e4"}nkungen, da Ollama lokal l{"\u00e4"}uft und es keine Token-Limits gibt.

DU BIST:
- Freundlich, hilfsbereit und professionell
- Ein Experte f{"\u00fc"}r Lagerverwaltung, Inventar-Optimierung und MHD-Management
- Proaktiv mit Vorschl{"\u00e4"}gen und Empfehlungen
- Geduldig und verst{"\u00e4"}ndnisvoll

BEI BEGR{"\u00dc"}SSUNGEN:
Wenn jemand dich mit ""Hi"", ""Hallo"", ""Guten Tag"" oder {"\u00e4"}hnlich begr{"\u00fc\u00df"}t:
- Begr{"\u00fc\u00df"}e zur{"\u00fc"}ck mit warmem, pers{"\u00f6"}nlichem Ton
- Stelle dich kurz vor (AI Lager-Assistent)
- Gib 2-3 konkrete Beispiele was du helfen kannst
- Erw{"\u00e4"}hne die aktuellen Lager-Statistiken inkl. MHD-Status
- Frage, wie du helfen kannst

LAGER-GESAMTSTATISTIK:
Gesamtanzahl Produkte: {totalProducts}
Gesamtmenge aller Artikel: {totalQuantity:N0} St{"\u00fc"}ck
Gesamtwert des Lagers: {totalValue:C}
Anzahl Lagerpl{"\u00e4"}tze: {totalStorageLocations}
Anzahl Chargen: {totalBatches}

Bestandsstatus:
  OK: {okCount} Produkte ({"\u00fc"}ber 150% des Mindestbestands)
  Niedrig: {lowCount} Produkte (100-150% des Mindestbestands)
  KRITISCH: {criticalCount} Produkte (auf oder unter Mindestbestand)

Anzahl Kategorien: {categoriesList.Count}

CHARGEN & MHD-{"\u00dc"}BERSICHT:
{batchSummary}

ABGELAUFENE CHARGEN ({expiredBatches.Count}):
{expiredBatchDetails}

LAUFEN IN 7 TAGEN AB ({expiringSoon.Count}):
{expiringSoonDetails}

LAUFEN IM N{"\u00c4"}CHSTEN MONAT AB ({expiringMonth.Count}):
{expiringMonthDetails}

VOLLST{"\u00c4"}NDIGE PRODUKTLISTE MIT LAGERPOSITIONEN UND CHARGEN (ALLE {totalProducts} PRODUKTE):
{allProducts}

LAGERPLATZ-{"\u00dc"}BERSICHT:
{storageLocationsSummary}

KATEGORIEN-{"\u00dc"}BERSICHT (ALLE {categoriesList.Count} KATEGORIEN):
{categoryDetails}

KRITISCHE BEST{"\u00c4"}NDE ({criticalCount} Produkte):
{(criticalCount > 0 ? criticalStockDetails : "Keine kritischen Best\u00e4nde - alles im gr\u00fcnen Bereich!")}

INTELLIGENTE PRODUKTSUCHE & MHD-ABFRAGEN:
Produkte k{"\u00f6"}nnen mit TEILNAMEN gesucht werden!
Beispiele:
- ""Wo liegt der Permanent Marker?"" findet ""Edding 3000 Permanent Marker""
- ""Wo ist das Edding?"" findet ""Edding 3000 Permanent Marker""
- ""Welche Lebensmittel laufen bald ab?"" zeigt alle Lebensmittel mit MHD in 7 Tagen

SUCH-REGELN:
1. Vergleiche den Suchbegriff CASE-INSENSITIVE mit Produktnamen
2. Ein Treffer ist g{"\u00fc"}ltig, wenn der Suchbegriff IRGENDWO im Produktnamen vorkommt
3. Bei mehreren Treffern: Liste ALLE gefundenen Produkte auf
4. Gib IMMER die Lagerpositionen mit Raum, Code und Menge an
5. Zeige MHD-Status, wenn Chargen vorhanden sind

FORMATIERUNGS-REGELN:
1. Strukturiere mit {"\u00dc"}berschriften, Aufz{"\u00e4"}hlungen und Einr{"\u00fc"}ckungen
2. Keine Tabellen (schlecht lesbar)
3. Gib konkrete Produktnamen, IDs, Lagercodes, Chargennummern und Zahlen an
4. Bei Standort-Fragen: IMMER Raum, Lagerplatz-Code und Menge angeben
5. Bei MHD-Fragen: IMMER Charge, MHD-Datum, Tage bis Ablauf und Status angeben
6. Unterst{"\u00fc"}tze Teilnamen-Suche
7. Berechne Prozente, Summen und Verh{"\u00e4"}ltnisse
8. F{"\u00fc"}ge immer eine Zusammenfassung/Handlungsempfehlung hinzu
9. Antworte auf Deutsch in klarem, freundlichem, professionellem Stil
10. Bei Begr{"\u00fc\u00df"}ungen: Sei warm, pers{"\u00f6"}nlich und zeige aktuelle Statistiken inkl. MHD
11. Priorisiere FIFO (First In, First Out) bei MHD-Empfehlungen

Beantworte jetzt die folgende Frage pr{"\u00e4"}zise mit den obigen Daten und Formatierungs-Regeln:";

        return await ChatAsync(question, "llama3.2", systemPrompt);
    }

    public async Task<string> GenerateProductDescriptionAsync(string productName, string? category = null, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Erstelle eine professionelle Produktbeschreibung f{"\u00fc"}r folgendes Produkt:
Produktname: {productName}
{(category != null ? $"Kategorie: {category}" : "")}

Die Beschreibung sollte:
- Kurz und pr{"\u00e4"}gnant sein (2-3 S{"\u00e4"}tze)
- Die wichtigsten Features hervorheben
- Professionell klingen
- Auf Deutsch sein

Gib nur die Beschreibung zur{"\u00fc"}ck, ohne zus{"\u00e4"}tzliche Erkl{"\u00e4"}rungen.";

        return await ChatAsync(prompt, "llama3.2");
    }

    public async Task<string> SuggestOptimalStorageAsync(string productName, string category, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Schlage einen optimalen Lagerort f{"\u00fc"}r folgendes Produkt vor:
Produktname: {productName}
Kategorie: {category}

Ber{"\u00fc"}cksichtige dabei:
- Zugriffsh{"\u00e4"}ufigkeit (h{"\u00e4"}ufig benutzte Produkte sollten leicht erreichbar sein)
- Temperaturanforderungen
- Gr{"\u00f6\u00df"}e und Gewicht
- Zusammenlagerung mit {"\u00e4"}hnlichen Produkten

Gib eine kurze, praktische Empfehlung (2-3 S{"\u00e4"}tze) auf Deutsch.";

        return await ChatAsync(prompt, "llama3.2");
    }

    public async Task<string> AnalyzeInventoryTrendsAsync(CancellationToken cancellationToken = default)
    {
        var movements = await _inventoryService.GetRecentMovementsAsync(100);
        var recentMovements = movements.ToList();

        var prompt = $@"Analysiere folgende Lagerbewegungsdaten:
Anzahl Bewegungen (letzte 100): {recentMovements.Count}
Warenein/-ausg{"\u00e4"}nge: Analysiere Trends

Gib eine kurze Analyse mit:
1. Identifizierte Trends
2. Auff{"\u00e4"}lligkeiten
3. Handlungsempfehlungen

Antworte auf Deutsch, max. 5 S{"\u00e4"}tze.";

        return await ChatAsync(prompt, "llama3.2");
    }

    public async Task<string> PredictReorderNeedsAsync(CancellationToken cancellationToken = default)
    {
        var lowStockProducts = await _inventoryService.GetLowStockProductsAsync();

        var prompt = $@"Basierend auf folgenden Daten zum niedrigen Bestand:
Produkte mit niedrigem Bestand: {lowStockProducts.Count()}

Erstelle eine Nachbestellungsempfehlung:
1. Priorisierung der Produkte
2. Empfohlene Bestellmengen
3. Zeitrahmen

Antworte auf Deutsch, strukturiert und pr{"\u00e4"}zise.";

        return await ChatAsync(prompt, "llama3.2");
    }



    public async Task<string> ConvertToSqlQueryAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Konvertiere folgende nat{"\u00fc"}rlichsprachliche Abfrage in eine SQLite-SQL-Query:
'{naturalLanguageQuery}'

Verf{"\u00fc"}gbare Tabellen:
- Products (Id, Name, Barcode, CategoryId, Quantity, MinQuantity, Price)
- Categories (Id, Name, Description)
- StockMovements (Id, ProductId, Quantity, MovementType, Timestamp)
- StorageLocations (Id, Code, Name, RoomId)

Gib NUR die SQL-Query zur{"\u00fc"}ck, ohne Erkl{"\u00e4"}rungen oder Markdown-Formatierung.";

        return await ChatAsync(prompt, "llama3.2");
    }



    public async Task<List<OllamaModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching models from {BaseUrl}/api/tags", _ollamaBaseUrl);

            var response = await _httpClient.GetAsync("/api/tags");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama API returned status code: {StatusCode}", response.StatusCode);
                return new List<OllamaModel>();
            }

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Received JSON: {Json}", json.Substring(0, Math.Min(500, json.Length)));

            var result = JsonSerializer.Deserialize<OllamaTagsResponse>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            if (result?.Models == null || !result.Models.Any())
            {
                _logger.LogWarning("No models found in response");
                return new List<OllamaModel>();
            }

            _logger.LogInformation("Found {Count} models", result.Models.Count);
            return result.Models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Ollama models from {BaseUrl}", _ollamaBaseUrl);
            return new List<OllamaModel>();
        }
    }

    public async Task<bool> PullModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new { name = modelName };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/pull", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling Ollama model {ModelName}", modelName);
            return false;
        }
    }

    public async Task<bool> IsModelAvailableAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var models = await GetAvailableModelsAsync();
        return models.Any(m => m.Name.StartsWith(modelName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OllamaModelInfo> GetModelInfoAsync(string modelName, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new { name = modelName };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/show", content);
            if (!response.IsSuccessStatusCode)
                return new OllamaModelInfo { Name = modelName };

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OllamaModelInfo>(responseJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? new OllamaModelInfo { Name = modelName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model info for {ModelName}", modelName);
            return new OllamaModelInfo { Name = modelName };
        }
    }



    public async Task<bool> IsOllamaRunningAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<OllamaStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new OllamaStatus();

        try
        {
            var response = await _httpClient.GetAsync("/api/version");
            status.IsRunning = response.IsSuccessStatusCode;

            if (status.IsRunning)
            {
                var versionJson = await response.Content.ReadAsStringAsync();
                var versionObj = JsonSerializer.Deserialize<Dictionary<string, string>>(versionJson);
                status.Version = versionObj?.GetValueOrDefault("version") ?? "Unknown";

                var models = await GetAvailableModelsAsync();
                status.AvailableModels = models.Select(m => m.Name).ToList();
            }
        }
        catch (Exception ex)
        {
            status.IsRunning = false;
            status.Error = ex.Message;
        }

        return status;
    }


    private class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModel> Models { get; set; } = new();
    }
}
