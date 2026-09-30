# Contribuindo com o DualAudioMirror

Obrigado por seu interesse em contribuir! Este documento explica como reportar bugs, propor funcionalidades e enviar alterações para o projeto.

## Como reportar um bug

1. Pesquise as [issues existentes](https://github.com/brunocsilva41/DualAudioMirror/issues) antes de abrir uma nova.
2. Abra uma issue usando o template **Reportar bug** ([nova issue](https://github.com/brunocsilva41/DualAudioMirror/issues/new/choose)).
3. Preencha versão, passos para reproduzir, comportamento esperado e atual, e anexe o log em `%LocalAppData%\DualAudioMirror\DualAudioMirror.log`.

## Como propor uma feature

1. Abra uma issue usando o template **Pedir feature** ([nova issue](https://github.com/brunocsilva41/DualAudioMirror/issues/new/choose)).
2. Descreva o problema, a solução proposta e a área envolvida (Interface, Motor de áudio, Instalador, Atualizações ou Outro).
3. Para mudanças grandes, discuta a proposta na issue antes de abrir o PR.

## Fluxo de contribuição

1. Faça um fork do repositório.
2. Crie uma branch a partir de `master`:

   ```pwsh
   git checkout -b feature/minha-melhoria master
   ```

3. Faça suas alterações em commits atômicos.
4. Atualize o `CHANGELOG.md` (se a mudança for relevante) e a documentação.
5. Abra um Pull Request para a branch `master`.
6. Aguarde a revisão (ver Code Owners abaixo).

Sugestão de prefixos de branch: `feature/`, `fix/`, `docs/`, `chore/`, `ci/`.

## Conventional Commits

Formato: `tipo(escopo)!: descrição curta`

Tipos aceitos:

| Tipo    | Uso                                              |
| ------- | ------------------------------------------------ |
| `feat`  | nova funcionalidade                              |
| `fix`   | correção de bug                                  |
| `docs`  | apenas documentação                              |
| `chore` | manutenção, dependências, formatação             |
| `ci`    | mudanças em CI/CD, workflows e scripts           |
| `test`  | testes                                           |

- O escopo é opcional: `feat(audio): espelha a saída selecionada`.
- `!` antes dos parênteses indica breaking change, com `BREAKING CHANGE:` no corpo do commit.

Exemplos:

```
feat(audio): seleção automática de saída
fix(installer): corrige atalho da Área de Trabalho
docs: atualiza instruções de instalação
chore: atualiza dependências NuGet
ci: adiciona cache do NuGet no build
feat(settings)!: remove configurações legadas
```

### Commits atômicos

Cada commit deve conter **uma mudança lógica**. Não misture correção de bug, refatoração e formatação no mesmo commit — divida em commits separados.

## Build local

```pwsh
pwsh scripts/build.ps1
```

Parâmetros: `-Configuration` (padrão `Release`), `-Runtime` (padrão `win-x64`), `-Output` (padrão `publish/win-x64`). O build é self-contained.

## Empacotar

```pwsh
pwsh scripts/package.ps1
```

Requer o Inno Setup 6:

```pwsh
winget install JRSoftware.InnoSetup
```

O script gera em `artifacts/`:

- `DualAudioMirror-Setup-<ver>.exe`
- `DualAudioMirror-<ver>-portable.zip`
- `SHA256SUMS.txt`

## CHANGELOG

Toda PR relevante deve atualizar a seção `Unreleased` do `CHANGELOG.md`, seguindo o padrão [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Commits de manutenção interna sem efeito para o usuário não precisam de entrada.

## Code of Conduct

Ao participar, você concorda com o [Código de Conduta](CODE_OF_CONDUCT.md).

## Segurança

Não abra issues públicas para vulnerabilidades. Siga o [SECURITY.md](SECURITY.md).

## Code Owners e revisão

`.github/CODEOWNERS` define [@brunocsilva41](https://github.com/brunocsilva41) como revisor de todas as alterações do repositório. Toda PR precisa de aprovação antes do merge.
