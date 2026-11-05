# Status

## Fundação do Studio

A primeira entrega desenha uma sequência autoral de 8,6 s com texto plano, perspectiva, rigs parentados, curvas temporais e espaciais independentes, contramovimento, overshoot por velocidade e motion blur de três amostras. O Studio roda com timestamps locais. A direção de movimento e a apresentação serão revisadas na próxima etapa.

- Build Release com restauração bloqueada e 18 testes aprovados, incluindo duas comparações com os JSONs do After Effects. Os 257 frames de sete canais de câmera preservaram a ordem dos picos de velocidade. O maior erro de posição local foi 0,055 unidade na ponte #32. Os seis canais de overshoot tiveram erro máximo de contribuição de aproximadamente `2,8e−12` unidades.
- Captura contínua de diagnóstico em 1280 × 720 a 30 FPS, 259 frames. A inspeção levou a ajustes na fonte, na legibilidade do grupo intermediário e no encadeamento visual da segunda travessia. A janela nativa abriu e apresentou a sequência e as métricas. O teste interativo dos controles foi interrompido pelo Esc.
- O relatório foi movido para `docs/internal/LENOZ_CAMERA_RELATORIO.md` com SHA-256 preservado. Os 15 arquivos de evidência conferem com o manifesto local. As comparações abriram os dados em modo leitura.

## Desempenho do primeiro preview

i3-10100F, RX 570 e monitor de 75 Hz. Janela de 1920 × 1080, exposição de 180°, três amostras e 20 s de medição após 2 s de aquecimento, sem jogo em execução.

| Métrica | Resultado |
| --- | --- |
| Quadros submetidos | 1195 · 59,75 FPS |
| Quadros exibidos medidos por PresentMon | 59,85 FPS em 17 s, com 2 descartes em 1016 eventos |
| Intervalo de submissão | média 16,74 ms · p95 17,11 ms |
| Intervalo de exibição | média 16,71 ms · p95 26,71 ms |
| CPU de preparação | p95 0,29 ms |
| GPU de renderização | p95 3,06 ms |
| Uso local do processo reportado pelo DXGI | 123,58 MiB |

O intervalo efetivo de exibição reflete a cadência do monitor. A taxa média não implica apresentação regular de um quadro novo a cada 16,67 ms. O benchmark e a CSV de apresentação ficam em `artifacts/`. [Referência de medição](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md).

## Limites

O preview inicial usa fundo opaco e uma única sequência autoral. Mídia do sistema, provedores, composição de textos arbitrários, overlay transparente e interoperabilidade completa com o AE ainda estão em desenvolvimento. [Roadmap](ROADMAP.md).
