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

O LRCLIB recebe título, artista, álbum e duração para localizar a gravação. O cache fica em `%LOCALAPPDATA%\LyricSpace\lyrics-cache`. Cada faixa usa timestamps por linha. `--seed 12345` permite repetir a composição da faixa com os mesmos dados. A disponibilidade e o encaixe da letra dependem da gravação no LRCLIB.

## Abrir o Studio

```powershell
dotnet run --project src/LyricSpace.Studio -c Release --no-build -- --studio
```

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
