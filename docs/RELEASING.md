# Guia de release

Fluxo completo para publicar uma nova versão do DualAudioMirror. Versionamento semver com a versão única em `Directory.Build.props`.

## 1. Garantir CI verde em master

Confirme que o workflow `ci.yml` está verde no commit mais recente de `master` antes de qualquer coisa:

```pwsh
git checkout master
git pull origin master
```

## 2. Atualizar o CHANGELOG

Em `CHANGELOG.md`:

- mova os itens da seção `[Não lançado]` para uma nova seção `## [X.Y.Z] - AAAA-MM-DD` com a data do lançamento;
- deixe a seção `[Não lançado]` com o placeholder `Adicione mudanças aqui.`;
- classifique cada item em `Adicionado`, `Alterado` ou `Corrigido`;
- adicione o link de comparação da nova versão no rodapé:
  `[X.Y.Z]: https://github.com/brunocsilva41/DualAudioMirror/releases/tag/vX.Y.Z`
  e atualize o link `[Não lançado]` para `compare/vX.Y.Z...HEAD`.

## 3. Bump da versão

Edite `Directory.Build.props` e atualize os quatro campos, seguindo semver (major/minor/patch):

- `Version`
- `AssemblyVersion`
- `FileVersion`
- `InformationalVersion`

Exemplo para `1.1.0`:

```xml
<Version>1.1.0</Version>
<AssemblyVersion>1.1.0.0</AssemblyVersion>
<FileVersion>1.1.0.0</FileVersion>
<InformationalVersion>1.1.0</InformationalVersion>
```

## 4. Commit de release

```pwsh
git add Directory.Build.props CHANGELOG.md
git commit -m "chore: release vX.Y.Z"
git push origin master
```

## 5. Tag anotada

```pwsh
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin master vX.Y.Z
```

## 6. Publicação automática

O workflow `release.yml` dispara na tag `v*`, roda o empacotamento (como o `package.yml`) e publica a GitHub Release com:

- `DualAudioMirror-Setup-X.Y.Z.exe`
- `DualAudioMirror-X.Y.Z-portable.zip`
- `SHA256SUMS.txt`
- notas de versão extraídas do `CHANGELOG.md`

Acompanhe a execução em **Actions** e confirme que os assets foram anexados.

## 7. Checklist pós-release

- Baixe os assets publicados e valide os hashes com `SHA256SUMS.txt`.
- Teste a instalação em uma máquina (ou usuário) limpa, incluindo as opções de escopo e tarefas.
- Valide a atualização automática: publique uma versão seguinte maior e confira se o diálogo aparece com as quatro opções no app já instalado.
- Confira os badges do README (CI, Release) e a data/seção do `CHANGELOG.md`.
- Se a mudança for relevante, atualize `docs/` e a página de Releases.

## O que não fazer

- Não edite uma release já publicada trocando os assets por arquivos diferentes sem avisar os usuários — prefira publicar uma nova versão corretiva.
- Não faça force-push em `master` depois do release.
- Não mova nem recrie tags já publicadas; qualquer correção vira `vX.Y.Z+1`.
- Não publique a tag com a CI vermelha ou com o `CHANGELOG.md` desatualizado.
