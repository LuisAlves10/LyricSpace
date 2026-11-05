# LyricSpace

Preview nativo para composições tipográficas em espaço 3D no Windows. O Lenoz Motion Engine combina hierarquias de câmera e conteúdo, curvas Bézier assimétricas, contramovimento em profundidade e overshoot por velocidade. A primeira sequência é autoral, dura 8,6 s e roda com timestamps locais. [Visão](docs/VISION.md) · [Status](docs/STATUS.md)

## Executar no Windows

Windows 11 x64, GPU Direct3D 11 e SDK .NET 10 estável. Para executar um build pronto, use o .NET Desktop Runtime 10 x64. O Studio usa fontes instaladas pelo Windows.

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run --project src/LyricSpace.Studio -c Release --no-build
```

A janela começa no primeiro quadro e toca até o fim. Use **Pausar / Reproduzir**, **Reiniciar**, a barra de tempo e o seletor de velocidade. A caixa **Motion blur** alterna entre uma amostra e três amostras temporais. O status mostra FPS de submissão e os tempos de quadro, preparo na CPU e renderização na GPU.

Para repetir a microvariação com outra seed, passe `-- --seed 12345` ao fim do comando de execução. `-- --no-blur` inicia com uma amostra temporal.

## Validar

```powershell
dotnet test -c Release --no-build --filter "TestCategory!=LocalEvidence"
dotnet run --project src/LyricSpace.Studio -c Release --no-build -- --capture artifacts/frames
dotnet run --project src/LyricSpace.Studio -c Release --no-build -- --benchmark artifacts/benchmark.json
```

A captura de diagnóstico grava 720p a 30 FPS numa pasta nova. O benchmark abre uma janela sem bordas do tamanho do monitor primário, aquece por dois segundos e mede vinte segundos de preview. Consulte o JSON para confirmar resolução, adaptador e configuração. Esc encerra o benchmark.

Para validar as curvas com o acervo AE, aponte `LYRICSPACE_AE_EVIDENCE` para a pasta local que contém `ae_animation_audit.json`, `ae_animation_samples.json` e `animation_metrics.json` e rode `dotnet test -c Release --no-build --filter TestCategory=LocalEvidence`. O relatório e os dados originais ficam no acervo local.

## Projeto

- [Arquitetura](docs/ARCHITECTURE.md) e [especificação matemática](docs/LENOZ_MOTION_SPEC.md)
- [Estudo de upstream](docs/UPSTREAM_RESEARCH.md) e [avisos de terceiros](THIRD_PARTY_NOTICES.md)
- [Roadmap](docs/ROADMAP.md) e [status de validação](docs/STATUS.md)
