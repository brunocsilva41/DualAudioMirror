# Changelog

Todas as mudanças notáveis neste projeto serão documentadas neste arquivo.

O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/),
e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [Não lançado]

- Adicione mudanças aqui.

## [1.0.1] - 2026-10-01

### Corrigido

- Lista "Também tocar o som em" aparecia vazia: o template do `ListBox` no tema não tinha `ItemsPresenter`, então os dispositivos nunca eram desenhados.
- Lista de dispositivos espremida pela altura fixa da janela: a janela agora é redimensionável, a lista tem altura mínima e a altura respeita a área útil da tela.
- Caixa de delay cortava o número digitado.
- Aviso "Nenhum dispositivo de saída encontrado" não é mais sobrescrito pelo aviso do VB-Cable.
- Card de estatísticas aparecia vazio.
- Instalador: a pasta `Program Files\VB\CABLE` que sobra após desinstalar o VB-Cable era tomada como VB-Cable instalado e a instalação automática era pulada. A detecção agora exige o endpoint "CABLE Input" ativo.

## [1.0.0] - 2026-09-30

### Adicionado

- Espelhamento de áudio: captura o que o Windows toca e reproduz em dois ou mais dispositivos de saída ao mesmo tempo (modo espelho).
- Modo sincronizado: o cabo virtual VB-Cable vira a fonte e todos os aparelhos tocam juntos, com detecção automática do VB-Cable.
- Delay ajustável por dispositivo (0–400 ms) no modo sincronizado, com persistência entre sessões.
- Botão "Testar som" com tom de ~4 segundos para validar a saída antes de iniciar.
- Diagnóstico ao vivo na janela (KB/s capturados, buffer, underruns, overflows, correções de drift) e log completo em `%LocalAppData%\DualAudioMirror\DualAudioMirror.log`.
- Temas Dark e Light.
- Persistência de configurações em `%LocalAppData%\DualAudioMirror\settings.json` (tema, atualizações, dispositivo principal, modo e alvos).
- Definição e restauração do dispositivo padrão do Windows a partir do app.
- Instalador Inno Setup personalizado em PT-BR: escopo de instalação à escolha (só eu ou todos os usuários), tarefas de atalho na área de trabalho e "iniciar com o Windows", e página de dependências que detecta o VB-Cable e pode baixar/instalá-lo automaticamente.
- Pacote ZIP portátil (`DualAudioMirror-<versão>-portable.zip`).
- Arquivos de checksum SHA256 (`SHA256SUMS.txt`) para os artefatos publicados.
- Atualização automática via GitHub Releases: ao abrir o app consulta a API pública (com cerca de 4 s) e oferece "Atualizar agora" (download, validação de SHA256 e execução do instalador), "Baixar e instalar depois", "Mais tarde" e "Ignorar esta versão".
- CI/CD com GitHub Actions: `ci.yml` (build/teste), `package.yml` (publicação de binário, instalador, ZIP e checksums) e `release.yml` (GitHub Release automática a partir da tag `v*`).
- Documentação (README, guia de release) e templates de contribuição, de issue e de segurança.
- Ícone do aplicativo e licença MIT.

### Alterado

- A checagem de atualizações acontece na abertura do app, sem bloquear a interface.
- O modo sincronizado é o padrão quando o VB-Cable está instalado.

### Corrigido

- Saídas que param no meio da reprodução são reabertas automaticamente (até 3 tentativas) antes de reportar erro.
- Deriva entre dispositivos é corrigida periodicamente pelo ajuste do buffer de cada saída.
- Delay acima do limite é limitado a 400 ms para evitar buffers inválidos.

[Não lançado]: https://github.com/brunocsilva41/DualAudioMirror/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/brunocsilva41/DualAudioMirror/releases/tag/v1.0.1
[1.0.0]: https://github.com/brunocsilva41/DualAudioMirror/releases/tag/v1.0.0
