# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

---

## [1.0.0] - 2024-01-15

### Added

#### Core Features

- **Product management** - CRUD operations with batch tracking, expiry dates, price history, barcode/QR scanning, specification PDF attachments
- **Storage location management** - Hierarchical structure (warehouse, room, storage spot)
- **Category management** - Products organized by categories with ML-powered auto-suggestion
- **Stock movements** - Incoming/outgoing movements with full audit trail
- **Multi-warehouse** - Multiple warehouses with per-user access control
- **User management** - 4 roles: SuperAdmin, Admin, Manager, User

#### Security

- Cookie-based authentication with Blazor circuit tracking
- API key authentication for REST endpoints
- Two-factor authentication (TOTP)
- WebAuthn / Passkey support
- Magic link and email OTP login
- Trusted device management with browser fingerprinting
- Pwned password checking (Have I Been Pwned API)
- IP-based access rules per user
- Session management with real-time monitoring and remote termination
- Tamper-proof audit logging with hash chain verification
- Rate limiting (custom middleware + ASP.NET Core built-in)
- Security headers middleware (CSP, HSTS, X-Frame-Options)
- VPN detection via configurable subnet matching

#### Machine Learning

- Anomaly detection (Randomized PCA on user behavior)
- Security risk scoring per user
- Category prediction (SDCA text classification + keyword fallback with 2500+ keywords across 33 categories)
- Dedicated Blazor dashboards for each ML feature

#### Backup and Restore

- Automatic scheduled backups with configurable intervals
- 7 backup providers: Local, Network Share, Azure Blob, AWS S3, Google Drive, OneDrive, Cloudflare R2
- Encryption key backup with separate schedule
- JSON-based backup/restore
- Full database restore

#### Notifications

- In-app notification bell with real-time updates
- Email notifications (SMTP)
- Microsoft Teams webhook integration
- Configurable notification channels per alert type
- Security alert system (burst attacks, brute force, DDoS, slow-rate detection)

#### UI/UX

- Interactive dashboard with ApexCharts
- Application insights (page views, API requests, performance metrics)
- PWA with service worker and offline support
- Responsive design with mobile support
- Gamification system (user achievements)
- Team presence indicators
- Dark/light theme
- Keyboard shortcuts
- Drag-and-drop interactions

#### Deployment

- Windows Service support
- IIS deployment with web.config
- Self-contained publish

### Fixed

- Charts not rendering (missing `@rendermode InteractiveServer`)
- ApexCharts options parameter rendering issues
- Cache SizeLimit exception (missing `Size` property)
- Mobile zoom issue (viewport meta-tag + 16px font-size)
- Logout button hidden behind navigation on mobile
- Entity tracking conflicts in repositories
- Camera switch and stream handling

### Changed

- Product-StorageLocation relationship changed from one-to-many to many-to-many via `ProductStorageLocations`
- Dashboard layout switched to Bootstrap Cards
- Mobile navigation uses JavaScript instead of Blazor toggle

### Deprecated

- `Product.StorageLocationId` - use `ProductStorageLocations` instead
- `StorageLocation.Products` - use `ProductStorageLocations` instead

---

## [0.9.0] - 2024-01-10

- Beta release, feature-complete
- Bug fixes and performance optimization

## [0.8.0] - 2024-01-05

- Alpha release with core features
- Internal testing

## [0.1.0] - 2023-12-01

- Initial development and proof of concept

---

## Known Issues

- Chart tooltips may truncate very long names
- Barcode scanner does not work in Firefox Private Mode (browser limitation)
- iOS Safari: Service worker may not register on first load (reload to fix)

---

## Contributors

- **Lukas** - Lead Developer

---

## Links

- [Repository](https://github.com/lukislp/Lagersystem)
