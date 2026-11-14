# Visão do LyricSpace

LyricSpace transforma músicas tocadas no Windows em composições tipográficas animadas em tempo real. Palavras e grupos ficam em planos dentro de uma cena XYZ, com parallax, perspectiva, pivôs, câmera e oclusão por profundidade. A intenção é conseguir o ritmo e o desenho espacial de um lyric edit de motion design.

A composição privilegia a palavra que precisa ser lida. Textos auxiliares têm tempo, escala, cor e profundidade próprios. Travessias agressivas podem reduzir a leitura por alguns instantes, seguidas de apresentação acomodada. Um gesto pode ganhar velocidade enquanto outro desacelera. O [Lenoz Motion Engine](LENOZ_MOTION_SPEC.md) concentra essa linguagem.

## Experiência pretendida

- O Studio permite observar a composição, navegar pelo tempo e comparar configurações. A geração procedural usa presets, regras de hierarquia tipográfica, timing musical e uma seed reproduzível.
- O overlay é a experiência principal. Usa transparência por pixel, mantém o foco no aplicativo abaixo e abre numa região compacta no canto inferior direito. O usuário pode ajustar a região ao segurar o atalho de edição. O Studio fica separado como preview.
- O relógio segue as sessões de mídia do Windows. Uma faixa pode receber tempos por linha ou palavra. O compositor combina a letra local ou obtida de um provedor com sinais musicais, quando disponíveis.
- Presets autorais poderão ser editados, exportados e alimentados por dados do After Effects. Exportação de vídeo será uma saída separada para a mesma cena e o mesmo tempo.
- Jogos em janela e borderless são candidatos a testes. Roblox e Bloxstrap podem ser avaliados futuramente por vias externas e oficialmente suportadas, com o cliente separado do overlay.

## Alvo inicial

Windows 11 x64, Intel Core i3-10100F, Radeon RX 570 de 4 GB, 16 GB de RAM e 1920 × 1080 a 75 Hz. A meta de render do overlay é 60 FPS em 1080p. O objetivo de coexistir com jogos depende de medições de custo de GPU, VRAM e cadência sob carga. [STATUS](STATUS.md) registra o que foi medido.
