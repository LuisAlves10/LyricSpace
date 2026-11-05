# Roadmap

| Etapa | Entrega e critério de saída |
| --- | --- |
| 0 · Fundação | Studio executável, sequência autoral local, testes de curvas e rigs, comparação numérica com o AE e benchmark em 1080p. |
| 1 · Letras e relógio local | LRC local com tags, offsets e busca de cue. GSMTC com seleção de sessão, tratamento de pausa, seek, metadados incompletos e perda de sessão. Validar com mídia conhecida e medir deriva em sessões longas. |
| 2 · Linguagem procedural | Layout com métrica real de glifos, ênfase, grupos, margens seguras e adaptação à duração. Catálogo pequeno de movimentos parametrizáveis e seed. Aceitação em letras curtas e longas com leitura antes e depois de travessias. |
| 3 · Overlay | Janela transparente DirectComposition, click-through, hotkey configurável e recorte por monitor. Validar fullscreen borderless, DPI misto, recuperação do dispositivo e custo da GPU em jogo compatível. |
| 4 · Fontes remotas e análise de áudio | LRCLIB, cache por faixa, offsets salvos e comportamento quando a letra está indisponível. Testar GSMTC com Spotify, Apple Music e YouTube Music. Linha como fallback de tempo e palavra com timestamps válidos. Analisar transientes, batidas e intensidade por WASAPI com controle do usuário. |
| 5 · Ferramentas e exportação | Presets versionados, inspeção de curvas e pivôs no Studio, AE Bridge declarativo validado por fixtures e exportação determinística de vídeo em ritmo definido. |
| 6 · Integrações opcionais | Avaliar Roblox e Bloxstrap com documentação oficial vigente. Limitar a overlay externo e APIs oficialmente suportadas. Injeção no cliente e interferência no anticheat ficam fora do projeto. |

A etapa atual e as checagens executadas constam de [STATUS](STATUS.md).
