# Changelog

## 1.2.0 - 2026-05-01

### Added
- Inline rename for saved conversations in the history lists, preserving the original Codex/GPT thread ID so existing context resumes normally.
- Keyboard shortcut registration for the main `View.VisualCodexStudio` command, with the command exposed through Visual Studio keyboard settings.
- Shared history item template across recent, visible, and full history lists to keep rename and delete actions consistent.

### Changed
- Renamed the extension branding to `Visual Codex Studio`.
- Updated the extension icon used by the VSIX package, installed extensions list, and Marketplace metadata.
- Updated the `View > Codex` command icon to use the custom ChatGPT/Codex visual asset.
- Refined the history UX so saved conversations can carry meaningful labels without affecting their backing thread identity.
- Reworked chat message presentation to use a virtualized list surface instead of rendering the whole conversation in a single stacked panel.
- Buffered assistant output updates and switched large message list refreshes to batch operations to reduce UI churn in long conversations.

### Fixed
- Restored the rate-limit usage indicator as a proper donut chart instead of a stretched shape.
- Fixed the thread rename UI so it no longer breaks tool window loading at runtime.
- Reduced chat layout jumps during interactions such as copying content while keeping markdown rendering and collapse/expand behavior intact.
