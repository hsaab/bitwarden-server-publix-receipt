# Plan: Move the Bitwarden server pin from net8.0 to net10.0

## Approach
Ship one slice that sets the SDK and the TargetFramework, suppresses SYSLIB0057, then restores three canary projects and bumps only the packages that fail. `Directory.Build.props` and the ten projects that repeat `net8.0` move to `net10.0`. `global.json` moves to SDK `10.0.100` with `rollForward` `latestFeature`. A new `Directory.Build.targets` appends `SYSLIB0057` to `WarningsNotAsErrors` so the six existing `X509Certificate2` constructors still compile. The 8.0 framework packages stay put until a canary restore of `test/Core.Test/Core.Test.csproj`, `src/Api/Api.csproj`, and `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj` reports NU1202 or NU1605. Those three edits cannot land apart: a net10.0 TFM against the current 8.0 packages fails restore if that hypothesis holds, a 10.0 package fails restore against the current net8.0 TFM, and warnings-as-errors fails the build on SYSLIB0057 once the TFM moves and the suppression is absent. The slice stays under the line budget, so it does not need a cohesion exception.

## Slices
### 1. Retarget the pin and bump only packages that fail restore
- scope:
  - `Directory.Build.props` (TargetFramework only, unless the Core.Test host later names a test package in this same file)
  - `global.json` (sdk version only, unless restore fails inside an msbuild-sdk)
  - `Directory.Build.targets` (new file)
  - TargetFramework overrides: `test/Setup.Test/Setup.Test.csproj`, `test/Infrastructure.Dapper.Test/Infrastructure.Dapper.Test.csproj`, `test/Events.IntegrationTest/Events.IntegrationTest.csproj`, `test/Core.IntegrationTest/Core.IntegrationTest.csproj`, `util/SeederUtility/SeederUtility.csproj`, `util/SeederApi/SeederApi.csproj`, `util/RustSdk/RustSdk.csproj`, `util/Seeder/Seeder.csproj`, `bitwarden_license/test/SSO.Test/SSO.Test.csproj`, `bitwarden_license/test/Sso.IntegrationTest/Sso.IntegrationTest.csproj`
  - Package files the canary is allowed to edit, only on PackageReference version attributes that restore names: `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`, `src/Core/Core.csproj`, the four migration csproj files that pin `Microsoft.EntityFrameworkCore.Design` at `[8.0.8]`, the test projects that pin `Microsoft.Extensions.TimeProvider.Testing`, and any other csproj that already references an id in the package list below
  - No `packages.lock.json`. Do not add one. No generated snapshots.
- change:
  - Set `TargetFramework` to `net10.0` in `Directory.Build.props` and in each of the ten overrides. Leave `TreatWarningsAsErrors`, ImplicitUsings, and Nullable as they are. Do not set `LangVersion`.
  - Set `global.json` `sdk.version` to `10.0.100` and keep `rollForward` at `latestFeature`. Leave `Microsoft.Build.Traversal` 4.1.0, `Microsoft.Build.Sql` 1.0.0, and `Bitwarden.Server.Sdk` 1.5.1 in place for the first restore.
  - Add `Directory.Build.targets` and append `SYSLIB0057` onto `WarningsNotAsErrors` from that late import. Projects that already assign `WarningsNotAsErrors` (CA1304 and CA1305) replace a value set in `Directory.Build.props`, so the append has to happen after the project file. There is no `Directory.Build.targets` today, and this new file is the smallest way to add the code without wiping those per-project lists. Do not add a `#pragma`. Do not edit `CoreHelpers`, `OrganizationSsoRequestModel`, `DynamicAuthenticationSchemeProvider`, or `ProgramTests`. The six `new X509Certificate2(...)` constructors stay.
  - Install a 10.0 SDK if `dotnet --list-sdks` has none, then restore these three projects with the original package versions: `test/Core.Test/Core.Test.csproj`, `src/Api/Api.csproj`, `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`.
  - If that restore fails inside `Bitwarden.Server.Sdk` before NuGet selects packages, bump that msbuild-sdk to the first release that supports net10.0, in this slice. If NuGet has no such release, stop. If it does not fail there, leave 1.5.1. Leave `Microsoft.Build.Traversal` and `Microsoft.Build.Sql` alone unless an error names that SDK.
  - Bump a package only when restore reports NU1202 or NU1605 for it. Allowed ids: `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.AspNetCore.DataProtection`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.SignalR`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.*` currently at 8.0.0, 8.0.1, or 8.0.10, `System.Text.Json`, `Microsoft.Extensions.TimeProvider.Testing`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Pomelo.EntityFrameworkCore.MySql`. A failing id outside this list may have its version attribute edited in the csproj restore names. Do not introduce `Directory.Packages.props`.
  - For a Microsoft package that fails, pin the newest stable 10.0 patch of that id. Use the existing `[exact]` bracket style where the project already uses brackets. Use one shared version for the EntityFrameworkCore family, matching today's shared `[8.0.8]`. One patch per family. No floating versions.
  - For Npgsql or Pomelo, when restore fails, move that provider to the lowest stable version whose Entity Framework Core dependency is 10.x, still in exact brackets. If either provider has no such stable package, stop and report that package. Do not drop the MySQL or PostgreSQL reference.
  - Leave the test stack in `Directory.Build.props` (`Microsoft.NET.Test.Sdk` 18.0.1, xunit 2.6.6, the runner 2.5.6, coverlet 6.0.0, NSubstitute 5.1.0, AutoFixture 4.18.1) unless Core.Test restore or the test host names one of them. Then bump only the package the error names, in this slice.
  - Repeat the three restores until they succeed. Then build the three projects. If the build reports another warnings-as-errors diagnostic on unchanged behavior, append that specific code in `Directory.Build.targets`. If a diagnostic fires because a bumped API was removed, fix that call site. Stop and re-scope if those call-site edits would push the slice past 500 reviewable lines. Stop if a failure changes runtime behavior (serializer settings, auth handler contracts, EF query semantics).
- journeys:
  - A reviewer opens the diff and sees the framework pin move from `net8.0` to `net10.0` in `Directory.Build.props` and the ten overrides, with `global.json` at SDK `10.0.100`.
  - A developer restores and builds `test/Core.Test/Core.Test.csproj`, `src/Api/Api.csproj`, and `src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj` and gets a successful compile.
  - A developer runs the Core test project and the suite finishes.
- verify:
  - `dotnet --list-sdks` includes a 10.0 SDK, and `dotnet --version` resolves from `10.0.100` under `rollForward` `latestFeature`.
  - `dotnet restore test/Core.Test/Core.Test.csproj`
  - `dotnet restore src/Api/Api.csproj`
  - `dotnet restore src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`
  - `dotnet build test/Core.Test/Core.Test.csproj`
  - `dotnet build src/Api/Api.csproj`
  - `dotnet build src/Infrastructure.EntityFramework/Infrastructure.EntityFramework.csproj`
  - `dotnet test test/Core.Test/Core.Test.csproj`
  - These commands are the done gate. `dotnet build bitwarden-server.sln` is not the gate. That solution builds `src/Sql/Sql.sqlproj` and can run `cargo build --release` through `util/RustSdk`. CI removes the SQL project before tests. Core.Test does not need SQL or Rust, and it is the high-signal proof.
- estimate: 120 reviewable lines

## Risks
- Certificate constructors versus suppression. Candidates A and B rewrite the six `new X509Certificate2(...)` sites to `X509CertificateLoader`. Candidate C suppresses SYSLIB0057. This plan suppresses. The old constructor sniffed certificate bytes versus PKCS#12, and the loader methods do not, so a rewrite can change certificate import. `Directory.Build.targets` appends SYSLIB0057 and leaves `CoreHelpers`, `OrganizationSsoRequestModel`, `DynamicAuthenticationSchemeProvider`, and `ProgramTests` unchanged. Early warning: the net10 reference assemblies remove those constructors, and the build reports a missing method rather than warning SYSLIB0057. In that case the suppression cannot compile and the slice stops for a re-scope.
- Package majors versus a canary. Candidate C bumps every Microsoft 8.0 framework package, plus Npgsql and Pomelo, before any restore. A and B do the same. NU1202 is a hypothesis, not a measured restore. This plan changes the TFM and SDK first, restores the three canary projects, and bumps only packages that fail. The file list still covers every package the candidates named, so a failure on that list stays inside the slice. Early warning: the first canary restore names a package id that is not in the allowed list, or it dies inside `Bitwarden.Server.Sdk` before NuGet runs.
- Pomelo or Npgsql has no stable package that depends on EF Core 10, so `Infrastructure.EntityFramework` cannot restore after the Microsoft EF packages move. Early warning: NU1202 or NU1102 on that provider once `Microsoft.EntityFrameworkCore` is on 10.
- `Bitwarden.Server.Sdk` 1.5.1 does not load under the 10.0 SDK. Early warning: restore fails on a host project before package resolution and names `Bitwarden.Server.Sdk`. Bump only that SDK, and only then.
- The net10 compiler emits warnings-as-errors other than SYSLIB0057. Early warning: the first build lists a different warning id, especially the same id across many files. Append a new SDK warning on unchanged behavior. Stop if the fix is an API rewrite that would pass 500 reviewable lines.
- EF Core 10 or ASP.NET Core 10 removes members this pin calls. Early warning: CS0117 or CS1501 in `Infrastructure.EntityFramework` or in auth and startup code after a package bump.
- xunit 2.6.6 or `Microsoft.NET.Test.Sdk` 18.0.1 refuses the net10 test host. Early warning: Core.Test compiles, then `dotnet test` fails in the runner before any test result. Bump only the package the runner names.
- A reviewer runs `dotnet build bitwarden-server.sln` and hits `Sql.sqlproj` or `cargo`. Early warning: the log names the SQL SDK or a missing `cargo` after Core.Test already passed. That build stays outside the gate. If the error names `Microsoft.Build.Sql` or `Microsoft.Build.Traversal`, bump only that msbuild-sdk inside this slice.

## Gaps
- The canary set is Core.Test, Api, and Infrastructure.EntityFramework. A package that only those three projects avoid can still fail in another project later.
- GitHub workflows call setup-dotnet with no `dotnet-version`. This plan does not add one. If the hosted default is still 8, CI can fail while the local 10.0 SDK passes.
- `dotnet-ef` stays on its current tool pin. If EF Core packages move to 10, local migration commands from that tool can disagree with the compiled provider.
- The obsolete certificate constructors remain. The upgrade compiles them. It does not modernize them.

## Out of scope
- Dockerfiles, `.vscode/launch.json`, `dev/generate_openapi_files.ps1`, and `.config/dotnet-tools.json`.
- `.github/workflows/`. No workflow passes a `dotnet-version`, so there is nothing to retarget.
- Rewriting `X509Certificate2` construction to `X509CertificateLoader`.
- `Directory.Packages.props` and `packages.lock.json`.
- `dotnet build bitwarden-server.sln`, `src/Sql/Sql.sqlproj`, and the RustSdk cargo PreBuild.
- Rebasing onto upstream to inherit a newer pin.

## Provenance
Slice 1 merges A and C. The TFM file list, the `global.json` shape, the rule to leave `Bitwarden.Server.Sdk` at 1.5.1 unless restore fails inside it, and the project-level done commands come from A. The SYSLIB0057 append in a new `Directory.Build.targets`, and the decision to restore canaries before bumping packages, come from C. From B, the slice keeps one shared exact bracket for the EntityFrameworkCore family and leaves the test stack alone unless restore or the runner names it.

Rejected: rewriting the six `new X509Certificate2` sites to `X509CertificateLoader`, which is the approach A and B both ship. The old constructor sniffed certificate content versus PKCS#12 and the loader methods do not, so the rewrite can change certificate import. A late `WarningsNotAsErrors` append keeps the current constructors and does not wipe the CA1304 and CA1305 lists already set on individual projects.

Also dropped: C's edits under `.github/workflows/`, because setup-dotnet is called with no `dotnet-version`. Also dropped: B's `dotnet restore bitwarden-server.sln` as a required gate, because that solution includes `Sql.sqlproj` and RustSdk, which are outside the compile proof.
