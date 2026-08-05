# LagerSystem

[![CI/CD](https://github.com/lukislp/Lagersystem/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/lukislp/Lagersystem/actions/workflows/ci-cd.yml)
[![Release](https://img.shields.io/github/v/release/lukislp/Lagersystem)](https://github.com/lukislp/Lagersystem/releases)
[![License: AGPL-3.0](https://img.shields.io/github/license/lukislp/Lagersystem)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/lukislp/Lagersystem/main/.github/badges/coverage.json)](https://github.com/lukislp/Lagersystem/actions/workflows/ci-cd.yml)

A self-hosted inventory management system built with **Blazor Server** on **.NET 10**. Designed for home and small-business use with enterprise-grade security, multi-database support, machine learning integration, and comprehensive backup capabilities.

---

## Features

### Core Inventory

- **Products** - Full CRUD with batch tracking, expiry dates, price history, barcode/QR scanning, and specification PDF attachments
- **Categories** - Hierarchical product categorization with ML-powered auto-suggestion
- **Storage Locations** - Organize products across warehouses, rooms, and named storage spots
- **Stock Movements** - Track all incoming/outgoing movements with audit trail
- **Multi-Warehouse** - Support for multiple warehouses with per-user access control

### Security and Authentication

- Custom Blazor Server authentication (cookie-based with circuit tracking)
- API key authentication for REST endpoints
- Two-factor authentication (TOTP via Google Authenticator)
- WebAuthn / Passkey support for passwordless login
- Magic link and email OTP login options
- Trusted device management with browser fingerprinting (Thumbmarkjs)
- Pwned password checking (Have I Been Pwned API)
- IP-based access rules per user
- Session management with real-time monitoring and remote termination
- Tamper-proof audit logging with hash chain verification
- Rate limiting (custom middleware + ASP.NET Core built-in)
- Security headers middleware (CSP, HSTS, X-Frame-Options, etc.)
- VPN detection via configurable subnet matching

### Machine Learning (ML.NET)

- **Anomaly Detection** - Identifies unusual user behavior patterns using Randomized PCA
- **Security Risk Scoring** - Assesses per-user risk levels based on behavioral features
- **Category Prediction** - Auto-categorizes products using SDCA text classification with keyword-based fallback (2500+ keywords across 33 categories)
- Dedicated Blazor dashboards for each ML feature
- Models persist across deployments (included in publish output)

See [ML/README.md](LagersystemLVHome.Infrastructure/ML/README.md) for setup details and usage examples.

### Backup and Restore

- Automatic scheduled backups with configurable intervals
- **7 backup providers**: Local, Network Share, Azure Blob Storage, AWS S3, Google Drive, OneDrive, Cloudflare R2
- Automatic backup cleanup with configurable retention
- Encryption key backup with separate schedule
- JSON-based backup/restore (no external database tools required)
- Full database restore from any backup

### Notifications and Alerts

- In-app notification bell with real-time updates
- Email notifications (SMTP with template support)
- Microsoft Teams webhook integration
- Configurable notification channels per alert type (low stock, expiry, security, system, weekly reports, password reset)
- Security alert system (burst attacks, brute force, DDoS, slow-rate detection)

### Reporting and Analytics

- Interactive dashboard with ApexCharts
- Application insights (page views, API requests, performance metrics)
- Audit log dashboard with tamper-proof verification
- GDPR cleanup dashboard with automated data retention
- Weekly PDF reports (QuestPDF)
- Export to Excel (ClosedXML) and CSV
- Data import with validation

### Additional Features

- Gamification system (user achievements and stats)
- Team presence indicators (online/away/busy)
- Ollama AI assistant integration (local LLM support)
- GeoLocation via MaxMind GeoIP2 (see [GeoData/README.md](LagersystemLVHome/GeoData/README.md))
- Cloudflare integration (bot protection, DDoS mitigation, geo-blocking, analytics)
- Progressive Web App (PWA) with service worker
- Responsive design with mobile support (touch gestures, camera scanning)
- Configurable privacy policy (GDPR-compliant, German law focus)
- Dark/light theme support
- Keyboard shortcuts
- Drag-and-drop interactions

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | .NET 10, C# 14, Blazor Server (Interactive SSR) |
| Database | PostgreSQL (primary), MySQL, SQLite (portable fallback) |
| ORM | Entity Framework Core 10 with `IDbContextFactory` |
| ML | ML.NET 5.0 (anomaly detection, classification, vision) |
| Charts | Blazor-ApexCharts |
| PDF | QuestPDF |
| Excel | ClosedXML |
| Barcode/QR | ZXing.Net, QRCoder |
| Image Processing | SixLabors.ImageSharp, SkiaSharp |
| Cloud Storage | Azure.Storage.Blobs, AWSSDK.S3, Google.Apis.Drive.v3, Microsoft.Graph (OneDrive) |
| Caching | In-memory (default), Redis (optional via StackExchange.Redis) |
| Authentication | Custom Blazor + API key schemes, WebAuthn, TOTP (Otp.NET, GoogleAuthenticator) |
| Fingerprinting | Soenneker.Blazor.Thumbmarkjs |
| GeoIP | MaxMind.GeoIP2 |
| API Docs | Microsoft.AspNetCore.OpenApi + Scalar |
| Hosting | Kestrel, IIS, Windows Service |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A supported database:
  - **PostgreSQL 14+** (recommended)
  - **MySQL 8.0+**
  - **SQLite** (no installation required, default fallback)
- Optional: [MaxMind GeoLite2](https://dev.maxmind.com/geoip/geolite2-free-geolocation-data) database for IP geolocation
- Optional: [Ollama](https://ollama.com) for local AI assistant

---

## Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/lukislp/Lagersystem.git
cd Lagersystem
```

### 2. Configure the Application

Copy the example configuration and adjust settings:

```bash
cp LagersystemLVHome/appsettings.Example.json LagersystemLVHome/appsettings.json
```

Edit `appsettings.json` and set your database connection string. By default, the application falls back to SQLite if no connection string is configured.

### 3. Run the Application

```bash
cd LagersystemLVHome
dotnet run
```

Navigate to `https://localhost:7239`. On first launch, the setup wizard creates the database tables and prompts for the initial SuperAdmin account.

### 4. GeoIP Setup (Optional)

For IP-based geolocation features (session info, security dashboards):

1. Create a free account at [MaxMind](https://www.maxmind.com/en/geolite2/signup)
2. Download `GeoLite2-City.mmdb`
3. Place it in `LagersystemLVHome/GeoData/`

See [GeoData/README.md](LagersystemLVHome/GeoData/README.md) for detailed instructions.

---

## Database Providers

Configure the provider in `appsettings.json` under `DatabaseSettings`:

**PostgreSQL (recommended):**
```json
{
    "DatabaseSettings": {
        "Provider": "PostgreSQL",
        "ConnectionString": "Host=localhost;Database=Lagersystem;Username=postgres;Password=PLACEHOLDER;"
    }
}
```

**MySQL:**
> **Known issue:** currently broken on .NET 10 - [Pomelo.EntityFrameworkCore.MySql](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql) has no stable release for EF Core 10 yet (latest is `9.0.0`, targeting EF Core 9), which crashes the app at startup with a `MissingMethodException`. Use PostgreSQL or SQLite until Pomelo ships a compatible version.
```json
{
    "DatabaseSettings": {
        "Provider": "MySQL",
        "ConnectionString": "Server=localhost;Database=Lagersystem;User=root;Password=PLACEHOLDER;"
    }
}
```

**SQLite (no server required):**
```json
{
    "DatabaseSettings": {
        "Provider": "SQLite",
        "ConnectionString": "Data Source=inventory.db"
    }
}
```

Tables are created automatically on first run via `EnsureCreatedAsync()`.

---

## User Roles

| Role | Permissions |
|---|---|
| **SuperAdmin** | Full system access: warehouse management, user administration, ML training, security monitoring, system settings |
| **Admin** | Full access within assigned warehouse: users, products, storage, audit logs |
| **Manager** | Product management, stock movements, batch tracking, reports (read), notifications |
| **User** | View products, book stock movements, scanner access, receive notifications |

---

## REST API

The application exposes a REST API at `/api/*` endpoints, secured with API key authentication. API keys are managed per user in the profile settings.

**Authentication:** Include `X-API-Key: <your-key>` in request headers.

**Available endpoints:**

| Resource | Endpoints |
|---|---|
| Products | `GET/POST /api/products`, `GET/PUT/DELETE /api/products/{id}`, `GET /api/products/barcode/{barcode}` |
| Categories | `GET/POST /api/categories`, `GET/PUT/DELETE /api/categories/{id}` |
| Storage Locations | `GET/POST /api/storage-locations`, `GET/PUT /api/storage-locations/{id}` |
| Movements | `GET/POST /api/movements` |
| Batches | `GET/POST /api/batches`, `GET /api/batches/expiring` |
| Dashboard | `GET /api/dashboard` |
| Analytics | `GET /api/analytics/stats`, `GET /api/analytics/trends` |
| Sensors | `GET /api/sensors/all`, `GET /api/sensors/low-stock`, `GET /api/sensors/expiring` |
| Warehouses | `GET/POST /api/warehouses`, `GET/PUT/DELETE /api/warehouses/{id}` |
| Rooms | `GET/POST /api/rooms`, `GET/PUT/DELETE /api/rooms/{id}` |
| Alerts | `GET /api/alerts` |
| Notifications | `GET /api/notifications` |
| Audit Logs | `GET /api/audit-logs` |
| Search | `GET /api/search` |
| Users | `GET /api/users` (Admin) |

In development mode, interactive API documentation is available at `/scalar/v1` (powered by Scalar).

---

## Project Structure

```
LagersystemLVHome/                     Web/UI project (Blazor Server host)
    API/                        REST API controllers, DTOs, and mappers
    Authentication/             API key authentication handler
    Components/
        Layout/                 MainLayout
        Pages/
            Admin/              Admin dashboards
            Auth/               Login, register, password reset
            Backup/             Backup management, database restore
            Inventory/          Products, categories, storage, movements
            Reports/            Insights, audit, GDPR, export/import
            Security/           Security center, sessions, rate limiting
            Settings/           User profile, app settings, notifications
        Shared/                 Reusable Blazor components
    Configuration/               Strongly-typed settings classes
    Controllers/                 MVC controllers
    GeoData/                     MaxMind GeoIP database (not in repo)
    Middleware/                  Security headers, rate limiting, Cloudflare, session validation
    wwwroot/                     Static files (CSS, JS, PWA manifest)
    Pass/                        Encrypted database password (not in repo)
    keys/                        Data protection keys (not in repo)

LagersystemLVHome.Domain/              Entities and shared domain types
    Models/                      Entity Framework entity classes

LagersystemLVHome.Data/                Data access layer
    Repositories/                Repository pattern implementations
    InventoryDbContext.cs        EF Core database context

LagersystemLVHome.Application/         Business logic and services
    Services/
        Auth/                    Authentication, authorization, 2FA, WebAuthn
        Backup/                  Backup/restore, key backup, provider configs
        BackupProviders/         Cloud backup provider implementations
        Cache/                   Memory and Redis caching
        Database/                Database provider, health checks
        Integration/             Cloudflare, GeoIP, Ollama, Teams presence
        Inventory/               Products, categories, storage, pricing, barcode
        Notification/            Email, Teams, in-app notifications
        Reporting/               PDF reports, Excel export, insights, dashboard
        Security/                Audit, GDPR, encryption, rate limiting, alerts
        Session/                 Session management, monitoring, trusted devices
        UI/                      Camera, toast, keyboard shortcuts, gamification
    Utilities/                    Helper classes and converters

LagersystemLVHome.Infrastructure/      Cross-cutting infrastructure
    Data/                         Circuit handlers for Blazor Server
    HostedServices/               Background services (backup, cleanup, monitoring)
    ML/
        Components/               ML-specific Blazor dashboards
        Keywords/                 Category keyword definitions (33 categories)
        Models/                   ML data models
        Services/                 ML.NET service implementations
        Data/                     Trained ML model files
    Services/                     Additional infrastructure services (reporting, etc.)
```

---

## Configuration Reference

All settings are documented in `appsettings.Example.json`. Key sections:

| Section | Purpose |
|---|---|
| `DatabaseSettings` | Database provider and connection string |
| `CacheSettings` | Memory/Redis cache configuration |
| `DashboardSettings` | Dashboard refresh intervals and defaults |
| `EmailSettings` | SMTP server for email notifications |
| `TeamsSettings` | Microsoft Teams webhook integration |
| `NotificationChannels` | Per-alert-type notification routing |
| `GdprSettings` | Automated data retention and cleanup schedules |
| `PerformanceSettings` | Response compression, output caching, pagination |
| `UISettings` | Drag-and-drop, keyboard shortcuts, toast duration |
| `ApiSettings` | OpenAPI toggle, API key requirement |
| `RateLimitSettings` | Rate limiting per role with endpoint overrides |
| `OllamaSettings` | Local LLM connection for AI assistant |
| `GeoIP` | MaxMind database path |
| `SecurityAlerts` | Burst, brute force, DDoS, slow-rate thresholds |
| `VpnDetection` | VPN subnet patterns |
| `CloudflareSettings` | Cloudflare integration (bot, DDoS, geo, WAF) |
| `PrivacyPolicySettings` | GDPR privacy policy content |
| `WebAuthn` | Passkey relying party configuration |

### Secrets

Real secrets are **never** committed:

1. Copy `appsettings.Example.json` to `appsettings.Development.json`
   (gitignored).
2. Leave `Password=PLACEHOLDER` in the connection string. The real
   password is stored AES-256 encrypted in `Pass/db.password.enc` and
   decrypted at startup by `SecureConnectionStringProvider`.
3. Run `.\setup-database-password.ps1` once to seed the encryption key
   and the encrypted blob.
4. For production prefer environment variables or a secret store
   (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) over plain
   JSON configuration files.

---

## Deployment


### IIS

```powershell
dotnet publish -c Release -r win-x64 --self-contained
```

Copy the published output to your IIS site directory. A `web.config` is included for IIS hosting configuration.

### Windows Service

The application supports `UseWindowsService()` out of the box:

```powershell
dotnet publish -c Release -r win-x64 --self-contained
sc create LagerSystem binPath="C:\path\to\LagersystemLVHome.exe"
sc start LagerSystem
```

### Production Configuration

Create `appsettings.Production.json` for production-specific settings (HTTPS endpoints, external SMTP, Cloudflare, etc.). See `appsettings.Example.json` for all available options.

---

## Design Patterns

- **Repository Pattern** - Data access abstraction (`IProductRepository`, `ICategoryRepository`, etc.)
- **Service Layer** - Business logic encapsulation with interface-based DI
- **Factory Pattern** - Backup provider selection (`BackupProviderFactory`), database provider configuration
- **DbContext Factory** - Thread-safe database access for Blazor Server (`IDbContextFactory<InventoryDbContext>`)
- **Circuit Handlers** - Blazor Server lifecycle management for sessions and authentication
- **Hosted Services** - Background tasks (backup scheduling, GDPR cleanup, session cleanup, security monitoring, weekly reports)

---

## Repository

https://github.com/lukislp/Lagersystem

---

## License

AGPL-3.0 - see [LICENSE](LICENSE) for details.
