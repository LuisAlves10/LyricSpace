# Status

## Overlay

O executável começa pelo overlay DirectComposition. O Studio abre com `--studio`. O overlay compõe texto com alfa por pixel, sombra suave, motion blur e feather. A região padrão está no canto inferior direito e mede cerca de 806 × 453 px em 1080p. Para ajustar, segure Ctrl + Shift + Alt. Arraste por dentro da região para mover. Puxe um canto para redimensionar mantendo a proporção. Ao soltar, a moldura e a janela de edição somem, a cena retoma e a região é salva localmente.

O overlay apareceu sobre um navegador e sobre a janela independente de diagnóstico com fundo claro e escuro. Com sombra, o contorno dos glifos ficou perceptível na superfície clara. Em 138 amostras de z-order com 117 posições cada, o hit-testing do Windows retornou o aplicativo abaixo em todos os pontos. O overlay ficou fora do foco em todas as amostras. Os movimentos e clamps de todos os cantos foram testados sobre a lógica que o editor usa. O teste nativo da janela de edição confirmou hit-testing dentro da região, transparência de input fora, foco preservado e restauração ao fechar. O atalho físico e o gesto de mouse em conjunto ainda não foram exercitados por automação.

## Continuidade

O protótipo inicial já tinha hierarquia real e um mundo comum para os planos. As matrizes locais eram compostas com o pai e os rigs eram montados uma vez. A causa da rigidez estava nos intervalos. A troca de caminho da câmera por volta de 4,5 s ocorria entre o fim da primeira viagem em 3,88 s e o início da segunda em 5,04 s. Nesse platô, os grupos já tinham encerrado boa parte da chegada. Roll, FOV e wiggle ainda variavam, com pouca transferência de movimento entre gestos maiores.

O rig revisado combina aproximação, ponte, travessia, contramovimento e roll por tempos independentes. A ponte vai de 1,84 a 4,81 s, e a travessia começa em 4,42 s. A rotação secundária dos grupos entra antes de acabar a chegada e atravessa o começo da saída. A intensidade do wiggle foi reduzida. O último gesto ainda acomoda na palavra final.

O diagnóstico projetou os quatro cantos do quad de cada palavra principal a 1920 × 1080. Para medir o gesto coerente, usou uma diferença central com janela total de 1 s e selecionou palavras com centro no quadro e opacidade mínima de 0,5. Com limiar de 50 px/s mantido por pelo menos 0,30 s, o protótipo inicial apresentou intervalos em 1,02–1,47, 3,73–4,45 e 7,30–8,10 s. A versão revisada não apresentou intervalos sob esse mesmo critério no domínio medido. O limiar é um diagnóstico do protótipo. A janela central exclui o primeiro e o último meio segundo e pode cancelar uma trajetória que volte sobre si. A gravação contínua e o gráfico por palavra mostram o ataque, a travessia e a desaceleração.

## Equivalência com o After Effects

O replay lê os JSONs originais e avalia cinco nulos e a câmera de F212 a F429, sem mudar os tempos. O script em `tools/ae/ProbeCamera.jsx` mediu matrizes e três pontos de projeção em uma cópia descartável aberta no After Effects. As posições, roll e zoom da cópia conferiram com as amostras da auditoria. O SHA-256 do projeto original permaneceu idêntico após a medição. A comparação cobre 218 frames e seis matrizes de mundo por frame.

| Verificação | Resultado |
| --- | --- |
| Contramovimento em Z | Pico local de aproximadamente 398,44 em F278 e retorno a zero em F324 |
| Base das matrizes mundiais | Erro máximo de norma dos vetores de base de 0,0000172 |
| Origem das matrizes mundiais | Erro máximo de 0,0567 unidade |
| Velocidade da câmera | Pico em F401 em ambos, perto de 12.148 unidades/s. Erro máximo de 1,08 unidade/s |
| Aceleração da câmera | Pico em F380 em ambos, perto de 86.459 unidades/s². Erro máximo de 287 unidades/s² |
| Projeção em 1080 × 1080 | 254 amostras dentro do quadro. Erro máximo de 0,019 px |
| Canais locais e overshoot | 257 frames em sete canais de câmera. Seis sinais de overshoot conferidos com os valores pré/pós-expressão |

O replay usa planos de texto próprios para mostrar a projeção. As animações de objeto e efeitos internos do Element e a variação exata do wiggle entre frames ficam fora dessa equivalência. As bases de câmera, a projeção e o movimento dos planos de prova foram verificados nos frames originais. [Especificação matemática](LENOZ_MOTION_SPEC.md).

## Desempenho e artefatos

Medição do overlay em 1920 × 1080 com região de tela inteira, RX 570, i3-10100F, monitor de 75 Hz, fundo de outro app e motion blur de três amostras. Coleta de 20 s após aquecimento.

| Métrica | Resultado |
| --- | --- |
| Submissão DXGI | 1200 quadros · 59,95 FPS |
| Exibição por PresentMon | 59,86 FPS · 2 descartes em 1017 eventos |
| Render GPU | p95 de 2,15 ms |
| Intervalo de submissão | p95 de 17,10 ms |
| Intervalo de exibição | p95 de 26,71 ms |
| Memória local do processo reportada pelo DXGI | 123,83 MiB |

O intervalo de exibição varia com o monitor de 75 Hz. A medição representa o desktop, com um aplicativo abaixo. Jogos simultâneos, fullscreen exclusivo e múltiplos monitores ainda pedem testes.

- Build Release, restore bloqueado e 26 testes aprovados, incluindo comparação nativa do rig.
- Capturas contínuas da composição revisada e do replay a 60 FPS, com 517 e 218 frames. A gravação do overlay compacto foi capturada no desktop, sobre uma janela neutra, e entregue em 60 FPS. Os gráficos, os três vídeos e as métricas ficam em `artifacts/`.
- O relatório original está em `docs/internal/LENOZ_CAMERA_RELATORIO.md`, com SHA-256 preservado. Os 15 arquivos do manifesto de evidência foram verificados. O projeto do AE, a cópia, os JSONs e as capturas de desktop ficam locais.
