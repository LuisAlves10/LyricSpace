# LyricSpace

Lyric edit em um overlay transparente sobre o Windows. O Lenoz Motion Engine combina hierarquias 3D, curvas Bézier assimétricas, fases sobrepostas, contramovimento e overshoot por velocidade. O overlay acompanha a música publicada pelos controles de mídia do Windows e gera uma composição 3D por faixa a partir das letras sincronizadas do LRCLIB. [Visão](docs/VISION.md) · [Status](docs/STATUS.md)

## Executar no Windows

Windows 11 x64, GPU Direct3D 11 e SDK .NET 10 estável. Para um build pronto, use o .NET Desktop Runtime 10 x64. O app usa as fontes instaladas pelo Windows.

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run --project src/LyricSpace.Studio -c Release --no-build
```

Abra o Spotify desktop e toque uma música. O LyricSpace detecta a faixa automaticamente, busca a letra e acompanha a posição, a pausa e os saltos do player. Se a faixa estiver sem letra cronometrada, a região fica transparente. O ícone na bandeja do Windows indica o estado da sincronização e oferece **Sair do LyricSpace**.

O overlay começa menor no canto inferior direito. Segure **Ctrl + Shift + Alt** para mostrar a moldura de edição. Arraste por dentro para mover, ou puxe qualquer canto para redimensionar. A posição e o tamanho ficam salvos neste computador. A soltura do atalho restaura o click-through.

Para iniciar numa região explícita, passe `-- --region 40,560,720,405 --feather 54`. Os valores são X, Y, largura e altura em pixels a partir da origem do monitor primário. `--demo` exibe a cena autoral de 8,6 s. `--loop` repete a demo até encerrar o processo. `--no-blur` troca as três amostras temporais por uma.

O LRCLIB recebe título, artista, álbum e duração para localizar a gravação. O cache fica em `%LOCALAPPDATA%\LyricSpace\lyrics-cache`. `--seed 12345` permite repetir a composição da faixa com os mesmos dados. A disponibilidade e o encaixe da letra dependem da gravação no LRCLIB.

## Aparência e palavras

O baseline visual oficial é `visual-baseline-v2`, com aparência e animação em configurações independentes. A tag `visual-baseline-v1` preserva o primeiro perfil visual aprovado. O overlay usa por padrão o [perfil branco do teste aprovado](src/LyricSpace.Studio/white-estimated.json), com estimativa por palavra para letras que trazem tempos de linha. Para escolher a mesma aparência explicitamente, use `--look visual-baseline-v1`.

Abra **Aparência** no menu do ícone do LyricSpace na bandeja. Escolha **Marfim e âmbar**, **Branco com glow** ou **Azul monocromático**. **Editar arquivo personalizado** cria o arquivo com os valores da aparência atual e abre o editor. Após salvar, escolha **Aplicar arquivo personalizado** no mesmo menu.

O arquivo **%LOCALAPPDATA%\LyricSpace\appearance.json** controla cores, fontes e glow. Use `Base`, `Secondary` e `Accent` em `#RRGGBB`, fontes do Windows em `DisplayFont` e `BodyFont`, e `Glow`, `AmbientGlow` e `GlowRadius` para o halo.

Para ajustar movimento e sincronização, crie **%LOCALAPPDATA%\LyricSpace\animation.json** e reinicie o overlay. Os campos omitidos conservam os valores da baseline, compartilhados por todos os presets.

```json
{
  "EstimateWords": true,
  "Reveal": "some",
  "Motion": 1.08,
  "Tracking": 0.034,
  "ActiveTracking": 0.017
}
```

`Reveal` aceita `none`, `some` ou `all`. `Tracking` e `ActiveTracking` medem a expansão em ems. Se um perfil antigo inclui essas chaves, transfira as preferências de movimento que quiser manter para `animation.json`. O JSON original completo permanece versionado como referência do teste aprovado.

O destaque preciso usa os tempos internos de Enhanced LRC. Para aplicar uma letra local no overlay, salve o arquivo em **%LOCALAPPDATA%\LyricSpace\lyrics**, com nome no formato **Artista - Título.lrc**, seguindo os metadados da sessão do Windows. Caracteres inválidos de nome de arquivo viram sublinhado. A letra é lida na próxima carga da faixa.

Exemplo curto de Enhanced LRC com três palavras e o fim da última:

```lrc
[00:02.000]<00:02.000>Eu <00:02.250>volto <00:02.770>devagar<00:03.950>
```

Quando a fonte fornece apenas tempos de linha, o destaque por palavra usa a estimativa ativada por padrão. A bandeja identifica esse modo. O cálculo distribui o tempo da linha segundo o comprimento dos termos e a pontuação.

## Abrir o Studio

```powershell
dotnet run --project src/LyricSpace.Studio -c Release --no-build -- --studio
```

Para experimentar uma letra local no Studio, adicione --lrc caminho.lrc. Use --look ivory, --look white, --look ice ou --appearance caminho.json para selecionar a aparência. Esses parâmetros também acompanham --capture, --trace e --overlay-benchmark ao usar --lrc. A captura com uma região explícita gera PNGs com alpha. O trace com LRC mede o centro óptico, os limites e a folga de cada frase. As coordenadas da região no trace e na captura são relativas ao alvo de 1280 × 720.

Use **Pausar / Reproduzir**, **Reiniciar**, a barra de tempo e o seletor de velocidade. O status mostra FPS de submissão e tempos de quadro, CPU e GPU. Com `--seed 12345`, a microvariação é reproduzível.

## Validação

```powershell
dotnet test -c Release --no-build --filter "TestCategory!=LocalEvidence"
dotnet run --project src/LyricSpace.Studio -c Release --no-build -- --capture artifacts/frames
dotnet run --project src/LyricSpace.Studio -c Release --no-build -- --trace artifacts/motion.csv
dotnet run --project src/LyricSpace.Studio -c Release --no-build -- --overlay-benchmark artifacts/overlay.json
```

Para diagnóstico da integração, use `-- --media-log artifacts/playback.jsonl`. O log opcional contém metadados, estado, posição e índice da linha e deve ficar local.

A captura grava a composição a 720p e 60 FPS. O benchmark desenha 20 s após 2 s de aquecimento e informa resolução, adaptador e cadência de submissão. Para medir com toda a região em 1080p, passe também `--region 0,0,1920,1080`. O script `tools/plot-motion.py` lê os traces para gerar os gráficos.

Para reproduzir a câmera auditada, passe `--ae-reference <pasta-dos-JSONs>` com `--studio`, `--capture` ou `--trace`. Os testes de evidência leem `LYRICSPACE_AE_EVIDENCE` para localizar os JSONs originais e `LYRICSPACE_AE_NATIVE` para a medição nativa de matrizes e projeções. O [procedimento de referência](docs/LENOZ_MOTION_SPEC.md) descreve o replay e a coleta em uma cópia de projeto.

## Projeto

- [Arquitetura](docs/ARCHITECTURE.md) e [especificação matemática](docs/LENOZ_MOTION_SPEC.md)
- [Estudo de upstream](docs/UPSTREAM_RESEARCH.md) e [avisos de terceiros](THIRD_PARTY_NOTICES.md)
- [Roadmap](docs/ROADMAP.md) e [status de validação](docs/STATUS.md)
