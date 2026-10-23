# Application Builder C# and Visual Basic parity tests

**Scope:** issue [#14978](https://github.com/dotnet/winforms/issues/14978). This suite verifies equivalent behavior through the shared `Microsoft.Extensions.WinForms` public API; it does not add a second VB lifecycle implementation.

## Paired scenarios

The VB scenarios are in `WinFormsApplicationBuilderParityTests` in the Visual Basic unit-test project. Each row identifies the C# test that exercises the same contract. Existing C# coverage is reused where its assertions already match the parity scenario.

| Contract | Visual Basic test | C# counterpart |
|---|---|---|
| Startup, started, stopping, and stopped event order | `Run_StartupFormClose_RaisesLifetimeInOrder` | `WinFormsApplicationBuilderTests.Run_StartupFormClose_RaisesLifetimeInOrder` |
| Startup cancellation and terminal cleanup | `Run_StartingCancellationSkipsStartupAndCleansUp` | `WinFormsApplicationBuilderTests.Run_StartingCancellationSkipsHostAndDisposesStartupForm` |
| Startup-handler exception propagation and cleanup order | `Run_StartingHandlerFailureCleansUpAndPropagates` | `WinFormsApplicationBuilderTests.Run_StartingHandlerFailureCleansUpAndPropagates` |
| Raw command-line snapshot | `CreateBuilder_SnapshotsCommandLineArguments` | `WinFormsApplicationBuilderTests.CreateBuilder_WithSettings_SnapshotsArguments` |
| Splash disposal before the startup form is shown | `Run_WithSplashScreen_CoordinatesStartupAndDisposesSplash` | `WinFormsApplicationServicesTests.Run_WithSplashScreen_CoordinatesHostlessStartupAndShutdown` |
| Application/runtime metadata and user-settings service resolution | `AddApplicationServices_AndUserSettings_AreAvailableToStartupForm` | `WinFormsApplicationServicesTests.AddApplicationServices_RegistersApplicationAndRuntimeInformation` and `WinFormsUserSettingsServiceTests.AddUserSettings_ProjectsSelectedValuesAndReloadsConfiguration` |
| Single-instance activation argument forwarding | `Run_SingleInstance_ForwardsActivationArguments` | `WinFormsApplicationBuilderTests.Run_SingleInstance_QueuesActivationDuringApplicationStarting` |

The scenarios use event ordering, service values, or forwarded arguments as assertions. Splash tests set the minimum duration to zero; single-instance tests use unique application IDs and a timeout only as a deadlock guard. They do not assert elapsed time or depend on a particular network state.

Network-change behavior is covered by the deterministic C# fake-monitor test `WinFormsApplicationServicesTests.NetworkAvailabilityChanged_MarshalsAndRaisesOnlyForAvailabilityChanges`. Its monitor seam is internal, so the VB suite verifies shared service registration through `AddApplicationServices()` instead of introducing a public test-only injection API. Multi-user, multi-session, terminal-services, and RDP behavior remain environment validation for #14980 rather than timing-sensitive CI tests.

## Running the focused tests

From the repository root:

```powershell
dotnet test src\Microsoft.VisualBasic.Forms\tests\UnitTests\Microsoft.VisualBasic.Forms.Tests.vbproj -- --filter-class Microsoft.VisualBasic.Forms.Tests.WinFormsApplicationBuilderParityTests
dotnet test src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj -- --filter-class System.Windows.Forms.Tests.WinFormsApplicationBuilderTests
dotnet test src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj -- --filter-class System.Windows.Forms.Tests.WinFormsApplicationServicesTests
dotnet test src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj -- --filter-class System.Windows.Forms.Tests.WinFormsUserSettingsServiceTests
```
