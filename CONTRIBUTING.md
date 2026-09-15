# Contributing to Lagersystem

Thanks for taking the time. Lagersystem is a single-maintainer project, so the process is
deliberately small - but it is the same for every change, including the maintainer's own.

## How changes get in

1. Open an issue first for anything bigger than a typo or an obvious bug fix, so the direction can
   be agreed before you spend time on it. Use the templates under `.github/ISSUE_TEMPLATE/`.
2. Fork the repository (or branch, if you have write access) and make your change on a branch.
3. Open a pull request against `main`. The pull-request template asks for what changed and why.
4. `main` is protected: a PR merges only after the test stage of
   [`.github/workflows/ci-cd.yml`](.github/workflows/ci-cd.yml) is green and the branch is up to
   date with `main` (enable auto-merge and it lands on its own once that is the case). Nobody
   pushes to `main` directly, not even the maintainer.

## What a pull request needs

- **Conventional Commits.** The version and the changelog are generated from the commit messages
  (`feat:` = minor release, `fix:` = patch release, `build:`/`ci:`/`docs:`/`test:` = no release).
  Squash-merge keeps the PR title as the commit message, so give the PR a Conventional Commit
  title.
- **Green required checks.** `test-unit`, `test-lint`, `test-security`,
  `test-db-providers (sqlite)`, `test-db-providers (postgresql)`, `build` and
  `review / dependency-review` are required; a red one blocks the merge.
- **Tests for new functionality.** New features and bug fixes come with tests in
  `LagersystemLVHome.UnitTests` (laid out as `Common/`, `Data/`, `HostedServices/`,
  `Infrastructure/`, `ML/`, `Services/`, `Utilities/`). A PR that adds behaviour without a test is
  asked to add one. The coverage badge in the README is regenerated from the merged coverage
  report on every release and is expected not to drop.
- **Formatting and warnings.** `test-lint` runs
  `dotnet format LagersystemLVHome.sln --verify-no-changes --exclude-diagnostics CA1848`; run
  `dotnet format LagersystemLVHome.sln` before pushing. CA1848 is excluded because it has no
  Roslyn code fix and otherwise produces false "would be formatted" hits. Warnings are errors
  repo-wide (`Directory.Build.props`, with CA1848 in `WarningsNotAsErrors`) - do not silence one
  without saying why in the PR.
- **Both database providers.** The app must start against SQLite *and* PostgreSQL:
  `test-db-providers` boots it with each provider and waits for `/healthz`. A change to the data
  layer that only works on one of them fails here. Schema changes come with the matching EF Core
  migration.
- **Lock files.** Projects carry a `packages.lock.json` and CI restores with `--locked-mode`, so a
  csproj that disagrees with its lock file fails the restore instead of silently updating it. A
  plain `dotnet restore LagersystemLVHome.sln` refreshes them locally - commit the result.
- **Vulnerable packages.** `test-security` fails the build on known High or Critical NuGet
  advisories. One advisory (`GHSA-2m69-gcr7-jv3q`, SQLitePCLRaw) is accepted and documented in
  `Directory.Build.props`.

## Running things locally

`global.json` pins the .NET SDK to `10.0.100` (rolling forward to the latest feature band).
PostgreSQL 14+ is recommended; SQLite is the default fallback.

```bash
cp LagersystemLVHome/appsettings.Example.json LagersystemLVHome/appsettings.json
cd LagersystemLVHome
dotnet run
```

Navigate to `https://localhost:7239`; the setup wizard creates the tables and the initial
SuperAdmin.

On Linux the ML and reporting code pulls in SkiaSharp, which needs native libraries before the
tests will run:

```bash
sudo apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 fonts-liberation
```

Restore the local tools (`dotnet-ef`, `reportgenerator`, pinned in `.config/dotnet-tools.json`)
with `dotnet tool restore`. The same commands CI runs:

```bash
dotnet test LagersystemLVHome.UnitTests/LagersystemLVHome.UnitTests.csproj --configuration Release
dotnet format LagersystemLVHome.sln --verify-no-changes --exclude-diagnostics CA1848
dotnet restore LagersystemLVHome.sln --locked-mode
dotnet build LagersystemLVHome.sln --configuration Release --no-restore
```

## Security issues

Please do not open a public issue for a vulnerability - use the private reporting path described
in [SECURITY.md](SECURITY.md). The [Code of Conduct](CODE_OF_CONDUCT.md) applies to every
interaction in this repository.
