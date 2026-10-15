# Toolchain Ownership and Test Architecture Design

## Purpose

MediaExport should run reliably without forcing users to duplicate tools they already maintain, while still offering a predictable app-managed installation. The codebase should make this ownership rule explicit, keep WinUI concerns out of unit tests, and make production code testable without compiling source files into the test assembly by hand.

Success means:

- MediaExport prefers its own managed tool when one exists.
- A compatible external tool on `PATH` is used automatically as a fallback.
- MediaExport never updates or removes an external tool.
- Settings makes the active tool, its ownership, and available actions unambiguous.
- Unit tests reference production assemblies normally and exercise public behavior.
- The solution builds without the current C# and XAML warnings addressed by this work.
- The resulting code remains small enough for the team to understand by reading each component in isolation.

## Project Boundaries

Introduce a `MediaExport.Application` class library between the WinUI project and `MediaExport.Core`.

### `MediaExport.Core`

Owns domain and infrastructure code that does not depend on presentation:

- media models and search providers;
- exporting abstractions and download services;
- settings persistence and recent-list storage;
- tool discovery, installation, version inspection, updating, and removal.

### `MediaExport.Application`

Owns application state and orchestration that is independent of WinUI:

- `SearchViewModel` and search-term formatting helpers;
- `ExportViewModel`;
- `ActivityViewModel` and `ExportJob`;
- `SettingsViewModel` and `ToolViewModel`;
- presentation-oriented formatting helpers that do not implement WinUI interfaces;
- the `IThemeService` abstraction used by `SettingsViewModel`.

Existing namespaces remain stable where practical so XAML and calling code require minimal changes. Files move physically into the project that owns them; they are not linked into multiple projects.

### `MediaExport`

Owns WinUI views, controls, converters that implement WinUI interfaces, and platform implementations such as `ThemeService`. It references `MediaExport.Application`, which references `MediaExport.Core`.

### `MediaExport.Tests`

References `MediaExport.Core` and `MediaExport.Application`. It contains no manual `<Compile Include="../MediaExport/...">` entries and does not compile production sources a second time.

## Tool Resolution and Ownership

### Explicit result

Tool discovery returns a value describing both the executable and its ownership:

```text
ToolInstallation
  Path
  Origin: Managed | External
```

Callers no longer infer ownership from a path each time. Path containment is still checked inside the toolchain boundary when discovering managed files, using normalized full paths and directory boundaries rather than a raw prefix comparison.

### Resolution order

For each tool:

1. Use the configured MediaExport-managed installation when its executable exists.
2. Otherwise search the injected `PATH` directories in order.
3. If neither exists, report the tool as missing.

An external tool is accepted when it can be located and its normal version command can be executed. Failure to execute or inspect it is surfaced as a tool error rather than silently treated as a healthy installation. No minimum-version policy is introduced until the application has concrete compatibility requirements.

### Managed operations

Install, update, and remove are legal only for managed copies:

- `Install` creates or replaces a managed copy without modifying an external installation.
- `Update` is available only when the active copy is managed and the tool has a supported download source.
- `Remove` deletes only the managed executable and clears its managed settings. Discovery then naturally falls back to an external copy if available.
- Auto update considers managed copies only.

Downloads use an injected `HttpClient`. Files are downloaded to a temporary file in the managed tool directory and moved into place only after a complete successful download, preventing a failed update from destroying the working executable.

### Configuration and boundaries

`ToolchainService` receives a small options value containing the managed folder and external search path. Production constructs it from the application data folder and process environment. Tests provide temporary folders and an explicit search path.

Process execution is behind one narrow `IProcessRunner` interface so version success, invalid executables, non-zero exits, and cancellation can be tested without starting arbitrary programs. File-system behavior uses real temporary directories rather than a broad mock file-system abstraction.

## Settings Experience

The Settings page keeps only the approved Appearance and Tools sections.

The Tools section contains:

- an auto-update row explicitly labeled as applying only to tools installed by MediaExport;
- an expanded installed-tools panel;
- `Refresh` and `Open folder` actions in the panel header;
- one row per required tool with name, version, status, ownership, and path where useful.

Row actions follow one hierarchy:

- Missing and installable: `Install`.
- External and installable by MediaExport: `Install managed copy`.
- Managed and updateable: `Update`.
- Managed: `Remove`.
- Every tool: equal-size Website and Source icon buttons.

Install and update actions share a common text-button height and minimum width. Website, Source, and Remove share one square icon-button style. The page contains no overflow menus and no group-level `Update all` action.

External rows say that MediaExport is using a tool installed outside the app and that its installer or package manager owns updates and removal. After `Install managed copy`, the row switches to the managed copy because managed discovery has priority.

## Error Handling

Tool operations expose concise user-facing failures while preserving the previous usable state:

- discovery and version errors are reported on the relevant row;
- download, write, and permission failures leave an existing managed executable intact;
- cancellation removes temporary files and does not report a failure;
- removal failures keep the managed registration and show the operating-system error;
- an unavailable managed path falls back to external discovery rather than remaining falsely installed.

Commands disable while their operation is running. A single operation state prevents overlapping install, update, refresh, and removal actions on the same tool.

## Test Strategy

Tests verify observable behavior through public APIs. They do not inspect XAML, use reflection to reach private setters, or assert that generated command properties merely exist.

### Toolchain tests

- Happy path: a managed executable is selected before an external executable.
- Basic input: an external executable is selected when no managed copy exists.
- Missing input: no installation is returned when neither location contains the executable.
- Boundary: a similarly prefixed directory is not classified as the managed folder.
- Happy path: installing writes the complete managed executable and persists its location.
- Replacement: updating atomically replaces a managed executable.
- Failure: a failed download leaves an existing executable unchanged and removes temporary data.
- Protection: update and removal reject external installations.
- Fallback: removing a managed copy reveals an available external copy.
- Process success: version output is parsed from a successful command.
- Process failure: start failure, non-zero exit, unreadable output, and cancellation are surfaced predictably.

### Application tests

- Tool rows expose the correct actions for missing, external, and managed states.
- Refresh transitions from checking to the resolved installation and version.
- Install/update/remove commands delegate once, refresh afterward, and expose failures.
- Search, activity, and export tests focus on business workflows: successful operations, ordinary inputs, boundaries, cancellation, dependency failures, and persistence restoration.
- Tests whose only purpose is UI wiring or a trivial property assignment are removed.

Test fixtures use small named fakes for network/process boundaries and real temporary directories for files. Shared builders create representative media, settings, and tool definitions without obscuring the behavior under test.

## Warning Cleanup

This work resolves warnings in touched and currently reported application code:

- rename `Loaded` visual states so generated fields do not hide `FrameworkElement.Loaded`;
- replace contextual `field` setters with explicit backing fields;
- make the `SearchTermBox` identity comparison type-safe;
- remove duplicate XAML values already supplied by styles;
- explicitly set grid placement where adaptive visual states later change it.

Warnings are fixed at their cause. No warning suppressions are added.

## Migration Sequence

1. Add the Application project and project references.
2. Move WinUI-independent application files without changing behavior.
3. Remove linked compile entries and restore a green test/build baseline.
4. Introduce explicit tool installation ownership and injectable boundaries.
5. Add behavior-first toolchain and tool-row tests.
6. Implement managed-first/external-fallback operations and atomic downloads.
7. Refine Settings actions and copy.
8. Replace weak or UI-structure tests with workflow tests.
9. Resolve reported warnings and verify all architectures supported by the solution where the host can build them.

## Non-goals

- Bundling every external tool into the application package.
- Updating, deleting, or otherwise managing tools installed by another package manager.
- Introducing minimum supported versions without an identified compatibility requirement.
- UI automation tests inside the unit-test project.
- A generic repository, file-system abstraction, or mocking framework solely for test convenience.
- Unrelated redesigns outside Settings, Activity, and Export changes already in progress.
