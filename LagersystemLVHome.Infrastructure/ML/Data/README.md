# ML Models Directory

This directory contains the trained Machine Learning models for LagerSystem.

## Models

### anomaly-detection-model.zip

- **Purpose**: Detection of unusual user behavior
- **Algorithm**: Randomized PCA (Principal Component Analysis)
- **Training data**: AuditLogs from the last 180 days
- **Minimum requirement**: 100 AuditLog entries

### security-risk-model.zip

- **Purpose**: Security risk assessment per user
- **Algorithm**: Binary Classification (FastTree)
- **Training data**: Aggregated user behavior features
- **Minimum requirement**: 50 active users

### category-prediction-model.zip

- **Purpose**: Automatic product categorization
- **Algorithm**: Text Classification (SDCA)
- **Training data**: Categorized products
- **Minimum requirement**: 50 categorized products

> **Note:** Not all model files may be present initially. Models are created when training is triggered from the ML dashboards (SuperAdmin only).

## Publish Behavior

ML models are **automatically included in published output**:

```xml
<ItemGroup>
    <Content Include="ML\Data\*.zip" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

Benefits:
- No training required immediately after deployment
- Models are ready to use on new servers
- Consistent model versions across deployments
- Faster initial setup with no training delay

### Deployment Process

1. **Before publish** (optional): Train models on local environment
2. **Publish**: `dotnet publish -c Release -r win-x64 --self-contained`
3. **After deployment**: Models are immediately available in `ML/Data/`. Optionally retrain with production data.

## Model Updates

### Manual Training on Production

1. Log in as **SuperAdmin**
2. Navigate to the ML dashboards
3. Train individual or all models
4. Models are overwritten automatically

### Automatic Backup Before Training

The services create backups automatically:

```
ML/Data/anomaly-detection-model.zip.backup
ML/Data/security-risk-model.zip.backup
```

### Model Versioning

Recommended for production systems:

```bash
git add ML/Data/*.zip
git commit -m "ML Models v1.0.0 - Trained with 10k samples"
git tag ml-models-v1.0.0
```
