# [1.3.0](https://github.com/lukislp/Lagersystem/compare/v1.2.6...v1.3.0) (2026-08-24)


### Features

* **ci:** multi-arch images via native per-arch builds ([0407689](https://github.com/lukislp/Lagersystem/commit/04076895ca6c05ec6420bf901ae1879854368e01))

## [1.2.6](https://github.com/lukislp/Lagersystem/compare/v1.2.5...v1.2.6) (2026-08-08)


### Bug Fixes

* detect real iPhone user agents as iOS instead of macOS ([46407d8](https://github.com/lukislp/Lagersystem/commit/46407d8985916e2b0e9988accc7e5a8888856d82))
* persist audit log Severity and the Entity alias field ([1c0bb52](https://github.com/lukislp/Lagersystem/commit/1c0bb5284754024a37a400a3e7c90c0e373dd16a))
* resolve linked device fingerprints via active session lookup ([e742b80](https://github.com/lukislp/Lagersystem/commit/e742b800c9504e1519563576da81ca407924516d))
* show idle and away teammates in the presence list ([9f17716](https://github.com/lukislp/Lagersystem/commit/9f17716542164d5c1fe3d4a9bfa1c10879537bc0))
* surface custom presence status set before a session is first read ([20e1e51](https://github.com/lukislp/Lagersystem/commit/20e1e51b009984b2271ff5b59a8db70e175333ef))
* sync Product.Price when a new price entry is added ([4fc9194](https://github.com/lukislp/Lagersystem/commit/4fc919401c41b7edf258d85bcc8c0e2247e36547))

## [1.2.5](https://github.com/lukislp/Lagersystem/compare/v1.2.4...v1.2.5) (2026-08-08)


### Bug Fixes

* eliminate the xUnit1031 warning tripping dotnet format's verify check ([8f14bfe](https://github.com/lukislp/Lagersystem/commit/8f14bfe7b7b17924694c582223745e4b6b720425))
* make two backup-provider tests pass on Linux CI, fix formatting ([fc28bb4](https://github.com/lukislp/Lagersystem/commit/fc28bb450fe653d0895e51a631ac3ff14f0b5275))
* raise unit-test coverage from 74.7% to 88.5% ([9097f42](https://github.com/lukislp/Lagersystem/commit/9097f42f8983e293e035223ab5cf7bbadf145420))
* sort the last remaining using directive System-first ([6d16186](https://github.com/lukislp/Lagersystem/commit/6d161867d70a1ff74ba56f780903ebe3e9212919))

## [1.2.4](https://github.com/lukislp/Lagersystem/compare/v1.2.3...v1.2.4) (2026-08-07)


### Bug Fixes

* add a dashboard screenshot to the README ([c873817](https://github.com/lukislp/Lagersystem/commit/c873817570e4aceda660ec66b882824f8382c97b))
* eliminate a Progress<T> race in the JSON restore round-trip test ([c3d4174](https://github.com/lukislp/Lagersystem/commit/c3d4174fc9bee66f8921618c21cb4964fe34446d))

## [1.2.3](https://github.com/lukislp/Lagersystem/compare/v1.2.2...v1.2.3) (2026-08-07)


### Bug Fixes

* address production bugs found by the coverage test suites ([e8f2209](https://github.com/lukislp/Lagersystem/commit/e8f2209bdb6ccf41071cf808464f1f3fb54af302))
* re-trigger CI after a run got stuck queued during the GitHub Actions incident ([be3dc07](https://github.com/lukislp/Lagersystem/commit/be3dc07722699dfa9ae9c103b060860e4667491b))
* stabilize anomaly-detection training and remove time-of-day flakiness from risk scoring test ([4d72cc3](https://github.com/lukislp/Lagersystem/commit/4d72cc30658a4397404196907e4cb3f09565ed03))

## [1.2.2](https://github.com/lukislp/Lagersystem/compare/v1.2.1...v1.2.2) (2026-08-06)


### Bug Fixes

* null category name no longer crashes the icon lookup ([6ffe3ee](https://github.com/lukislp/Lagersystem/commit/6ffe3ee2781de538f8d63322bab3689d5d9ca7fd))

## [1.2.1](https://github.com/lukislp/Lagersystem/compare/v1.2.0...v1.2.1) (2026-08-06)


### Bug Fixes

* exclude source-generator output from the coverage measurement ([796ecca](https://github.com/lukislp/Lagersystem/commit/796ecca4eb9508d1989a7f4530e6699364e7ef57))
* make the three delete-failure tests platform-independent ([07c541a](https://github.com/lukislp/Lagersystem/commit/07c541af978f1c53c34b82c614f9f5c02c4f65d3))
* re-guard the unix-mode restore lambda for the platform analyzer ([67e3050](https://github.com/lukislp/Lagersystem/commit/67e3050608f29540a29e6c110cf5c61e79095d7b))
* WebP uploads were always rejected by the signature validation ([c6f6a55](https://github.com/lukislp/Lagersystem/commit/c6f6a55383ddf08b4ed19a2d2915d241e75e84e0))

# [1.2.0](https://github.com/lukislp/Lagersystem/compare/v1.1.2...v1.2.0) (2026-08-05)


### Features

* add a self-hosted test coverage badge ([b9df689](https://github.com/lukislp/Lagersystem/commit/b9df6893939aef1b8192e9983f11b84310853f42))

## [1.1.2](https://github.com/lukislp/Lagersystem/compare/v1.1.1...v1.1.2) (2026-08-05)


### Bug Fixes

* surface build/release/license status via README badges ([1ae2cf9](https://github.com/lukislp/Lagersystem/commit/1ae2cf9f54f381c3c0143f46f7fda8806f1f091e))

## [1.1.1](https://github.com/lukislp/Lagersystem/compare/v1.1.0...v1.1.1) (2026-08-04)


### Bug Fixes

* exclude Blazor Server SignalR hub from setup-required redirect ([907d327](https://github.com/lukislp/Lagersystem/commit/907d327794c9ae9b72b9fea305ac8666085c5520))

# [1.1.0](https://github.com/lukislp/Lagersystem/compare/v1.0.0...v1.1.0) (2026-08-04)


### Bug Fixes

* install SkiaSharp native deps in CI and force consistent CRLF checkout ([f0f4006](https://github.com/lukislp/Lagersystem/commit/f0f40062f35fc82c243daff0c0502dfe09981dc1))
* make GeoLite2-City.mmdb build inclusion conditional on the file existing ([2b7fa8a](https://github.com/lukislp/Lagersystem/commit/2b7fa8a89f6d06dfb31ba8095354e57d8431920e))
* mark MySQL CI leg as known-broken instead of retrying a hard version mismatch ([6228439](https://github.com/lukislp/Lagersystem/commit/622843932b67e60f24ef7eac317412a6abe91ce1))
* retry MySQL server version auto-detection on transient startup failures ([29735d4](https://github.com/lukislp/Lagersystem/commit/29735d480018a63bb8ac601adea13957f6a6a426))
* separate build from app startup in test-db-providers, widen healthz window ([21d91e4](https://github.com/lukislp/Lagersystem/commit/21d91e477e26480d752fd6f87f6bf3bdd901128f))
* suppress CS8618 on InventoryDbContext's DbSet properties ([b32c7ee](https://github.com/lukislp/Lagersystem/commit/b32c7eecc3e911b938a6107e8df7748523d1ff5d))
* unblock CI test-lint, test-unit, and test-db-providers ([b9b2e6e](https://github.com/lukislp/Lagersystem/commit/b9b2e6e1aa375e3eef83944263c4d1c3a1c25623))


### Features

* add GitHub Actions CI/CD pipeline with automated releases and Docker publishing ([1c6c234](https://github.com/lukislp/Lagersystem/commit/1c6c234f7dfec3981e4f10afeea13a28145ae642))

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
