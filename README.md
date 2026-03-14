# Codex for Visual Studio 2026

Extensão VSIX para Visual Studio 2026 com painel lateral para integração com o Codex CLI.

## Recursos incluídos

- Tool Window lateral no Visual Studio 2026
- Campo de prompt e saída
- Seleção de modelo
- Seleção de reasoning/thinking effort (`minimal`, `low`, `medium`, `high`, `xhigh`)
- Seleção de verbosity (`low`, `medium`, `high`)
- Seleção de `approval_policy` e `sandbox_mode`
- Persistência local de configurações
- Botão para usar a pasta da solução aberta como working directory
- Botão para abrir ou editar `~/.codex/config.toml`
- Histórico local de prompts
- Anexo de imagem por clipboard ou seletor de arquivo
- Suporte a `--image` ao chamar o Codex CLI
- Busca de arquivos da solução enquanto o usuário digita `@arquivo` no final do prompt

## Observações

- O empacotamento da VSIX no CI usa `MSBuild` completo e habilita a geração do pacote com a propriedade `BuildVsixPackage=true`.
- A busca com `@` foi implementada na UI da extensão; ela sugere arquivos da solução e insere o caminho relativo no prompt.
- O suporte a imagem depende de o `codex` instalado aceitar `--image`.
