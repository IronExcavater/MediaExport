# Toolchain Ownership and Test Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give MediaExport a managed-first toolchain with automatic external fallback, clear Settings actions, normal project references, behavior-focused unit tests, and no known warnings in the touched code.

**Architecture:** Add a WinUI-independent `MediaExport.Application` project for view models and application abstractions. Keep discovery, ownership, process inspection, and atomic managed-tool operations in `MediaExport.Core`; keep platform launchers and XAML in `MediaExport`. Tests reference Core and Application as assemblies and exercise public behavior with temporary directories and narrow fakes.

**Tech Stack:** C# 14, .NET 10, WinUI 3, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection, NUnit 4.

**Spec:** `docs/superpowers/specs/2026-10-14-toolchain-testing-design.md`

## Global Constraints

- Prefer a valid MediaExport-managed executable; automatically fall back to the first valid external executable in the configured search path.
- MediaExport must never update or remove an external executable.
- Auto update and manual update apply only to tools installed by MediaExport.
- Preserve the current uncommitted Settings, Activity, and Export UI work while moving files or refining behavior.
- Keep only Appearance and Tools on Settings; do not restore About, history settings, overflow menus, or Update all.
- Tests must reference production projects normally; do not link production `.cs` files into `MediaExport.Tests`.
- Use real temporary directories for file behavior, an injected `HttpClient` for downloads, and a narrow process runner for executable inspection.
- Fix warnings at their source; do not add warning suppressions.
- Do not introduce a generic repository, file-system abstraction, mocking framework, or minimum-version policy.

## Review Focus

- A saved managed path whose file was deleted must fall back to an external copy and report it as external; pin this in Task 2.
- A sibling directory whose name merely starts with the managed directory name must never gain managed privileges; pin this in Task 2.
- Cancellation or a partial HTTP response must leave the previous managed executable byte-for-byte intact and remove temporary data; pin this in Task 4.
- An executable that starts but exits non-zero or prints no parseable version must become a row error, not a healthy installation; pin this in Tasks 3 and 5.
- Repeated clicks or a refresh during install/update/remove must not overlap operations for one tool; pin this in Task 5.

---

## File Structure

### New application project

- `MediaExport.Application/MediaExport.Application.csproj` — .NET 10 class library referencing Core and CommunityToolkit.Mvvm.
- `MediaExport.Application/Activity/ActivityViewModel.cs` — export queue orchestration.
- `MediaExport.Application/Activity/ExportJob.cs` — one export's state and lifecycle.
- `MediaExport.Application/Export/ExportViewModel.cs` — selected media and recent-media orchestration.
- `MediaExport.Application/Search/SearchViewModel.cs` — search workflow.
- `MediaExport.Application/Search/SearchTermText.cs` — search-term display text.
- `MediaExport.Application/Formatting/Format.cs` — platform-independent formatting helpers, retaining the `MediaExport.Converters` namespace for XAML compatibility.
- `MediaExport.Application/Settings/IThemeService.cs` — theme application boundary.
- `MediaExport.Application/Settings/IFolderLauncher.cs` — managed-folder launch boundary.
- `MediaExport.Application/Settings/ThemeOption.cs` — theme choice model.
- `MediaExport.Application/Settings/ToolInfo.cs` — immutable tool metadata.
- `MediaExport.Application/Settings/ToolViewModel.cs` — one tool row's state and commands.
- `MediaExport.Application/Settings/SettingsViewModel.cs` — Appearance and Tools orchestration.

### Core toolchain

- `MediaExport.Core/Tooling/ToolInstallation.cs` — explicit path and `Managed`/`External` origin.
- `MediaExport.Core/Tooling/ToolchainOptions.cs` — managed folder and external search directories.
- `MediaExport.Core/Tooling/IProcessRunner.cs` — process inspection boundary and result value.
- `MediaExport.Core/Tooling/SystemProcessRunner.cs` — production process implementation.
- `MediaExport.Core/Tooling/ToolInspectionException.cs` — predictable inspection failure.
- `MediaExport.Core/Tooling/ToolchainService.cs` — discovery and protected, atomic managed operations.

### WinUI adapters and views

- `MediaExport/Settings/ThemeService.cs` — WinUI implementation only.
- `MediaExport/Services/WindowsFolderLauncher.cs` — opens a folder through Explorer.
- `MediaExport/Services/ServiceCollectionExtensions.cs` — constructs toolchain options, HTTP/process dependencies, and application services.
- `MediaExport/Settings/SettingsPage.xaml` — ownership copy and state-specific, consistently sized actions.
- Existing page/control files remain UI-only and keep their existing namespaces.

### Tests

- `MediaExport.Tests/Support/TemporaryDirectory.cs` — deterministic disposable test directory.
- `MediaExport.Tests/Support/StubHttpMessageHandler.cs` — scripted response/failure/download body.
- `MediaExport.Tests/Support/FakeProcessRunner.cs` — records requests and returns or throws scripted results.
- `MediaExport.Tests/Tooling/ToolchainDiscoveryTests.cs` — precedence, fallback, and path-boundary behavior.
- `MediaExport.Tests/Tooling/ToolchainInspectionTests.cs` — version inspection outcomes.
- `MediaExport.Tests/Tooling/ToolchainManagementTests.cs` — install/update/remove and atomicity.
- `MediaExport.Tests/Settings/ToolViewModelTests.cs` — public state and command behavior without reflection.
- Existing Search, Activity, Export, formatting, provider, storage, and Settings tests stay focused on behavior and move only as namespaces/references require.

---

### Task 1: Establish the Application assembly boundary

**Files:**
- Create: `MediaExport.Application/MediaExport.Application.csproj`
- Create: `MediaExport.Application/Settings/IThemeService.cs`
- Create: `MediaExport.Application/Settings/IFolderLauncher.cs`
- Move: the WinUI-independent files listed under “New application project” above
- Modify: `MediaExport.slnx`
- Modify: `MediaExport/MediaExport.csproj`
- Modify: `MediaExport.Tests/MediaExport.Tests.csproj`
- Modify: `MediaExport/Settings/ThemeService.cs`
- Modify: `MediaExport/Services/ServiceCollectionExtensions.cs`
- Test: all existing `MediaExport.Tests` files

**Interfaces:**
- Produces: `IThemeService.CurrentTheme`, `IThemeService.Apply(AppTheme theme)`, and `IFolderLauncher.Open(string folder)` in `MediaExport.Settings`.
- Produces: a test project with only `ProjectReference` entries for `MediaExport.Core` and `MediaExport.Application`.

- [ ] **Step 1: Record the pre-move test baseline**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --no-restore`

Expected: the current suite passes; record the test count so the move cannot silently drop tests.

- [ ] **Step 2: Create the Application project and move production files**

Create a `net10.0` nullable class library with implicit usings, a Core project reference, and `CommunityToolkit.Mvvm` 8.4.2. Move files rather than linking or copying them, retain existing `MediaExport.*` namespaces, split `IThemeService` from `ThemeService`, and replace the Settings view model's direct Explorer call with `IFolderLauncher.Open(string folder)`.

- [ ] **Step 3: Replace linked compilation with project references**

Add Application to `MediaExport.slnx`; reference it from WinUI and Tests; remove every `<Compile Include="../MediaExport/...">` item from the test project. Register the platform `ThemeService` and folder launcher in WinUI DI.

- [ ] **Step 4: Verify the assembly boundary**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj`

Expected: the baseline test count passes, and `rg -n "Compile Include=.*MediaExport" MediaExport.Tests/MediaExport.Tests.csproj` returns no matches.

- [ ] **Step 5: Commit**

```powershell
git add MediaExport.Application MediaExport.slnx MediaExport/MediaExport.csproj MediaExport.Tests/MediaExport.Tests.csproj MediaExport/Settings/ThemeService.cs MediaExport/Services/ServiceCollectionExtensions.cs MediaExport/Activity/ActivityViewModel.cs MediaExport/Activity/ExportJob.cs MediaExport/Export/ExportViewModel.cs MediaExport/Search/SearchViewModel.cs MediaExport/Search/SearchTermText.cs MediaExport/Converters/Format.cs MediaExport/Settings/SettingsViewModel.cs MediaExport/Settings/ToolViewModel.cs MediaExport/Settings/ThemeOption.cs
git commit -m "Refactor application logic into testable assembly"
```

### Task 2: Make discovery return explicit ownership

**Files:**
- Create: `MediaExport.Core/Tooling/ToolInstallation.cs`
- Create: `MediaExport.Core/Tooling/ToolchainOptions.cs`
- Modify: `MediaExport.Core/Tooling/ToolchainService.cs`
- Modify: `MediaExport.Core/Media/Ytdlp/YtdlpService.cs`
- Modify: `MediaExport/Services/ServiceCollectionExtensions.cs`
- Create: `MediaExport.Tests/Support/TemporaryDirectory.cs`
- Create: `MediaExport.Tests/Tooling/ToolchainDiscoveryTests.cs`
- Modify: `MediaExport.Tests/Tooling/ToolchainServiceTests.cs`

**Interfaces:**
- Produces: `public enum ToolOrigin { Managed, External }`.
- Produces: `public sealed record ToolInstallation(string Path, ToolOrigin Origin)`.
- Produces: `public sealed record ToolchainOptions(string ManagedFolder, IReadOnlyList<string> SearchPath)`.
- Produces: `ToolchainService.Folder`, `ToolchainService.Find(string id) -> ToolInstallation?`, and `ToolchainService.IsInstalled(string id) -> bool`.
- Consumes: `SettingsStore.Current.Tools[id].InstallPath` as the persisted managed candidate; an absent tool definition uses `id` as its executable name.

- [ ] **Step 1: Write discovery tests that fail against the string-only API**

Add tests named `Find_prefers_the_saved_managed_executable`, `Find_uses_the_first_external_executable_when_managed_is_missing`, `Find_falls_back_when_the_saved_managed_file_was_deleted`, `Find_returns_null_when_no_candidate_exists`, and `A_prefixed_sibling_directory_is_external`. Assert both normalized full `Path` and exact `Origin`.

- [ ] **Step 2: Run the discovery tests and confirm the ownership API is missing**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter FullyQualifiedName~ToolchainDiscoveryTests`

Expected: FAIL because `ToolInstallation`, `ToolOrigin`, `ToolchainOptions`, and the new `Find` result do not exist.

- [ ] **Step 3: Implement managed-first resolution**

Give `ToolchainService` a constructor `ToolchainService(SettingsStore settings, ToolchainOptions options)`. Normalize candidates with `Path.GetFullPath`. Classify a path as managed only when `Path.GetRelativePath(options.ManagedFolder, path)` is neither rooted nor starts with `..` as a complete segment. Search only injected directories after the saved managed candidate.

In WinUI DI, construct `ToolchainOptions` from `<dataFolder>/Tools` and the process `PATH`, splitting with `Path.PathSeparator`, removing blank entries, and trimming whitespace and enclosing quotes.

- [ ] **Step 4: Update the yt-dlp consumer**

Change `YtdlpService` to execute `tools.Find("yt-dlp")?.Path`; retain its current missing-tool message and search/download behavior.

- [ ] **Step 5: Verify discovery behavior**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter "FullyQualifiedName~ToolchainDiscoveryTests|FullyQualifiedName~YtdlpServiceTests"`

Expected: PASS, including stale-path fallback and the managed-directory boundary case.

- [ ] **Step 6: Commit**

```powershell
git add MediaExport.Core/Tooling MediaExport.Core/Media/Ytdlp/YtdlpService.cs MediaExport/Services/ServiceCollectionExtensions.cs MediaExport.Tests/Support/TemporaryDirectory.cs MediaExport.Tests/Tooling
git commit -m "Add managed-first tool discovery"
```

### Task 3: Isolate and validate version inspection

**Files:**
- Create: `MediaExport.Core/Tooling/IProcessRunner.cs`
- Create: `MediaExport.Core/Tooling/SystemProcessRunner.cs`
- Create: `MediaExport.Core/Tooling/ToolInspectionException.cs`
- Modify: `MediaExport.Core/Tooling/ToolchainService.cs`
- Modify: `MediaExport/Services/ServiceCollectionExtensions.cs`
- Create: `MediaExport.Tests/Support/FakeProcessRunner.cs`
- Create: `MediaExport.Tests/Tooling/ToolchainInspectionTests.cs`
- Remove: superseded parse-only assertions from `MediaExport.Tests/Tooling/ToolchainServiceTests.cs`

**Interfaces:**
- Produces: `public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)`.
- Produces: `IProcessRunner.RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default) -> Task<ProcessResult>`.
- Produces: `ToolchainService.ReadVersionAsync(ToolInstallation installation, string argument, CancellationToken cancellationToken = default) -> Task<string>`.
- Produces: `ToolchainService(SettingsStore settings, ToolchainOptions options, IProcessRunner processRunner)` as the constructor used after this task.
- Produces: `ToolInspectionException` for start failure, non-zero exit, or output without a parseable version; cancellation remains `OperationCanceledException`.

- [ ] **Step 1: Write inspection behavior tests**

Test exact cases: stdout `ffmpeg version 7.1-full_build` returns `7.1`; stdout `2026.09.01` returns `2026.09.01`; a start exception is wrapped with the tool path; exit code `2` includes the last nonblank stderr line; blank/unparseable output throws; cancellation remains cancellation. Assert the fake receives the installation path and version argument once.

- [ ] **Step 2: Run the tests and verify failure**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter FullyQualifiedName~ToolchainInspectionTests`

Expected: FAIL because the runner contract and exception behavior are absent.

- [ ] **Step 3: Implement the process boundary and inspection policy**

`SystemProcessRunner` owns `ProcessStartInfo`, redirected output/error, waiting, cancellation, and disposal. `ToolchainService` owns version parsing and converts non-cancellation execution failures into concise `ToolInspectionException` messages.

Register `SystemProcessRunner` as the singleton `IProcessRunner` used by `ToolchainService`.

- [ ] **Step 4: Verify inspection tests**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter FullyQualifiedName~ToolchainInspectionTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add MediaExport.Core/Tooling MediaExport/Services/ServiceCollectionExtensions.cs MediaExport.Tests/Support/FakeProcessRunner.cs MediaExport.Tests/Tooling
git commit -m "Make tool version inspection testable"
```

### Task 4: Protect managed install, update, and removal

**Files:**
- Modify: `MediaExport.Core/Tooling/ToolchainService.cs`
- Modify: `MediaExport/Services/ServiceCollectionExtensions.cs`
- Create: `MediaExport.Tests/Support/StubHttpMessageHandler.cs`
- Create: `MediaExport.Tests/Tooling/ToolchainManagementTests.cs`

**Interfaces:**
- Produces: `ToolchainService.InstallAsync(string id, Uri downloadUri, CancellationToken cancellationToken = default) -> Task<ToolInstallation>`.
- Produces: `ToolchainService.UpdateAsync(string id, Uri downloadUri, CancellationToken cancellationToken = default) -> Task<ToolInstallation>`.
- Produces: `ToolchainService.RemoveAsync(string id, CancellationToken cancellationToken = default) -> Task`.
- Produces: `ToolchainService(SettingsStore settings, ToolchainOptions options, HttpClient httpClient, IProcessRunner processRunner)` as the final constructor.
- Contract: Update and Remove throw `InvalidOperationException` unless `Find(id)` resolves a contained managed installation; Install may create a managed copy while an external copy is active.

- [ ] **Step 1: Write lifecycle tests**

Add tests named `Install_writes_and_persists_a_managed_executable`, `Install_replaces_an_external_active_copy_with_a_managed_copy`, `Update_atomically_replaces_a_managed_executable`, `Failed_update_preserves_the_existing_executable_and_removes_the_temporary_file`, `Cancelled_update_preserves_the_existing_executable_and_removes_the_temporary_file`, `Update_rejects_an_external_copy`, `Remove_rejects_an_external_copy`, and `Remove_clears_managed_state_then_reveals_external_fallback`.

- [ ] **Step 2: Run lifecycle tests and confirm they fail**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter FullyQualifiedName~ToolchainManagementTests`

Expected: FAIL because the protected lifecycle API and atomic replacement are not implemented.

- [ ] **Step 3: Implement atomic managed writes**

Download with the injected `HttpClient` into a uniquely named temporary file inside `<ManagedFolder>/<id>`. Only after a complete response/body write, replace the target with `File.Move(temp, target, overwrite: true)`. In `finally`, delete an existing temporary file. Persist `InstallPath`, `IsInstalled`, `Version = "latest"`, and `LastCheckedUtc` only after the move succeeds.

Register a dedicated singleton download `HttpClient` when constructing `ToolchainService`; do not reuse the SoundCloud client with its browser user agent.

- [ ] **Step 4: Implement protected update and removal**

Update delegates to the atomic writer only after resolving a managed copy. Remove validates ownership and containment again, deletes only that file, clears `InstallPath`, `IsInstalled`, `Version`, and `LastCheckedUtc` on the existing `ToolSettings`, saves, and leaves the tool definition/auto-update preference intact.

- [ ] **Step 5: Verify lifecycle and discovery together**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter FullyQualifiedName~Toolchain`

Expected: PASS; cancellation is reported as cancellation, no `*.tmp` file remains, and external fallback becomes active after removal.

- [ ] **Step 6: Commit**

```powershell
git add MediaExport.Core/Tooling/ToolchainService.cs MediaExport/Services/ServiceCollectionExtensions.cs MediaExport.Tests/Support/StubHttpMessageHandler.cs MediaExport.Tests/Tooling/ToolchainManagementTests.cs
git commit -m "Protect managed tool lifecycle"
```

### Task 5: Drive Settings tool rows from explicit state

**Files:**
- Modify: `MediaExport.Application/Settings/ToolInfo.cs`
- Modify: `MediaExport.Application/Settings/ToolViewModel.cs`
- Modify: `MediaExport.Application/Settings/SettingsViewModel.cs`
- Modify: `MediaExport/Settings/SettingsPage.xaml`
- Modify: `MediaExport/Settings/SettingsPage.xaml.cs`
- Modify: `MediaExport/Services/ServiceCollectionExtensions.cs`
- Rewrite: `MediaExport.Tests/Settings/ToolViewModelTests.cs`
- Create: `MediaExport.Tests/Settings/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4 toolchain APIs.
- Produces: `public sealed record ToolInfo(string Id, string Name, string Executable, string VersionArgument, Uri Website, Uri SourceCode, Uri? DownloadUri)`.
- Produces: `ToolViewModel(ToolInfo tool, ToolchainService toolchain)` and `SettingsViewModel(ToolchainService toolchain, SettingsStore settings, IThemeService themeService, IFolderLauncher folderLauncher)`.
- Produces: `ToolViewModel.Installation`, `IsChecking`, `IsBusy`, `HasError`, `IsMissing`, `IsExternal`, `IsManaged`, `CanInstall`, `CanUpdate`, `CanRemove`, `InstallActionText`, `VersionText`, `SourceText`, and generated `RefreshCommand`, `InstallCommand`, `UpdateCommand`, `RemoveCommand`.
- Contract: `CanInstall` is true for a downloadable missing or external tool; its label is `Install` when missing and `Install managed copy` when external. Update and Remove are available only when managed.
- Produces: `SettingsViewModel.RefreshToolsAsync()` and `SettingsViewModel.OpenToolsFolderCommand`; auto-update attempts each eligible managed tool at most once per `SettingsViewModel` lifetime and never touches external tools.

- [ ] **Step 1: Replace reflection tests with state-matrix tests**

Using a real `ToolchainService` backed by a temporary directory, fake process runner, and stub HTTP handler, assert the full public state/action matrix for missing, external, and managed installations. Assert exact ownership copy: external rows say updates/removal happen where the tool was installed; managed rows say `Installed by MediaExport`.

- [ ] **Step 2: Add command and failure tests**

Test that Refresh moves from checking to the discovered version; Install on external creates and selects a managed copy; Update and Remove call the lifecycle once then refresh; inspection/download/remove failures appear in `Error`; cancellation produces no error; and a held operation makes all four commands non-executable until it completes. In `SettingsViewModelTests`, prove that automatic update runs once for an eligible managed tool and never for an external tool.

- [ ] **Step 3: Run Settings tests and confirm failure**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter "FullyQualifiedName~ToolViewModelTests|FullyQualifiedName~SettingsViewModelTests"`

Expected: FAIL because ToolViewModel still infers ownership from a string and lacks the required operation state.

- [ ] **Step 4: Implement the row state machine**

Store `ToolInstallation?` rather than a path. Route every command through one private operation guard that sets `IsBusy`, clears the prior error, catches expected tool/HTTP/I/O/access failures into concise row text, treats cancellation as a non-error, refreshes after mutations, and notifies every dependent property/command exactly once.

- [ ] **Step 5: Implement Settings orchestration and platform registration**

Construct the three `ToolInfo` values once in Settings. Remove obsolete `YtDlpInstalled`, `FfmpegInstalled`, `InstallYtDlpCommand`, and `InstallFfmpegCommand`. Register `IFolderLauncher`, and ensure auto-update filters on `Origin == Managed`, a non-null download URI, and the persisted per-tool setting.

- [ ] **Step 6: Refine the XAML action layout**

Keep Refresh/Open folder inside the expanded-tools header. Use one 32px-high text-button style with a common minimum width for Install/Install managed copy/Update, and one 32×32 icon-button style for Website/Source/Remove. Bind visibility/enabled state directly to the public state matrix; do not add overflow menus or Update all. Keep the stable scrollbar gutter and vertically aligned theme item icon/text.

- [ ] **Step 7: Verify Settings behavior**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj --filter "FullyQualifiedName~Settings"`

Expected: PASS with no reflection or private-member access in Settings tests.

- [ ] **Step 8: Commit**

```powershell
git add MediaExport.Application/Settings MediaExport/Settings/SettingsPage.xaml MediaExport/Settings/SettingsPage.xaml.cs MediaExport/Services/ServiceCollectionExtensions.cs MediaExport.Tests/Settings
git commit -m "Clarify managed and external tool actions"
```

### Task 6: Consolidate behavior-focused application tests

**Files:**
- Modify: `MediaExport.Tests/Activity/ActivityViewModelTests.cs`
- Modify: `MediaExport.Tests/Export/ExportViewModelTests.cs`
- Modify: `MediaExport.Tests/Search/SearchViewModelTests.cs`
- Modify: `MediaExport.Tests/Converters/FormatTests.cs`
- Modify or remove: any test that only checks UI wiring, generated command existence, or trivial property assignment

**Interfaces:**
- Consumes: Application public APIs established by Task 1.
- Produces: unit coverage organized around happy path, basic/boundary input, failure, and cancellation/replacement behavior.

- [ ] **Step 1: Audit the suite by behavior category**

For each Application test fixture, retain tests that prove a workflow outcome. Delete duplicate structural assertions. Rename vague prose tests only where the scenario/outcome is unclear; do not churn already clear tests.

- [ ] **Step 2: Make Export tests direct and behavioral**

Replace reflection in `CloseClearsTheSelectedMedia` with a direct `Close()` call. Add assertions that Close cancels an in-flight link lookup and ignores its late result, a provider failure returns the user-facing error and clears loading, and a successful Open/Close preserves Recently viewed.

- [ ] **Step 3: Fill only meaningful Activity/Search gaps**

Add an Activity cancellation test that proves a running downloader receives cancellation and history records the cancelled outcome. Preserve the existing Search happy path, empty input, provider failure, and newer-search replacement tests; add no UI-event or XAML tests.

- [ ] **Step 4: Run the complete unit suite**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj`

Expected: PASS; `rg -n "GetMethod\(|GetProperty\(|Reflection|XamlReader|FindName" MediaExport.Tests` returns no matches.

- [ ] **Step 5: Commit**

```powershell
git add MediaExport.Tests
git commit -m "Focus tests on application behavior"
```

### Task 7: Resolve source warnings and verify the product

**Files:**
- Modify: `MediaExport/Media/Controls/CardMediaView.xaml`
- Modify: `MediaExport/Media/Controls/CardMediaView.xaml.cs` if state names are referenced there
- Modify: `MediaExport/Media/Controls/RowMediaView.xaml`
- Modify: `MediaExport/Media/Controls/RowMediaView.xaml.cs` if state names are referenced there
- Modify: `MediaExport/Media/Controls/PlayerMediaView.xaml`
- Modify: `MediaExport/Media/Controls/PlayerMediaView.xaml.cs` if state names are referenced there
- Modify: `MediaExport/Search/Controls/SearchInput.xaml.cs`
- Modify: `MediaExport/Search/Controls/SearchTermBox.xaml.cs`
- Modify: XAML files named by the current `RowIconButtonStyle` and missing `Grid.Row` warnings

**Interfaces:**
- Produces: unchanged runtime behavior with zero currently reported C# and XAML warnings.

- [ ] **Step 1: Capture the exact warning baseline**

Run: `dotnet build MediaExport/MediaExport.csproj -p:Platform=x64 --no-restore`

Expected: success with the known `Loaded` generated-member, contextual `field`, reference-comparison, duplicate styled value, and missing `Grid.Row` warnings recorded by file/line.

- [ ] **Step 2: Fix each warning at its cause**

Rename each `Loaded` visual state to `ContentLoaded` and update state transitions. Replace contextual `field` accessors in SearchInput with named backing fields. Replace the `sender == _pickedBox` comparison with `ReferenceEquals(sender, _pickedBox)` after the appropriate type check. Remove local values already supplied by `RowIconButtonStyle`. Add explicit `Grid.Row="0"` to non-first children whose row is changed by adaptive states.

- [ ] **Step 3: Verify tests and x64 WinUI build**

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj`

Expected: PASS.

Run: `dotnet build MediaExport/MediaExport.csproj -p:Platform=x64 --no-restore`

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 4: Cross-build supported architectures**

Run: `dotnet build MediaExport/MediaExport.csproj -p:Platform=x86 --no-restore`

Run: `dotnet build MediaExport/MediaExport.csproj -p:Platform=ARM64 --no-restore`

Expected: each architecture the installed host toolchain supports succeeds with 0 warnings; document an unavailable host workload separately rather than suppressing it.

- [ ] **Step 5: Inspect the final diff for ownership and test-boundary regressions**

Run: `rg -n "Compile Include=.*MediaExport|Update all|MenuFlyout|IsManaged\(" MediaExport.Tests MediaExport MediaExport.Core -g '*.csproj' -g '*.cs' -g '*.xaml'`

Expected: no linked production sources, Update all, tool overflow menu, or caller-side ownership inference remains.

- [ ] **Step 6: Commit**

```powershell
git add MediaExport/Media/Controls MediaExport/Search/Controls MediaExport/Activity/ActivityPage.xaml MediaExport/Export/ExportPage.xaml MediaExport/Settings/SettingsPage.xaml
git commit -m "Resolve application and XAML warnings"
```

### Task 8: Final regression review

**Files:**
- Modify: only files required by failures found in this task

**Interfaces:**
- Consumes: all prior tasks.
- Produces: one verified branch in which managed tools take precedence, external fallback is automatic, Settings exposes only legal actions, and tests compile production code once.

- [ ] **Step 1: Run the complete verification sequence from a clean build output**

Run: `dotnet clean MediaExport.slnx -p:Platform=x64`

Run: `dotnet test MediaExport.Tests/MediaExport.Tests.csproj`

Run: `dotnet build MediaExport/MediaExport.csproj -p:Platform=x64 --no-restore`

Expected: all tests pass and the WinUI build reports 0 warnings and 0 errors.

- [ ] **Step 2: Review the working tree and commits**

Run: `git status --short`

Run: `git log --oneline --decorate -10`

Expected: only intentional user-owned changes remain uncommitted, and each implementation commit corresponds to one task above.

- [ ] **Step 3: Commit any regression-only correction**

If verification required a correction, stage only its files and commit with a message naming the corrected behavior. If no correction was needed, do not create an empty commit.
