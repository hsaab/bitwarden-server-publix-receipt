# Candidate C: Upgrade the Bitwarden server pin from net8.0 to net10.0

## Approach
Ship one commit that moves the SDK pin, the shared TargetFramework, the ten project overrides, and the framework package majors together. A smaller split cannot restore: the 8.0.100 SDK cannot compile net10.0, the 10.0 framework packages generally refuse net8.0, and the 8.0 framework packages are expected to fail on net10.0 with NU1202. Leave Bitwarden.Server.Sdk at 1.5.1 and keep certificate construction behavior by extending the existing WarningsNotAsErrors policy, so the diff is the pin move plus the package majors required to make that pin compile.

## Why this over the obvious alternative
The obvious alternative is a two-step diff: retarget Directory.Build.props and global.json first, then bump packages after restore fails. There is no order in which both steps compile. A net10.0 TargetFramework with Microsoft.AspNetCore, Microsoft.Extensions, Entity Framework Core, and System.Text.Json still on 8.0 is the NU1202 case this pin has to survive, and those same packages on the 10.0 line generally do not restore while the tree is still net8.0. Rebasing onto upstream main is the other way to inherit a newer pin, and this task forbids that. Rewriting the six `X509Certificate2` constructors is the other tempting extra; the constructors still run, TreatWarningsAsErrors is what turns SYSLIB0057 into a failed build, and changing certificate loading is a behavior change this upgrade does not need.

## Slices
### 1. Move the pin to net10.0
- exception: The SDK pin, TargetFramework, and matching framework package majors are one compile. Splitting them leaves a tree that cannot restore, so this slice is the whole reviewable diff.
- scope:
  - `global.json` (sdk version only; leave `Microsoft.Build.Traversal` 4.1.0, `Microsoft.Build.Sql` 1.0.0, and `Bitwarden.Server.Sdk` 1.5.1)
  - `Directory.Build.props` (TargetFramework)
  - `Directory.Build.targets` (create only if the repo has no such file; otherwise add the warning append to the existing late import)
  - TargetFramework overrides: `test/Setup.Test/Setup.Test.csproj`, `test/Infrastructure.Dapper.Test/Infrastructure.Dapper.Test.csproj`, `test/Events.IntegrationTest/Events.IntegrationTest.csproj`, `test/Core.IntegrationTest/Core.IntegrationTest.csproj`, `util/SeederUtility/SeederUtility.csproj`, `util/SeederApi/SeederApi.csproj`, `util/RustSdk/RustSdk.csproj`, `util/Seeder/Seeder.csproj`, `bitwarden_license/test/SSO.Test/SSO.Test.csproj`, `bitwarden_license/test/Sso.IntegrationTest/Sso.IntegrationTest.csproj`
  - Direct 8.0 framework PackageReferences, about 27 refs across about 18 project files, including `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`, `src/Core/Core.csproj` (`System.Text.Json`), the four migration projects that pin `Microsoft.EntityFrameworkCore.Design`, and the two test projects that pin `Microsoft.Extensions.TimeProvider.Testing`
  - Non-Microsoft provider refs in `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Pomelo.EntityFrameworkCore.MySql`
  - Workflow files under `.github/workflows/` only where a `dotnet-version` or setup-dotnet argument is still `8.0.x` (expected in `test.yml` and any sibling that installs the SDK)
- change:
  - Set `global.json` sdk to `10.0.100` and keep `rollForward` at `latestFeature`. Install a 10.0 SDK on the machine so that pin resolves. Do not add a lock file and do not introduce `Directory.Packages.props`.
  - Set TargetFramework to `net10.0` in `Directory.Build.props` and in all ten overrides. `src` has no overrides and follows the props file.
  - Bump direct framework packages from their 8.0 versions to an exact 10.0 patch, preserving bracketed exact versions where the project already uses brackets. That set is Entity Framework Core Relational, SqlServer, Sqlite, and Design; the ASP.NET packages (DataProtection, JwtBearer, SignalR, Mvc.Testing); `Microsoft.Extensions.*`; `System.Text.Json`; and `Microsoft.Extensions.TimeProvider.Testing`. Pick the newest stable 10.0 patch for that package id. Do not switch package ids.
  - Move Npgsql's EF provider to the lowest stable version that depends on Entity Framework Core 10. Apply the same rule to Pomelo. If Pomelo publishes no such version, stop that project and report it; do not drop MySQL or multi-target it inside this slice.
  - Leave the test stack in `Directory.Build.props` (Test SDK 18.0.1, xUnit 2.6.6, runner 2.5.6, coverlet 6.0.0, NSubstitute 5.1.0, AutoFixture 4.18.1) unless Core.Test restore or the test host fails. Only then bump Test SDK and the xUnit runner, in this same slice.
  - Append `SYSLIB0057` to `WarningsNotAsErrors` from the late import so projects that already list CA1304 and CA1305 do not wipe the suppression. Do not edit `CoreHelpers`, `OrganizationSsoRequestModel`, `DynamicAuthenticationSchemeProvider`, or `ProgramTests`. Do not add a `#pragma`.
  - If the first Core or Api build reports another warnings-as-errors diagnostic, append that specific code the same way when it is a new SDK warning on unchanged behavior. If a diagnostic fires because a bumped API was removed, fix that call site. Stop and re-scope if those call-site edits would push the slice past 500 reviewable lines.
  - If a workflow installs .NET 8, change that version argument to `10.0.x` so CI can compile the pin. Leave the existing SQL-project removal in `test.yml` as it is.
  - Do not edit Dockerfiles, `.vscode/launch.json`, `dev/generate_openapi_files.ps1`, or `.config/dotnet-tools.json`. None of them are on the Core.Test, Api, or Entity Framework compile path.
- journeys:
  - A reviewer opens one diff and sees `net8.0` become `net10.0` in `Directory.Build.props`, the ten overrides, and `global.json` at `10.0.100`, with matching 10.0 package versions beside them. Docker tags, launch paths, and the OpenAPI script still show 8.0, which is intentional.
  - A developer installs the 10.0 SDK, restores, and builds this pin. `test/Core.Test/Core.Test.csproj`, `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`, and `src/Api/Api.csproj` compile.
  - A developer runs the Core test project and the suite finishes without a database, Docker, or Rust.
- verify:
  - `dotnet --list-sdks` shows a 10.0 SDK.
  - `dotnet restore test/Core.Test/Core.Test.csproj`
  - `dotnet build test/Core.Test/Core.Test.csproj --no-restore`
  - `dotnet test test/Core.Test/Core.Test.csproj --no-build`
  - `dotnet build src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`
  - `dotnet build src/Api/Api.csproj`
  - Do not treat `dotnet build bitwarden-server.sln` as the gate. That build includes `src/Sql/Sql.sqlproj` and can run `cargo build --release` through `util/RustSdk`.
- estimate: 110 reviewable lines. No lock file and no generated snapshots. Each rewritten TargetFramework or version attribute counts as two. The ten framework overrides are about 20, the framework and provider version edits about 60, plus `global.json`, the props file, a short targets append, and any workflow version arguments.

## Risks
- `Bitwarden.Server.Sdk` 1.5.1 fails to import under the net10 SDK. Early warning: the build dies in SDK resolution, before NuGet restore, naming `Bitwarden.Server.Sdk` or a missing props or targets file.
- Pomelo has no package that depends on Entity Framework Core 10, so the EF project cannot restore. Early warning: NU1202 on `Pomelo.EntityFrameworkCore.MySql` after the Microsoft.EntityFrameworkCore packages have moved to 10.
- A new warnings-as-errors code shows up beyond SYSLIB0057 and is either widespread or a real API break. Early warning: the first Core build lists a diagnostic other than SYSLIB0057, especially if the same code repeats across many files.
- xUnit 2.6.6 or Test SDK 18.0.1 fails on the net10 test host after a clean compile. Early warning: testhost or vstest failure, or NU1202 on `Microsoft.NET.Test.Sdk`, once `dotnet test` starts.
- Api's build log invokes Cargo or the SQL SDK, which this slice does not fix. Early warning: `cargo build --release` or `Microsoft.Build.Sql` in the Api log. Core.Test and the Entity Framework project remain the gate if that is the only Api failure.
- Entity Framework Core 10 or the Npgsql provider breaks compilation of query or mapping code that Core and Api still compile on 8.0.8. Early warning: CS errors in Infrastructure.EntityFramework or a Core.Test failure that appears only after the provider version changes.

## Least sure about
Bumping every Microsoft 8.0 framework package, plus Npgsql and Pomelo, in this commit rather than only the packages that fail restore. A canary restore of `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`, `src/Api/Api.csproj`, and `test/Core.Test/Core.Test.csproj` on net10.0 with the original package versions would change my mind: any package that restores cleanly stays on its 8.0 version, and only NU1202 or NU1605 failures get a major bump. If that canary fails inside Bitwarden.Server.Sdk before NuGet runs, the package list is the wrong risk and the SDK version has to move first.
