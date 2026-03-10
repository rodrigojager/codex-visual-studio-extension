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

## Releases no GitHub

O repositório está preparado para publicar releases com GitHub Actions.

1. Crie uma tag no formato `vX.Y.Z`, por exemplo `v1.0.0`.
2. Faça push da tag para o GitHub.
3. O workflow `.github/workflows/release.yml` atualiza a versão da extensão, compila a VSIX em `Release` e publica o arquivo no GitHub Releases.

Também é possível disparar o workflow manualmente em `Actions > Release`, informando a versão no formato `X.Y.Z`.

## Observações

- O empacotamento da VSIX no CI usa `MSBuild` completo e habilita a geração do pacote com a propriedade `BuildVsixPackage=true`.
- A busca com `@` foi implementada na UI da extensão; ela sugere arquivos da solução e insere o caminho relativo no prompt.
- O suporte a imagem depende de o `codex` instalado aceitar `--image`.
