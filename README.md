# Codex for Visual Studio

VSIX extension that adds a docked Codex tool window to Visual Studio 2022 and 2026.

## Features

- Docked chat-style tool window inside Visual Studio
- Model selection
- Reasoning / thinking effort selection (`minimal`, `low`, `medium`, `high`, `xhigh`)
- Verbosity selection (`low`, `medium`, `high`)
- `approval_policy` and `sandbox_mode` selection
- Local settings persistence
- Use the open solution folder as the working directory
- Open or edit `~/.codex/config.toml`
- Local prompt history
- Image attachment from the clipboard or file picker
- `--image` support when calling the installed Codex runtime
- Solution-aware `@file` search while typing

## Requirements

- Visual Studio 2022 or Visual Studio 2026, 64-bit
- Authentication for Codex on the machine

## Notes

- Release packaging uses full `MSBuild`; a normal Release build of `CodexVsix.csproj` now produces `CodexVsix.vsix`.
- If you build from the command line, use Visual Studio `MSBuild.exe` rather than `dotnet build` because VSIX packaging targets do not run under Core MSBuild.
- The extension resolves the installed Codex executable from the configured path or the machine environment.
- The `@file` picker is implemented in the extension UI and inserts relative paths into the prompt.
- Image support depends on the installed `codex` runtime supporting `--image`.

## Release Notes

- Version 1.1.10 now works better with Light Theme, thanks to [ppd50](https://github.com/ppd50)
