# Changelog

## 1.3.8 - 2026-10-23

### Fixed
- Image attachments from the picker, clipboard, drag-and-drop and classic composer now preserve actual image content. Binary reads expose the field expected by the shipped WebView; pasted filenames are no longer mistaken for filesystem paths (#25).
- Structured image inputs are validated, deduplicated and converted to inline data for both new and steered turns. File signatures determine the media type, BMP is converted to PNG, and bounded reads prevent oversized attachments. Mentioning a filename in text does not upload it.
- Missing CLI sign-in now offers installation with consent, respects custom executable paths, and never prompts during background startup probes (#24). Installer processes have bounded execution, cancellation and output, and refreshed PATH entries are deduplicated.
- Automatic compaction uses the latest per-conversation usage, including zero. Lower usage, a new turn or disabling the setting cancels pending compaction; duplicate completions and cooldowns are respected (#23).
- Updated the build-only MessagePack override to 3.1.9 for the strict dependency audit.

### Validation
- Passed 191 .NET tests and 8 behavior tests against the shipped WebView assets.
- Passed locked dependency restore, strict NuGet auditing, Release packaging and installed-file integrity verification.
- Installed and manually tested in Visual Studio 2026, with user acceptance before publication.

### Contributors
- Billy Martin (@BillyMartin1964): PR #26, the missing CLI report (#24), image investigation and proposed fixes adapted in this version.
- @maghilum: image attachment report (#25).
- @jtownzen-42: automatic compaction reproduction and initial fix suggestion (#23).
- See [CONTRIBUTORS.md](CONTRIBUTORS.md) for links to the reports and incorporated contribution.

## 1.3.7 - 2026-09-30

### Changed
- Renamed the extension to Codex Community Connector and replaced the Marketplace and command icons with the supplied community logo.
- Kept the extension identity and canonical command names so existing settings, shortcuts, and upgrades remain compatible.
- Updated Extension Manager metadata to describe Codex Community Connector as an independent community project using the local Codex CLI.
- Set the package's More Information URL and the author link in the Marketplace overview to https://rodrigojager.com, without www.
- Included the current changelog in the VSIX as ReleaseNotes.txt and linked the Extension Manager release notes to this packaged file.
- Updated the current product name in third-party notices while retaining historical changelog entries.

### Fixed
- The chat selector's actual reasoning-label component now renders Max and Ultra instead of empty entries; existing localized labels remain unchanged.
- Chat reasoning validation now follows the selected model's runtime-supported values, so Max and Ultra are retained after selection and unsupported efforts remain excluded.
- Automatic diff updates only update an open artifact for the same conversation; they no longer open unsolicited editor tabs or move focus.
- Model discovery follows catalog pagination and sends the hidden-model preference, preserving runtime capabilities and model announcement metadata.
- Added GPT-6.1 Sol, GPT-6 Astra, GPT-6 Sol, and GPT-6 Luna to the fallback catalog while retaining previous choices and saved selections.
- The frozen composer displays Max and Ultra reasoning labels and reuses its existing icon styling.
- Custom-provider authentication fallback resolves the active profile, waits for configuration inspection, and preserves explicit app-server authentication decisions and account metadata.
- Native codex.exe installations are discovered after the existing codex.cmd lookups, without substituting explicit paths or changing the preference for an existing npm installation.
- Frozen WebView assets use a versioned cache origin while the shell origin and persisted state remain stable.

### Validation
- Added behavior tests for diff lifecycle, pagination, provider authentication, executable selection, and the shipped WebView model controls.
- Added regression tests covering the label component and validation functions used by the chat selector for Sol, Astra, Luna, and future runtime efforts.
- Added a read-only local runtime probe; it does not submit prompts or start inference turns.

## 1.3.4 - 2026-07-22

### Corrige a alternativa visual e torna os diagnósticos úteis
- Recupera a interface moderna em etapas: WebView2 windowed, WebView2 composition
  e um perfil WebView2 isolado antes de recorrer à interface clássica WPF.
- Detecta quando a navegação termina mas a interface não envia o sinal de pronta,
  registra cada tentativa e oferece a ação "Tentar a interface moderna novamente".
- Mantém uma falha da janela de Configurações isolada, sem trocar o chat principal
  para a interface clássica.
- Tenta novamente a interface oficial após reiniciar o Visual Studio, sem deixar
  uma falha transitória prender as próximas sessões à interface clássica.
- Aceita erros simples retornados por providers personalizados e mostra a mensagem
  real em vez de falhar ao interpretar o JSON.
- Registra o ciclo do app-server, requests, respostas e stderr quando os
  diagnósticos estão habilitados, sem incluir o prompt ou os parâmetros enviados.
- Abre a interface clássica de configurações diretamente em uma seção utilizável.

## 1.3.3 - 2026-07-18

### Deixa o chat mais confiável em modo dock e em janela
- Melhora o funcionamento do painel ao encaixar, soltar ou mover a janela do
  Codex dentro do Visual Studio.
- Aprende qual interface funciona melhor no computador atual e começa por ela
  nas próximas sessões, usando a alternativa automaticamente quando necessário.
- Mantém apenas uma interface carregada por vez e preserva as melhorias de
  velocidade da versão anterior.
- Evita recarregamentos desnecessários enquanto o painel permanece na mesma
  janela do Visual Studio.
- Adiciona logs de diagnóstico opcionais em **Configurações > Geral**, sem exigir
  reinicialização e sempre desligados por padrão.
- Corrige a exibição dessa opção na tela de configurações e mantém os arquivos
  locais com rotação automática.
- Os logs evitam credenciais em formatos comuns, mas ainda devem ser revisados
  antes de serem compartilhados publicamente.
- Amplia os testes automáticos para evitar regressões no dock, na alternativa
  automática, nos logs e no pacote de instalação.

## 1.3.1 - 2026-07-16

### Melhora o desempenho e a estabilidade da janela do Codex
- Reduz a lentidão do editor enquanto a janela do Codex está aberta.
- Evita o carregamento simultâneo de duas interfaces.
- Mantém a interface anterior como alternativa caso a principal não carregue.
- Posterga a abertura automática até o Visual Studio estar pronto.
- Melhora o encerramento e a liberação dos recursos da extensão.
- Adiciona testes para evitar que esses problemas retornem.

## 1.3.0 - 2026-07-11

### Added
- VS Code-style Codex header, composer controls, and accessible settings detail panel.
- Visual Studio command parity for opening the sidebar, starting a new Codex agent, adding editor selections/files to the current thread, running `/review`, and asking Codex to implement selected context.
- Context menu entries for editor selections and Solution Explorer files.
- VS Code-compatible extension settings for composer Enter behavior, follow-up mode, review delivery, and open-on-startup.
- Automated tests for app-server arguments, path containment/indexing, and protected settings persistence.
- Windows CI for locked restore, NuGet audit, tests, warning-free build, VSIX packaging, and package verification.
- Optional release signing through Microsoft's Sign CLI when repository signing credentials are configured.
- Bounded official-WebView history with explicit older-message batches, display-only payload limits, and responsive history controls.
- Manual conversation compaction and optional automatic compaction at 85% context usage.

### Changed
- Enter now sends from the composer by default, while Shift+Enter inserts a newline and Ctrl+Enter remains an explicit send shortcut.
- Updated the integration to the current Codex app-server methods for plugins, reviews, steering, approvals, permissions, elicitation, and logout.
- Updated Visual Studio, WebView2, Newtonsoft.Json, Community Toolkit, and transitive MessagePack dependency baselines.
- Updated the embedded Mermaid bundle to 11.15.0 and added packaged third-party notices.
- Sensitive settings and prompt history are now protected with Windows DPAPI and saved atomically.
- Authentication guidance now makes per-user `codex login` the recommended path, and the official WebView no longer receives `OPENAI_API_KEY` from the Visual Studio process.
- Refreshed fallback model choices from the installed Codex 0.144 catalog, with GPT-5.6 Sol as the default for new settings.
- Localized the new model, IDE-command, history, Markdown, and skill-scope UI across all five supported languages.

### Fixed
- Managed MCP servers now use dotted `-c` overrides accepted by current Codex CLI versions.
- Cancellation, queued follow-ups, one-shot attachments, server restart, and stale process callbacks no longer race each other.
- Solution file search is asynchronous, cached, cancellable, bounded, and excludes generated directories and reparse points.
- Quoted `@file` mentions, paths containing spaces, drive-root containment, and dynamic Markdown fences are handled correctly.
- Mermaid preview navigation, CSP, messages, external links, and URL schemes are restricted to trusted values.
- Package, manifest, assembly, and app-server client versions now share one committed version source.
- Clearing prompt history no longer merges the deleted entries back from disk.
- Settings coordination now uses a deterministic cross-process mutex, while malformed-file preservation no longer mislabels transient I/O failures as corruption.
- PowerShell executable paths, command resolution timeouts, skill directory boundaries, and `.system` scope detection are handled correctly.
- Changing the extension language no longer changes the Visual Studio process culture.
- Rendered Mermaid diagrams are frozen after their first valid layout so inactive messages do not retain WebView2 instances.
- Extremely long conversations no longer hydrate their entire history automatically, and large tool/assistant streams are bounded only in the visual copy.
- WebView event dispatch and UI-thread transitions no longer rely on `async void` or unobserved dispatcher operations.
- The official WebView now receives the active Visual Studio document, active selection, and open tabs using its expected IDE-context contract, with an editor-view fallback while focus is in the chat.
- The requested model and reasoning effort are now supplied as host runtime metadata so the assistant can identify the selected model without exposing synthetic text as the user's message.
- Codex settings now open as an independent document-well tab, leaving chat accessible while changes persist immediately and invalidate every active WebView consumer.
- Recent task history now uses a bounded local-history popover in Visual Studio, avoiding the incompatible cloud-task component without requiring a ChatGPT web-session credential.
- VSIX verification now rejects credential files and private signing material before release.

## 1.2.1 - 2026-05-01

### Changed
- Updated the VSIX manifests shipped with the extension package.
- Refreshed the packaged extension metadata so the published release carries the new manifest content.

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
