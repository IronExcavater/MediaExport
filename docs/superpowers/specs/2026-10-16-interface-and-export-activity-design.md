# MediaExport interface and export activity design

## Purpose

Make MediaExport's main surfaces clearer, more consistent with the MVP, and easier to evolve without duplicating media presentation code. The work covers Search, shared media views, Settings, Activity, Export, yt-dlp progress and DRM reporting, and the local development launch path. It does not add new export formats or quality controls.

## Product principles

- Prefer readable labels and direct actions over clusters of small icon-only buttons.
- Keep secondary or infrequent item actions in context menus.
- Preserve selectable media text without making ordinary clicks on that text feel broken.
- Put shared presentation behavior behind explicit control properties and events; page-specific commands remain owned by their pages or view models.
- Report yt-dlp state honestly. A determinate percentage is shown only when yt-dlp supplies one.
- External tools are valid fallbacks, but MediaExport only updates or removes copies it manages.

## Shared media presentation

`MediaView` remains the common base for media data, thumbnail sizing, selectable text, and activation behavior. `RowMediaView` exposes an explicit comfortable or compact presentation setting. Comfortable is the default and uses a thumbnail and typography scale comparable to `CardMediaView`; only the title-bar `resultsPopup` opts into the compact presentation.

Consumers continue to own selection, commands, context menus, and surrounding chrome. Activity composes `RowMediaView` inside a selectable job container rather than embedding another private media layout. The controls expose documented properties and activation events so external consumers can configure them without depending on their internal named elements.

Text selection and activation are distinguished by pointer intent. A drag or a non-empty text selection does not activate the media. A normal press and release that does not select text invokes the owning media action even when the pointer is over selectable text.

Search's justified layout stretches the final result row using the same width policy as preceding rows.

## Search presentation

The Search heading presents the human query directly instead of generating a sentence from all terms. A result page uses the words as its title, such as `LCD`. Providers and structured terms appear in a compact metadata line, such as `YouTube · exact “asd” · excluding “asd”`.

An empty result changes the heading itself to `No results for “LCD”`. The pagination surface contains pagination only and is hidden when there are no result pages. No empty-result explanation appears under the pagination controls.

The provider flyout uses explicit theme-aware item chrome and removes the opaque white selection oval.

## Settings

### Tools

Tool rows use one fixed primary-action width so `Install`, `Install managed copy`, and `Update` align. Refresh and Open folder use consistent explicit heights, padding, and content alignment.

An external tool is presented as healthy, with an `External` status and the concise detail `Updates are managed outside MediaExport.` The complete executable path remains available through tooltip and accessibility help text. Update and Remove are exposed only for MediaExport-managed installations.

The Settings content and tool collection use reposition transitions so expanding or collapsing Installed tools moves surrounding content smoothly.

### Theme and About

Theme choices center the combined icon-and-label group horizontally and align both elements on the same vertical center in the selected value and dropdown items.

About remains. Its display name, version, publisher/contributor text, and links come from a small testable metadata model instead of hard-coded XAML. Packaged metadata is preferred, with assembly metadata as the development fallback. Report-problem, source, and licence links are mapped to their correct destinations.

## Activity

Activity has a visible page title and a toolbar aligned to its right. Jobs use larger, readable rows based on the comfortable `RowMediaView` presentation.

The job list supports extended selection. The toolbar reports the selected count and provides labeled Select all, Retry, Cancel, Remove, and clear-selection behavior. Each command is enabled only when at least one selected job supports it. Bulk actions ignore selected jobs to which the action does not apply.

Each row exposes one visible, meaningful primary action:

- queued or running: Cancel;
- failed and retryable: Retry;
- completed: Show in folder;
- DRM-protected: no misleading retry action.

Secondary row actions live in a context menu: Open file, Show in folder, Export again…, Copy link, and Remove from activity. Export again opens the Export page with the job's original choices restored but does not start it automatically.

Retry resets a failed row in place. It reuses the same media and export options, creates a fresh cancellation token, clears stale files, progress, completion time, and error state, then returns that same object to the queue. Persisted history is updated rather than duplicated. DRM failures are not retryable.

## Progress and DRM

The downloader requests a dedicated, machine-readable yt-dlp progress template with a MediaExport-specific marker. It parses progress from the stream yt-dlp writes and keeps final-file reporting separately marked. Download progress carries percentage, speed, ETA, and a phase. Post-processing phases such as merging or converting are indeterminate when yt-dlp provides no percentage.

Activity displays the current phase, a determinate progress bar when percentage exists, and speed and ETA when supplied. It no longer jumps directly from `Looking it up` to completion when intermediate progress was reported.

DRM handling follows the MVP behavior at two boundaries:

1. Export inspects the available formats before enabling export. If formats exist but every usable format is DRM-protected, the page presents a clear protected-content message and disables export configuration and submission.
2. If yt-dlp reports DRM only when downloading, the error is normalized into the same typed DRM failure in Activity. The row explains that the media is DRM-protected and cannot be exported, and Retry is unavailable.

Other failures remain retryable and retain their useful yt-dlp error text.

## Export page

The Export page keeps the current feature set. Its editor is reorganized into a concise format, quality, and location form with consistent labels, spacing, and grouping modeled on the MVP. The primary Export action remains fixed and prominent.

The selected media area uses the comfortable shared media presentation. The small close icon is replaced with a labeled Remove media action. DRM status is shown near the media and export controls, not as an Activity-only failure.

## Development workflow

The repository provides one canonical development launcher that builds and starts the exact unpackaged output just produced. It uses `WindowsPackageType=None` and a self-contained Windows App SDK runtime when the machine does not have a suitable registered runtime. It never relies on or copies into a previously registered `AppX` directory.

The workflow documents the command and verifies the launched process path and build timestamp. Packaged builds remain a separate release-testing workflow.

## Third-provider recommendation

Adding another provider is moderate rather than architectural work because the provider abstraction, picker, and mixed-result flow already exist. Bilibili is the preferred next provider: yt-dlp currently exposes the native `bilisearch:` prefix, so it requires a provider class, registration, icon, query mapping, and focused tests rather than a custom search API. Adding the provider is not part of this implementation unless separately requested.

## Testing and delivery

Behavioral changes use unit tests first for search-heading composition, DRM classification, progress parsing, retry-in-place state transitions, selection command eligibility, app metadata fallback, and export-option restoration. XAML-only presentation changes are verified through a clean x64 build and inspection of the current unpackaged output.

Implementation is committed and pushed in coherent batches:

1. shared media presentation and Search;
2. Settings metadata, layout, and development workflow;
3. downloader progress and DRM domain behavior;
4. Activity selection/retry and Export presentation;
5. final integration verification and small follow-up corrections.
