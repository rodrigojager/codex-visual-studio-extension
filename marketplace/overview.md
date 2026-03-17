# Codex for Visual Studio

Run Codex without leaving Visual Studio.

`Codex for Visual Studio` adds a docked tool window that lets you work with Codex directly inside Visual Studio 2022 and 2026. It keeps the prompt, settings, and recent history inside the IDE while using your local Codex CLI installation.

## Highlights

- Chat-style Codex panel inside Visual Studio
- Model, reasoning effort, and verbosity controls
- `approval_policy` and `sandbox_mode` controls
- Solution folder as working directory
- Local prompt history
- Clipboard and file-based image attachments
- `@file` lookup against the current solution

## Requirements

- Visual Studio 2022 or Visual Studio 2026, 64-bit
- Codex CLI installed locally
- Codex CLI already authenticated on the machine

## Notes

- The extension calls your local Codex CLI; it does not bundle the CLI.
- Image attachments depend on the installed `codex` CLI supporting `--image`.
- The extension is distributed as a standard VSIX package through the Visual Studio Marketplace.
