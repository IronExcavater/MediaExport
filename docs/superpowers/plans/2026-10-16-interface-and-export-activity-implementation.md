# MediaExport Interface and Export Activity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved Search, shared media-view, Settings, development-workflow, yt-dlp progress/DRM, Activity, and Export improvements without adding new providers or export capabilities.

**Architecture:** Keep business state in `MediaExport.Application` and downloader parsing in `MediaExport.Core`; WinUI controls expose explicit density and activation contracts while pages own commands, selection, and context menus. Progress and DRM cross layers through typed records/exceptions, and Activity retries mutate the existing job object through the scheduler.

**Tech Stack:** .NET 10, C# 14, WinUI 3 / Windows App SDK 2.5, CommunityToolkit.Mvvm, NUnit 4, PowerShell development launcher.

**Spec:** `docs/superpowers/specs/2026-10-16-interface-and-export-activity-design.md`

## Global Constraints

- Do not add Vimeo, Internet Archive, Bilibili, or any other provider in this pass.
- Do not add export formats, quality choices, clipping, or other export functionality.
- External tools remain valid fallbacks; Update and Remove remain managed-installation-only actions.
- Keep selectable media text and keyboard-accessible media activation.
- Use labeled primary actions and context menus for secondary item actions.
- Preserve the existing x64 build and NUnit project with default SDK file inclusion.
- Commit and push each task as its own coherent batch.

## Review Focus

- yt-dlp writes marked progress to either redirected stream: Task 3 tests both readers and ignores unrelated lines.
- A download can expose DRM only after inspection succeeded: Task 3 tests typed DRM errors; Task 4 tests a non-retryable Activity row.
- Retrying after cancellation needs a fresh token: Task 4 retries the same instance after its first attempt was cancelled/failed.
- A media title click must not activate after a drag selection: Task 1 verifies this through native app inspection at the end of the task.
- The development launcher must not silently start registered stale output: Task 2 verifies the live process path and output timestamp.

---

### Task 1: Shared media views and Search presentation

**Files:**
- Create: `MediaExport/Media/Controls/MediaPresentation.cs`
- Create: `MediaExport/Media/Controls/MediaButton.cs`
- Modify: `MediaExport/Media/Controls/MediaView.cs`
- Modify: `MediaExport/Media/Controls/RowMediaView.xaml`
- Modify: `MediaExport/Media/Controls/RowMediaView.xaml.cs`
- Modify: `MediaExport/Media/Controls/CardMediaView.xaml`
- Modify: `MediaExport/Search/Controls/SearchInput.xaml`
- Modify: `MediaExport/Search/Controls/ProviderPicker.xaml`
- Modify: `MediaExport/Search/SearchPage.xaml`
- Modify: `MediaExport/Search/SearchPage.xaml.cs`
- Modify: `MediaExport/Controls/JustifiedLayout.cs`
- Modify: `MediaExport.Application/Search/SearchViewModel.cs`
- Test: `MediaExport.Tests/Search/SearchViewModelTests.cs`

**Interfaces:**
- Produces: `MediaPresentation.Comfortable|Compact`; `RowMediaView.Presentation`; `MediaButton` with selection-aware `Click`; `SearchViewModel.Title`, `SearchViewModel.TermSummary`, and `SearchViewModel.HasPages`.
- Consumes: existing `MediaSearchResult`, `SearchTerm`, provider, pagination, and `CardButtonStyle` behavior.

- [ ] **Step 1: Add failing Search heading tests**

Add NUnit cases proving that a successful `cats` search has title `cats`, its provider/terms live in `TermSummary`, an empty completed search has title `No results for “cats”`, and `HasPages` is false for no results. The break caught is rebuilding the old generated sentence or putting the empty message back under pagination.

- [ ] **Step 2: Run the focused Search tests and verify RED**

Run: `dotnet test .\MediaExport.Tests\MediaExport.Tests.csproj -p:Platform=x64 --no-restore --filter FullyQualifiedName~SearchViewModelTests`

Expected: FAIL because `TermSummary`/`HasPages` do not exist and `Title` still includes generated qualifiers.

- [ ] **Step 3: Implement the heading contract and Search layout**

Make `Title` use the shown query and empty-result state; add `TermSummary` and `HasPages` notifications. Bind the Search title and metadata separately, keep pagination free of result messages, hide it when `HasPages` is false, apply theme-aware provider-item chrome, and update `JustifiedLayout` so its final row fills the available width under the same bounds as other rows.

- [ ] **Step 4: Implement externally configurable media presentation**

Add `MediaPresentation`, bind comfortable/compact thumbnail and typography values through public dependency properties, and set only `resultsPopup` rows/skeletons to Compact. Add `MediaButton` as the reusable activation wrapper: keyboard/ordinary clicks invoke once, while a pointer drag that leaves selected text does not invoke. Search results consume the wrapper without reaching into named media-view elements.

- [ ] **Step 5: Verify Task 1**

Run the focused Search tests, then:

`dotnet build .\MediaExport\MediaExport.csproj -p:Platform=x64 --no-restore`

Expected: focused tests pass; build succeeds with 0 warnings and 0 errors. Inspect Search at wide and narrow widths, including last-row fill, provider flyout, selectable-title click/drag behavior, comfortable results and compact popup rows.

- [ ] **Step 6: Commit and push batch 1**

Commit message: `refactor: clarify search and media presentation`

Push `dev` to `origin` after `git diff --check` and Task 1 verification pass.

---

### Task 2: Settings metadata, tool presentation, and current-build launcher

**Files:**
- Create: `MediaExport.Application/Settings/AppMetadata.cs`
- Create: `MediaExport/Settings/WindowsAppMetadataProvider.cs`
- Create: `eng/Run-Dev.ps1`
- Modify: `MediaExport.Application/Settings/SettingsViewModel.cs`
- Modify: `MediaExport.Application/Settings/ToolViewModel.cs`
- Modify: `MediaExport/Settings/SettingsPage.xaml`
- Modify: `MediaExport/Services/ServiceCollectionExtensions.cs`
- Modify: `MediaExport/MediaExport.csproj`
- Modify: `README.md` if present, otherwise create `docs/development.md`
- Test: `MediaExport.Tests/Settings/SettingsViewModelTests.cs`
- Test: `MediaExport.Tests/Settings/ToolViewModelTests.cs`

**Interfaces:**
- Produces: `AppMetadata` record, `IAppMetadataProvider.Get()`, `SettingsViewModel.App`, concise external `ToolViewModel.StatusText`/`SourceText`, and `eng/Run-Dev.ps1`.
- Consumes: Task 1 layout conventions; package metadata with assembly fallback; current `ToolInstallation.IsManaged` contract.

- [ ] **Step 1: Add failing metadata and external-tool tests**

Test that Settings exposes provider-supplied display name, semantic version, contributors, source/report/licence URIs; test an external installation reports status `External`, detail `Updates are managed outside MediaExport.`, retains its full path as accessible detail, and cannot Update/Remove. The breaks caught are XAML literals, reversed links, and long external paths dominating rows.

- [ ] **Step 2: Run focused Settings tests and verify RED**

Run: `dotnet test .\MediaExport.Tests\MediaExport.Tests.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~ToolViewModelTests"`

Expected: FAIL because metadata injection and concise external presentation do not exist.

- [ ] **Step 3: Implement metadata and Settings presentation**

Add the metadata provider boundary and Windows implementation, using package data first and assembly attributes second. Bind About entirely from `SettingsViewModel.App`, correct its links, use one fixed primary tool-action width, shorten external status, keep full paths in tooltip/help text, center theme icon/label groups, normalize Refresh/Open folder dimensions, and add `RepositionThemeTransition` to the expanding content and following sections.

- [ ] **Step 4: Prove the old development path is stale/broken, then add the launcher**

Run the existing unpackaged profile and record its `REGDB_E_CLASSNOTREG` failure or stale registered process path. Add `eng/Run-Dev.ps1` to build/run with `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true`, optionally accepting `-Platform x64`; document it as the canonical command without changing release packaging defaults.

- [ ] **Step 5: Verify Task 2**

Run the focused Settings tests and full x64 build. Run `eng/Run-Dev.ps1`, then verify `Get-Process MediaExport` points at the output just built and its executable timestamp is current. Inspect Settings for equal button columns, centered theme items, concise external rows, dynamic About, transition behavior, and correctly sized header actions.

Expected: tests pass; build has 0 warnings/errors; the live app is not the stale registered `AppX` copy.

- [ ] **Step 6: Commit and push batch 2**

Commit message: `feat: refine settings and development launch`

Push `dev` to `origin` after `git diff --check` and Task 2 verification pass.

---

### Task 3: Typed DRM and machine-readable yt-dlp progress

**Files:**
- Modify: `MediaExport.Core/Exporting/IMediaDownloader.cs`
- Modify: `MediaExport.Core/Media/Ytdlp/YtdlpService.cs`
- Modify: `MediaExport.Core/Settings/SettingsStore.cs`
- Test: `MediaExport.Tests/Media/Ytdlp/YtdlpServiceTests.cs`

**Interfaces:**
- Produces: `ExportFailureKind`, `MediaExportException`, `MediaAvailability`, `IMediaDownloader.InspectAsync`, and `ExportProgress(double? Percent, string? Speed, string? Eta, ExportPhase Phase)`.
- Consumes: yt-dlp JSON `formats[].has_drm`, marked `--progress-template` output, existing container-aware arguments, and existing early tool validation.

- [ ] **Step 1: Add failing downloader tests**

Add literal fixtures proving: all formats with `has_drm:true` return unavailable DRM; any non-DRM format remains exportable; DRM console errors become `MediaExportException` with `DrmProtected`; a marked progress line parses percent/speed/ETA/Downloading; marked post-process lines parse an indeterminate Merging/Converting phase; unrelated output is ignored; download arguments include the unique progress template without weakening container selection.

- [ ] **Step 2: Run focused yt-dlp tests and verify RED**

Run: `dotnet test .\MediaExport.Tests\MediaExport.Tests.csproj -p:Platform=x64 --no-restore --filter FullyQualifiedName~YtdlpServiceTests`

Expected: FAIL because typed availability/failure/phase contracts and marked progress do not exist.

- [ ] **Step 3: Implement typed inspection and error normalization**

Extend the downloader contract with `InspectAsync`; parse full metadata without downloading; classify only the all-formats-DRM case as blocked. Normalize yt-dlp DRM errors into `MediaExportException(DrmProtected, ...)`, leaving other useful errors retryable.

- [ ] **Step 4: Implement deterministic progress transport**

Add a MediaExport-specific `--progress-template` marker and explicit post-processing markers. Parse marked lines from both redirected streams, keep file-path output under its own marker, propagate nullable determinate percentage plus speed/ETA/phase, and do not discard queued progress merely because process completion is fast.

- [ ] **Step 5: Verify Task 3**

Run the focused yt-dlp tests and the full test suite.

Expected: all tests pass; existing argument/remux and failure tests remain green.

- [ ] **Step 6: Commit and push batch 3**

Commit message: `feat: report export progress and drm failures`

Push `dev` to `origin` after `git diff --check` and Task 3 verification pass.

---

### Task 4: Activity selection/retry and Export redesign

**Files:**
- Modify: `MediaExport.Application/Activity/ExportJob.cs`
- Modify: `MediaExport.Application/Activity/ActivityViewModel.cs`
- Modify: `MediaExport.Application/Export/ExportViewModel.cs`
- Modify: `MediaExport/Activity/ActivityPage.xaml`
- Modify: `MediaExport/Activity/ActivityPage.xaml.cs`
- Modify: `MediaExport/Export/ExportPage.xaml`
- Modify: `MediaExport/Export/ExportPage.xaml.cs`
- Modify: `MediaExport/MainWindow.xaml.cs`
- Test: `MediaExport.Tests/Activity/ActivityViewModelTests.cs`
- Test: `MediaExport.Tests/Export/ExportViewModelTests.cs`

**Interfaces:**
- Produces: `ExportJob.CanRetry`, `ExportJob.Retry()`, `ActivityViewModel.Retry(ExportJob)`, selected-job eligibility/actions, and `ExportViewModel.Open(MediaSearchResult, ExportOptions?)` with DRM availability state.
- Consumes: Task 1 comfortable `RowMediaView`/`MediaButton`; Task 3 typed progress, phases, availability, and failure kind; existing history persistence and two-job scheduler.

- [ ] **Step 1: Add failing retry, progress, DRM, and option-restoration tests**

Test that a normal failure is retryable; retry reuses the same object and options, clears stale state, uses a fresh token, and replaces its history record; DRM failure is not retryable; speed/ETA/phase update job display state before completion; opening Export with previous options restores format/quality/folder; blocked inspection disables Export and exposes the DRM message. Add selection-state tests for mixed selected jobs enabling only applicable Retry/Cancel/Remove actions.

- [ ] **Step 2: Run focused Activity/Export tests and verify RED**

Run: `dotnet test .\MediaExport.Tests\MediaExport.Tests.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ActivityViewModelTests|FullyQualifiedName~ExportViewModelTests"`

Expected: FAIL because retry-in-place, typed progress display, selection state, option restoration, and availability are absent.

- [ ] **Step 3: Implement job retry and selection behavior**

Make cancellation tokens replaceable per attempt, reset failed jobs in place, resubscribe them to scheduling/history updates, and keep DRM failures terminal. Add view-model-owned selected-job state and labeled bulk Retry/Cancel/Remove eligibility while leaving actual ListView selection synchronization in the page.

- [ ] **Step 4: Rebuild Activity UI**

Add the Activity title and right-aligned selection toolbar, extended selection, selected count, comfortable media rows, readable status/progress/speed/ETA, one labeled primary action per state, and the agreed context menu. Remove Clear finished. Export again restores the original options; retry resets the row in place.

- [ ] **Step 5: Implement Export inspection and presentation**

Inspect selected media asynchronously, expose loading/available/blocked state, disable configuration/submission for DRM, and render the protected-content message. Reorganize the existing format/quality/location controls with MVP spacing and hierarchy, keep the fixed Export action, use comfortable media presentation, and replace the icon close button with labeled Remove media.

- [ ] **Step 6: Verify Task 4 and integration**

Run focused Activity/Export tests, the full test suite, and a clean x64 build. Launch through `eng/Run-Dev.ps1` and inspect Search, Settings, Activity, and Export, including a real yt-dlp export that lasts long enough to show determinate progress if a non-DRM test URL is already available without introducing a fixture download.

Expected: all tests pass; clean build has 0 warnings/errors; Activity selection/context menus/retry and Export blocked/available states match the spec; no stale AppX process is used.

- [ ] **Step 7: Commit and push batch 4**

Commit message: `feat: redesign activity and export workflow`

Push `dev` to `origin` after `git diff --check` and full verification pass.

---

### Task 5: Whole-branch review and correction pass

**Files:**
- Modify only files required by Critical or Important review findings.
- Test the owning NUnit files for every accepted finding.

**Interfaces:**
- Consumes: Tasks 1–4 and the complete spec.
- Produces: reviewed, verified branch with all accepted high-severity findings fixed test-first.

- [ ] **Step 1: Build the review package and run a fresh whole-branch review**

Use the executing-plans review package from the pre-implementation base through `HEAD`. Review explicitly against the five Review Focus items and all ledger rulings.

- [ ] **Step 2: Re-grade findings and perform one RED→GREEN correction pass**

Fix Critical and Important findings only, each with a failing test before production changes. Ledger Minor findings for the final report.

- [ ] **Step 3: Run final verification**

Run:

`dotnet test .\MediaExport.Tests\MediaExport.Tests.csproj -p:Platform=x64 --no-restore`

`dotnet build .\MediaExport\MediaExport.csproj -p:Platform=x64 --no-restore`

`git diff --check`

Expected: 0 failed tests, 0 build warnings, 0 build errors, and no whitespace errors.

- [ ] **Step 4: Commit and push the final correction batch if needed**

Commit message: `fix: address interface redesign review`

Push only if the review produced code changes; otherwise report the reviewed Task 4 head as final.
