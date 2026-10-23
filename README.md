# Codex Community Connector

Run Codex inside Visual Studio without leaving the IDE.

`Codex Community Connector` is a 64-bit VSIX for Visual Studio 2022 and Visual Studio
2026. It hosts the local Codex CLI through `codex app-server`, adapts the Codex
WebView to Visual Studio, and connects conversations to the active solution,
editor, theme, and user settings.

Author: [Rodrigo Jager](https://rodrigojager.com). See the [changelog](CHANGELOG.md)
for the changes and release history.

Current release: [v1.3.8](https://github.com/rodrigojager/codex-visual-studio-extension/releases/tag/v1.3.8)

> [!WARNING]
> This project is not actively maintained. The 1.3.x updates were exceptional
> maintenance releases and do not imply ongoing development or
> support. Issues and pull requests may not be reviewed. Fork the repository if
> you need continued maintenance or compatibility work for future Codex and
> Visual Studio versions.

> [!IMPORTANT]
> This is an independent project. It is not affiliated with, endorsed by, or
> officially associated with OpenAI or ChatGPT. Logos and product references are
> used only to describe the integration.

## What's New in 1.3.8

- Image attachments preserve their content when selected, pasted, or dropped into
  the chat. Structured attachments are validated and sent inline to the runtime;
  mentioning a filename in text does not upload it.
- The classic composer can send an image without requiring accompanying text.
- Starting sign-in with a missing CLI offers installation through npm with your
  consent. Background startup checks never show an installation prompt, and an
  explicitly configured executable path is preserved.
- Automatic compaction uses the latest usage for each conversation and cancels
  pending work when usage falls, another turn starts, or the setting is disabled.
- Community fixes adapted from Billy Martin's PR #26 are documented in
  [CONTRIBUTORS.md](CONTRIBUTORS.md), together with the issue reporters' credits.

## What It Provides

### Codex inside Visual Studio

- Dockable Codex chat in the Visual Studio tool window area.
- Frozen Codex WebView adapted to Visual Studio WebView2, theme resources, locale,
  and the local app-server transport.
- Normal and plan collaboration modes.
- Runtime model discovery with model-specific reasoning options, verbosity,
  service tier, approval policy, and sandbox controls.
- Streaming assistant output, tool activity, approvals, interactive questions,
  Markdown, diffs, and Mermaid diagrams.
- Clipboard and file-picker image attachments validated and sent as inline
  app-server `image` inputs.
- Session usage and rate-limit information when supplied by the Codex runtime.
- Bounded local recent-task history instead of the incompatible cloud-task view.

### Visual Studio context and commands

- Active document, selected text, and open editor tabs are sent through the IDE
  context contract when IDE context is enabled.
- Solution-aware `@file` search is asynchronous, bounded, and excludes generated
  directories and reparse points.
- Editor context-menu commands can add a selection to the current thread, review
  selected code, or ask Codex to implement it.
- Solution Explorer can add the selected file to the current thread.
- Visual Studio commands are available for opening Codex, starting a new agent,
  and opening settings.
- Plan questions use a separate prompt window so the main conversation remains
  visible.

### Settings

- Settings open in an independent Visual Studio document tab, leaving chat
  available at the same time.
- Changes are persisted immediately and invalidate all active chat consumers;
  there is no Apply button or extension restart requirement.
- Configurable executable path, working directory, model, reasoning, verbosity,
  service tier, profile, approvals, sandbox, follow-up behavior, composer Enter
  behavior, review delivery, managed MCP servers, and startup behavior.
- Optional local diagnostic logging for investigating display or docking
  problems, disabled by default.
- UI localization for English, Brazilian Portuguese, Spanish, French, and German.
- Visual Studio theme integration for light and dark environments.

### Long-conversation behavior

The extension deliberately avoids materializing an entire large conversation in
the Visual Studio WebView at once:

- Initial history is limited to the most recent 120 turns or 2 MB.
- Older history is loaded explicitly in batches of 20 turns, with a 512 KB batch
  budget.
- Large messages, diffs, tool output, and streams are limited only in the Visual
  Studio display copy. The full content remains in the Codex session history.
- Browser-native lazy rendering reduces work for content outside the visible
  viewport.
- Manual context compaction is available, with optional automatic compaction after
  a completed turn reaches 85% context usage.

These controls improve responsiveness, but they do not make conversation size
unlimited. Runtime, model-context, and machine-resource limits still apply.

## Requirements

- Visual Studio 2022 or Visual Studio 2026, 64-bit, with the Core Editor workload.
- .NET Framework 4.7.2 or newer.
- Codex CLI installed locally and available through `codex`, `codex.cmd`, or the
  executable path selected in extension settings.
- A working per-user Codex login or provider configuration.

The VSIX does not bundle the Codex CLI or an OpenAI account.

## Installation

1. Install the Codex CLI and verify that `codex --version` works in a terminal.
2. Run `codex login` with the ChatGPT account that will use Visual Studio, or
   configure that user's provider in `~/.codex/config.toml`.
3. Download the VSIX from
   [GitHub Releases](https://github.com/rodrigojager/codex-visual-studio-extension/releases/latest)
   or use the Marketplace package when available.
4. Run the VSIX installer and follow its instructions for the desired Visual
   Studio instances.
5. Open Visual Studio and choose `View > Codex Community Connector`. The canonical command is
   `View.VisualCodexStudio` and can be assigned a keyboard shortcut.

If authentication is missing, the extension's Account settings can open the
local `codex login` flow.

If the default Codex command is missing when you start sign-in, the extension
offers to run `npm install --global @openai/codex`. This requires Node.js/npm and
your confirmation. If npm is unavailable, installation instructions are shown.
An invalid custom executable path must be corrected in settings instead.

## Authentication and Local Data

Authentication always belongs to the Windows user running Visual Studio. The
recommended path is that user's own `codex login` session. Advanced local setups
can instead use:

- `OPENAI_API_KEY` in that user's environment.
- Provider configuration in `~/.codex/config.toml`.
- A Codex profile that selects provider-specific configuration.

No ChatGPT cookie, token, API key, `auth.json`, publisher credential, or signing
key is bundled in the repository or VSIX. The WebView is not given
`OPENAI_API_KEY` from the Visual Studio process.

Extension settings are stored at `%LOCALAPPDATA%\CodexVsix\settings.json`, outside
the repository and VSIX. Sensitive extension settings and prompt history are
protected for the current Windows user with DPAPI and written atomically. Codex
authentication and provider configuration remain under that user's Codex home
directory and must never be copied into this repository.

When enabled, optional diagnostics are stored under
`%LOCALAPPDATA%\CodexVsix\logs`. They do not intentionally record prompts,
responses, or request contents and filter common credential formats, but error
details can still include local paths or other environment information. Review
the files before sharing them publicly.

## Configuration Notes

- Provider and profile behavior should be configured in `~/.codex/config.toml`.
- The settings UI supports extra model, reasoning, verbosity, and service-tier
  entries for runtimes that expose options newer than this frozen extension.
- Additional CLI arguments, environment overrides, and raw TOML overrides are
  advanced per-user settings. They are passed to the local runtime and should be
  reviewed before use.
- Attached local images must remain readable until the turn starts.
- Image attachments support PNG, JPEG, GIF, still WebP, and BMP (converted to
  PNG), with limits of 20 MiB per image, 40 MiB total per turn, and 25 megapixels
  per image. The modern picker classifies PNG, JPEG, GIF, and WebP as images.
- The embedded WebView is a frozen bundle. A future Codex CLI or protocol change
  may require source changes that this unmaintained project will not receive.

## Build and Test

Release packaging requires full Visual Studio MSBuild.

```powershell
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$install = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$msbuild = Join-Path $install "MSBuild\Current\Bin\MSBuild.exe"

[xml]$props = Get-Content Directory.Build.props -Raw
$version = [string]$props.Project.PropertyGroup.Version

dotnet restore CodexVs2026Extension.sln `
  --locked-mode `
  -p:NuGetAudit=true `
  -p:NuGetAuditMode=all `
  -p:WarningsAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904

dotnet test CodexVs2026Extension.sln -c Release --no-restore

& $msbuild `
  CodexVsix\CodexVsix.csproj `
  /t:Rebuild `
  /m:1 `
  /nr:false `
  /p:Configuration=Release `
  /p:RestoreLockedMode=true `
  /p:BuildVsixPackage=true

.\scripts\Test-VsixPackage.ps1 `
  -VsixPath CodexVsix\CodexVsix.vsix `
  -ExpectedVersion $version
```

`dotnet build` can compile the projects, but the final VSIX should be produced
with Visual Studio `MSBuild.exe`. The package verifier checks manifest and
assembly versions, required WebView assets, third-party runtime versions,
credential filenames, private signing material, and package signatures.

GitHub CI performs locked restore with NuGet auditing, tests, packaging, and VSIX
verification. Tagged releases are signed only when all repository signing secrets
are configured; otherwise the workflow publishes a verified unsigned package.
Visual Studio Marketplace publication is intentionally manual.

## Changelog

### 1.3.8 - 2026-10-23

- Fixed image attachment transport and image-only submissions in the classic composer.
- Added consent-based CLI installation at sign-in, with bounded execution and custom-path protection.
- Fixed stale automatic compaction decisions and cancellation between turns.
- Validated with 191 .NET tests, 8 WebView tests, package checks, and local Visual Studio acceptance.
- Credited Billy Martin (@BillyMartin1964), @maghilum, and @jtownzen-42 in the changelog and packaged [CONTRIBUTORS.md](CONTRIBUTORS.md).

### 1.3.7 - 2026-09-30

- Renamed the extension to Codex Community Connector and updated the logo and author URL.
- Corrected diff updates, model catalog pagination, Max/Ultra selection, provider authentication, and native executable discovery.
- Preserved settings and command identities; added regression tests and packaged release notes.
- See [CHANGELOG.md](CHANGELOG.md) for the complete release notes.

### 1.3.4 - 2026-07-22

- Recovers the modern interface through windowed WebView2, composition WebView2,
  and an isolated recovery profile before falling back to classic WPF.
- Detects a missing `webview-ready` signal, logs every recovery attempt, and adds
  a "Try the modern interface again" action to classic fallback surfaces.
- Keeps a settings-only WebView failure local instead of downgrading the main chat.
- Retries the official interface after Visual Studio restarts instead of keeping
  a transient WebView fallback across later sessions.
- Handles scalar errors from custom providers without a Newtonsoft `JValue`
  parsing failure and surfaces the provider's actual message.
- Records app-server requests, response outcomes, stderr, and process failures
  when diagnostic logging is enabled, without logging prompts or request payloads.
- Opens classic settings on a usable section instead of an empty panel.

### 1.3.3 - 2026-07-18

- Improved reliability when the Codex panel is docked, floating, or moved.
- Remembered the interface that works on each computer and added an automatic
  fallback, without loading two interfaces at the same time.
- Preserved the responsiveness improvements from 1.3.1 by reloading the panel
  only when Visual Studio actually moves it to another window.
- Added optional local diagnostic logs, disabled by default, and fixed their
  switch so it appears correctly under General settings.
- Expanded automated coverage for docking, fallback, diagnostics, and release
  packaging.

### 1.3.1 - 2026-07-16

- Reduced editor slowdown while the Codex tool window is open.
- Prevented the official WebView and the classic interface from loading at the
  same time.
- Kept the classic interface as a fallback when the official interface cannot
  load.
- Deferred automatic opening until Visual Studio reaches an idle state.
- Improved cleanup of the tool window and WebView resources.
- Added regression tests for the new loading and startup behavior.

### 1.3.0 - 2026-07-11

- Replaced the older presentation with the adapted Codex WebView, Visual Studio
  theme integration, Codex-style header/composer, and an independent settings tab.
- Added current IDE commands and context menus for selections, files, reviews, and
  implementation requests.
- Added active document, selection, and open-tab context using the WebView's
  expected IDE contract.
- Added runtime model discovery and host metadata so the assistant can identify
  the selected model and reasoning effort.
- Added bounded history loading, lazy rendering, display-only payload limits,
  manual compaction, and optional automatic compaction for long conversations.
- Replaced incompatible cloud task history with a bounded local task-history
  popover.
- Updated app-server support for reviews, steering, approvals, permissions,
  elicitation, plugins, MCP operations, authentication, and logout.
- Hardened process cancellation, follow-up queues, attachments, solution search,
  Markdown/Mermaid rendering, settings persistence, and WebView dispatch.
- Protected sensitive local settings with DPAPI and added package checks that
  reject credential and private signing files.
- Added locked dependency restore, NuGet auditing, 101 automated tests, reproducible
  versioning, VSIX verification, and optional release signing.

### 1.2.1 - 2026-05-01

- Refreshed the VSIX manifests and packaged extension metadata.

### 1.2.0 - 2026-05-01

- Renamed the extension to Visual Codex Studio and refreshed its iconography.
- Added conversation rename support and keyboard command registration.
- Virtualized chat history and buffered large message updates to reduce UI churn.
- Fixed history loading, rate-limit presentation, and layout instability.

See [CHANGELOG.md](CHANGELOG.md) for the complete release history and detailed
change list.

## Repository Layout

- `CodexVsix/`: Visual Studio extension, app-server host, WebView bridge, and UI.
- `CodexVsix.Tests/`: automated regression and package-contract tests.
- `scripts/Test-VsixPackage.ps1`: release package verifier.
- `scripts/Set-VsixVersion.ps1`: synchronized version updater.
- `marketplace/overview.md`: Marketplace-facing product description.
- `THIRD-PARTY-NOTICES.md`: bundled third-party notices and reviewed asset hashes.
- `CONTRIBUTORS.md`: credits for community reports and incorporated fixes.

## License and Third-Party Code

The project source is available under the [MIT License](LICENSE). Bundled
third-party components and the frozen WebView provenance are documented in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and
[`CodexVsix/UI/CodexWebview/SOURCE.md`](CodexVsix/UI/CodexWebview/SOURCE.md).
