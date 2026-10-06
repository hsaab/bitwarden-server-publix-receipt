# Candidate A: net8.0 to net10.0 pin upgrade

## Approach
Move the whole pin to net10.0 in one change: `Directory.Build.props`, the ten csproj files that repeat `net8.0`, `global.json` (`10.0.100` with `rollForward` `latestFeature`), and the Microsoft 8.0 framework packages those projects restore. Warnings stay errors, so the six `new X509Certificate2(...)` sites move to `X509CertificateLoader` in that same change. Dockerfiles, `.vscode/launch.json`, `dev/generate_openapi_files.ps1`, and the `dotnet-ef` 8.0.8 tool pin stay untouched because Core.Test does not need them.

The decision everything else follows: acceptance is `dotnet build` / `dotnet test` of `test/Core.Test/Core.Test.csproj`, plus a build of `src/Api` and `src/Infrastructure.EntityFramework`, which pull the ASP.NET and EF packages. `dotnet build bitwarden-server.sln` is not the bar. That solution builds `src/Sql/Sql.sqlproj` and can run `cargo` through `util/RustSdk`.

## Why this over the obvious alternative
The obvious alternative is a thin pin edit: `Directory.Build.props`, the ten overrides, and `global.json`, plus `SYSLIB0057` added to `WarningsNotAsErrors` so existing constructors still compile. I rejected that. The Microsoft.AspNetCore, Microsoft.Extensions, EntityFrameworkCore, and System.Text.Json 8.0 packages are the ones this pin restores, and the working hypothesis is that they have no net10.0 assets (NU1202). Even if NuGet accepted a net8.0 asset through framework fallback, EF Core 8 and ASP.NET Core 8 on a net10.0 compile are the wrong pin and fail when shared-framework types diverge. A warning suppression would also leave obsolete certificate constructors in Core and SSO under a repo that already fails the build on warnings.

## Slices
### 1. Retarget the pin to net10.0
- exception: These edits cannot be split into a compiling tree. A net10.0 TFM against the current 8.0 framework packages fails restore if the NU1202 hypothesis holds, and a net10-only framework package fails restore against the current net8.0 TFM. `X509CertificateLoader` does not exist on net8.0, so the certificate edit cannot land first. Shipping the SDK bump alone also fails on a machine that only has the 10.0 SDK, because the projects would still ask for the net8.0 targeting pack.
- scope:
  - `Directory.Build.props` (TargetFramework only; test package versions at lines 23-35 stay)
  - `global.json` (`sdk.version` only; Microsoft.Build.Traversal 4.1.0, Microsoft.Build.Sql 1.0.0, and Bitwarden.Server.Sdk 1.5.1 stay)
  - TFM overrides: `test/Setup.Test/Setup.Test.csproj`, `test/Infrastructure.Dapper.Test/Infrastructure.Dapper.Test.csproj`, `test/Events.IntegrationTest/Events.IntegrationTest.csproj`, `test/Core.IntegrationTest/Core.IntegrationTest.csproj`, `util/SeederUtility/SeederUtility.csproj`, `util/SeederApi/SeederApi.csproj`, `util/RustSdk/RustSdk.csproj`, `util/Seeder/Seeder.csproj`, `bitwarden_license/test/SSO.Test/SSO.Test.csproj`, `bitwarden_license/test/Sso.IntegrationTest/Sso.IntegrationTest.csproj`
  - Package bumps, only where these PackageReference ids already appear: `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`, `src/Core/Core.csproj`, the four migration csproj files that pin Microsoft.EntityFrameworkCore.Design at `[8.0.8]`, and the other csproj files in the 18 groups that reference the packages listed below
  - Certificate call sites: `src/Core/Utilities/CoreHelpers.cs`, `src/Api/Auth/Models/Request/OrganizationSsoRequestModel.cs`, `bitwarden_license/src/Sso/Utilities/DynamicAuthenticationSchemeProvider.cs`, `test/Setup.Test/ProgramTests.cs`
  - No lockfile and no generated snapshot is expected. Do not add `packages.lock.json`.
- change:
  - Set TargetFramework to `net10.0` in `Directory.Build.props` and in each of the ten overrides. Leave `TreatWarningsAsErrors`, ImplicitUsings, and Nullable as they are. Do not add a LangVersion.
  - Set `global.json` sdk to `10.0.100` and keep `rollForward` `latestFeature`. Leave the three msbuild-sdks. If restore of a host project fails inside Bitwarden.Server.Sdk before compilation, bump that SDK to the first release that supports net10.0, in this same slice. If nuget has no such release, stop.
  - Bump framework packages to exact brackets on the 10.0 line, newest stable 10.0 patch per family, same style as `[8.0.8]`. One patch per family. Do not use floating versions. Families: Microsoft.EntityFrameworkCore.Relational, SqlServer, Sqlite, and Design; Microsoft.AspNetCore.DataProtection, Authentication.JwtBearer, SignalR, and Mvc.Testing; Microsoft.Extensions packages currently at 8.0.0, 8.0.1, or 8.0.10; System.Text.Json in Core; Microsoft.Extensions.TimeProvider.Testing. Move Npgsql.EntityFrameworkCore.PostgreSQL and Pomelo.EntityFrameworkCore.MySql to the releases built for EF Core 10. If Pomelo has no EF Core 10 release, stop. Do not bump any other PackageReference unless restore reports NU1202 for it.
  - At each of the six `X509Certificate2` constructors, switch to the `X509CertificateLoader` method that matches the arguments already at that call. A password or key-storage flags means `LoadPkcs12` or `LoadPkcs12FromFile`. A certificate blob or file means `LoadCertificate` or `LoadCertificateFromFile`. Pass through the password and flags already present. Where flags were omitted, pass `X509KeyStorageFlags.Default`. Do not change validation, storage, or logging around the calls. Do not add a SYSLIB pragma and do not extend `WarningsNotAsErrors`.
  - Fix only compile errors that are mechanical renames caused by these package bumps. Stop if a failure changes runtime behavior (serializer settings, auth handler contracts, EF query semantics).
- journeys:
  - A reviewer opens the diff and sees `net8.0` become `net10.0` in `Directory.Build.props`, the ten overrides, and the SDK pin in `global.json`, with the Microsoft 8.0 framework packages moved to 10.0 brackets.
  - A developer restores and builds this pin and gets a successful compile of Core.Test, Api, and Infrastructure.EntityFramework.
  - A developer runs `test/Core.Test` and the suite finishes.
- verify:
  - `dotnet --list-sdks` includes a 10.0 SDK (install `10.0.100` if the machine has none).
  - `dotnet restore test/Core.Test/Core.Test.csproj`
  - `dotnet build test/Core.Test/Core.Test.csproj`
  - `dotnet build src/Api`
  - `dotnet build src/Infrastructure.EntityFramework`
  - `dotnet test test/Core.Test/Core.Test.csproj`
  - Do not require `dotnet build bitwarden-server.sln`, `dotnet test ./test`, or a Rust/cargo build.
- estimate: 160 reviewable lines

## Risks
- NU1202 is still a hypothesis: restore fails on a PackageReference outside the bump list, or a 10.0 package publishes no net10.0 asset. Early warning: the first `dotnet restore` names a package id the slice did not list.
- Pomelo or Npgsql has no build that depends on EF Core 10, so Infrastructure.EntityFramework cannot restore. Early warning: nuget shows no provider version whose dependency is EF Core 10.
- Bitwarden.Server.Sdk 1.5.1 or Microsoft.Build.Sql 1.0.0 does not load under the 10.0 SDK. Early warning: host restore fails inside that SDK, before any project code compiles.
- `X509CertificateLoader` is not a drop-in: the old constructor sniffed certificate versus PKCS#12, and the loader methods do not. Early warning: a call site takes a byte array or path and the surrounding method also accepts PFX payloads.
- New net10 analyzer warnings, treated as errors, spread past the six certificate sites and past the line budget. Early warning: the first build reports warning ids other than SYSLIB0057 across many files.
- EF Core 10 breaks call sites inside Infrastructure.EntityFramework beyond the version attributes. Early warning: build errors in LINQ, migrations, or provider configuration after the package bump.
- A reviewer builds `bitwarden-server.sln` and hits `Sql.sqlproj` or `cargo build` from RustSdk. Early warning: failure output is the SQL SDK or a missing `cargo`, while Core.Test already passed. That solution build stays outside this upgrade.

## Least sure about
Switching the six `X509Certificate2` constructors to `X509CertificateLoader` inside the upgrade, instead of adding SYSLIB0057 to `WarningsNotAsErrors`. I would change my mind if any call site passes bytes or a file that can be either a certificate or a PKCS#12, because the old constructor sniffed that content and the loader methods do not. In that case a one-line warning exclusion is safer than a behavior change in certificate import.
