# Codex for Visual Studio

Run Codex inside Visual Studio without leaving the IDE.

`Codex for Visual Studio` adds a docked Codex tool window to Visual Studio 2022 and 2026. It uses your local Codex CLI installation, keeps the conversation inside Visual Studio, and stays compatible with the active Visual Studio theme and the extension's current UI languages.

## Highlights

- Docked chat-style Codex panel inside Visual Studio
- Normal mode and plan mode
- Separate additional-information prompt window so the chat stays visible while answering plan questions
- Markdown output with a render/text toggle for easier reading and copy/paste
- Model selection
- Reasoning effort selection (`minimal`, `low`, `medium`, `high`, `xhigh`)
- Verbosity selection (`low`, `medium`, `high`)
- `approval_policy` and `sandbox_mode` selection
- Local settings persistence
- Local prompt history
- Image attachment from the clipboard or file picker
- `--image` support when calling the installed Codex runtime
- Solution-aware `@file` search while typing
- Session usage and rate-limit visibility in the Visual Studio UI

## Authentication and Provider Support

The extension follows the same local Codex setup you already use on the machine.

Supported setups include:

- Codex login on the local machine
- `OPENAI_API_KEY`
- Provider-based configuration in `~/.codex/config.toml`
- Profile-based configuration that selects a provider from `config.toml`

This means the extension does not force OpenAI login when your Codex CLI is already configured to use a compatible third-party provider through `config.toml`.

## Requirements

- Visual Studio 2022 or Visual Studio 2026, 64-bit
- Codex CLI installed locally
- A working local Codex authentication or provider configuration

## Notes

- The extension calls your local Codex CLI or app-server flow; it does not bundle the runtime.
- If your local Codex setup already works in the terminal, the extension is designed to reuse that setup.
- Image attachments depend on the installed `codex` runtime supporting `--image`.
- Theme support uses Visual Studio theme resources so the UI works in light and dark themes.
- UI strings remain localized through the extension's existing localization pipeline.

## Manual Future-Proofing

The extension keeps its local settings in `%LOCALAPPDATA%\CodexVsix\settings.json`. If new Codex or provider capabilities appear before the extension is updated, users can open this file from the Codex settings UI or edit it directly.

Useful fields:

- `CustomModels`: extra model ids shown in the model selector.
- `CustomReasoningEfforts`: extra reasoning effort values shown in the reasoning selector.
- `CustomVerbosityOptions`: extra verbosity values shown in the verbosity selector.
- `CustomServiceTiers`: extra service tier values shown in the speed selector.

Manual option entries can be either a raw value, such as `"minimal"`, or a label/value pair, such as `"Very high|very_high"` or `"Very high=very_high"`.

Provider and profile configuration should continue to live in `~/.codex/config.toml`; the extension starts the local Codex runtime and lets that runtime resolve provider-specific behavior.

## Build

Release packaging uses full Visual Studio MSBuild.

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  CodexVsix\CodexVsix.csproj `
  /t:Build `
  /p:Configuration=Release `
  /p:BuildVsixPackage=true
```

`dotnet build` can compile the project, but VSIX packaging targets should be produced with Visual Studio `MSBuild.exe`.

## Repository

- Extension project: `CodexVsix/CodexVsix.csproj`
- Marketplace overview: `marketplace/overview.md`
