# ML.NET Integration

## NuGet Packages

The following packages are included in the project file:

```xml
<PackageReference Include="Microsoft.ML" Version="5.0.0" />
<PackageReference Include="Microsoft.ML.Vision" Version="5.0.0" />
<PackageReference Include="Microsoft.ML.ImageAnalytics" Version="5.0.0" />
<PackageReference Include="Microsoft.ML.TimeSeries" Version="5.0.0" />
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.12" />
```

## Directory Structure

```
ML/
    Models/                         ML data models
        AnomalyDetectionModels.cs
        SecurityRiskModels.cs
        CategoryPredictionModels.cs
    Services/                       ML service implementations
        IAnomalyDetectionService.cs
        AnomalyDetectionService.cs
        ISecurityRiskService.cs
        SecurityRiskService.cs
        ICategoryPredictionService.cs
        CategoryPredictionService.cs
    Keywords/                       Category keyword definitions
        ICategoryKeywordProvider.cs
        CategoryKeywordService.cs
        ElectronicsKeywords.cs
        ... (33 categories)
    Components/                     Blazor UI components
        AnomalyDashboard.razor
        SecurityRiskDashboard.razor
        CategorySuggestionPanel.razor
    Data/                           Trained models
        anomaly-detection-model.zip
        security-risk-model.zip
        category-prediction-model.zip
```

## Service Registration

Services are registered in `Program.cs`:

```csharp
builder.Services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();
builder.Services.AddScoped<ISecurityRiskService, SecurityRiskService>();
builder.Services.AddScoped<ICategoryPredictionService, CategoryPredictionService>();
builder.Services.AddSingleton<CategoryKeywordService>();
```

## Features

### 1. Anomaly Detection (AnomalyDetectionService)

- Detects unusual user behavior patterns
- Based on AuditLog data
- Trainable with historical data
- Real-time analysis

### 2. Security Risk Scoring (SecurityRiskService)

- Calculates risk score per user
- Identifies risk factors
- Provides actionable recommendations
- 4 risk levels: Low, Medium, High, Critical

### 3. Category Prediction (CategoryPredictionService)

- Suggests categories automatically for products
- NLP-based text analysis with ML.NET SDCA
- Fallback to keyword-based matching (2500+ keywords across 33 categories)
- Auto-categorization for uncategorized products

## Usage Examples

### Anomaly Detection

```csharp
@inject IAnomalyDetectionService AnomalyService

var result = await AnomalyService.AnalyzeUserBehaviorAsync(userId);
if (result.IsHighRisk)
{
    // User has suspicious behavior
    Console.WriteLine($"Risk Score: {result.AnomalyScore}");
}
```

### Security Risk Scoring

```csharp
@inject ISecurityRiskService SecurityService

var assessment = await SecurityService.AssessUserRiskAsync(userId);
if (assessment.RiskLevel >= RiskLevel.High)
{
    foreach (var recommendation in assessment.Recommendations)
    {
        Console.WriteLine(recommendation);
    }
}
```

### Category Prediction

```csharp
@inject ICategoryPredictionService CategoryService

var result = await CategoryService.SuggestCategoriesAsync("Logitech MX Master 3");
var bestCategory = result.BestMatch;
```

## Model Training

### Initial Training

All models must be trained once with sufficient data:

```csharp
// Anomaly detection (requires min. 100 AuditLogs)
await AnomalyService.TrainModelAsync();

// Security risk scoring (requires min. 50 users)
await SecurityService.TrainModelAsync();

// Category prediction (requires min. 50 categorized products)
await CategoryService.TrainModelAsync();
```

Training can also be triggered from the Blazor ML dashboards (SuperAdmin only).

## Performance Notes

- ML predictions are fast (< 50ms)
- Training can take 1-10 minutes depending on data volume
- Image processing is resource-intensive
- More training data produces better predictions

## Model Storage

Trained models are stored in `ML/Data/` and included in published output automatically. See [Data/README.md](Data/README.md) for details on model management and versioning.
