# Lenoz Motion Engine

## Base observada

A auditoria local registra 17 composições, 131 layers, 34 nulos e 76.791 valores amostrados. As seções 5–7 detalham as contribuições da câmera. As seções 8–11 mostram cadeias de objeto, transformação global e respostas por velocidade. Os apêndices C–G trazem keys, handles, vínculos e amostragem para auditoria. O relatório integral fica em `docs/internal/` com seus links locais para os dados brutos.

A câmera principal usa a cadeia **travessia → giro e deslocamento → ponte → Z positivo → Z negativo → lente**. O giro da lente tem keys próprios, zoom independente, orientação com wiggle e ponto de interesse na fonte. A introdução tem um rig animado do conteúdo e uma base de câmera estática com wiggle. O Element 3D também separa o controle global de mundo e o rig que dirige um grupo. Essa composição de espaços é o dado artístico central.

## Tempo e curvas

O motor avalia a pose em segundos absolutos, independentemente da ordem de avaliação. O tempo de importação da auditoria precisa somar `displayStartTime` antes de converter para frames exibidos. Na composição de câmera auditada, o offset é −133 frames a 60 fps. O conjunto verificado contém keys em frames inteiros. Um importador futuro preservará keys fracionários, FPS por comp, início de exposição e fim exclusivo de layers.

Use duas coordenadas paramétricas para um segmento de duração `d = t₁ − t₀`. Seja `B(a,b,c,d;u)` o Bézier cúbico em base de Bernstein. `i₀` e `i₁` são influência de saída e entrada na faixa `(0, 1]`. `v₀` e `v₁` são velocidades **assinadas**, em unidades por segundo.

```text
x(u) = B(0, i₀, 1 − i₁, 1; u)
y(u) = B(y₀, y₀ + v₀ × d × i₀, y₁ − v₁ × d × i₁, y₁; u)
x(u) = (t − t₀) / d
dy/dt = y′(u) / (d × x′(u))
```

O motor resolve `u` com Newton limitado a um intervalo e bisseção como fallback. Influências altas podem cruzar os pontos de controle em X mantendo o tempo monótono. A influência de 100% permanece válida. A derivada no caso singular de dois handles de 100% pode ser infinita ou indefinida. Os valores dos keys e o clamp fora do intervalo permanecem definidos.

Dois lados lineares usam a reta. Em interpolações mistas, o lado linear usa influência `1/6` e a inclinação da secante. O zoom F276–317 confirma essa escolha com pico de 1529,067 px em F310 e velocidade de entrada negativa em F317. Uma influência de `1/3` no lado linear produz pico em F309 e erro RMS aproximado de 13,94 px nesse trecho. Os perfis de ataque e acomodação constam dos próprios handles. Mudanças intencionais de derivada, como a inversão em F276, permanecem expressivas. [Adobe, interpolação](https://helpx.adobe.com/after-effects/desktop/animate-in-after-effects/animation-keyframes/keyframe-interpolation.html).

## Espaço e rig

Os caminhos locais podem ser segmentos retos ou Béziers 3D com tangentes relativas aos endpoints. O progresso temporal representa fração de distância percorrida. A tabela de arco com 256 intervalos aproxima a distância do caminho curvo; o caminho reto de tangentes zero usa distância exata. Uma posição XYZ separada pode usar um canal escalar por eixo. Velocidade escalar espacial observada em uma reta pode ser projetada no componente com `v × (P₁ − P₀)/|P₁ − P₀|`. O material auditado da câmera tem tangentes zero nos canais espaciais comparados.

O LyricSpace usa vetores-linha com `+X` à direita, `+Y` para cima e câmera local olhando em `−Z`. Os canais Euler X/Y/Z são avaliados em graus acumulados. A rotação só vira matriz depois da amostragem, preservando voltas como `0 → −409°` na câmera e um canal de objeto com valor final `720°`. A ordem de composição da primeira fase é esta.

```text
local = T(−pivô) · escala · Rx · Ry · Rz · orientaçãoBase · T(posição)
mundo = local · mundoDoPai
view = inversa(mundoDaCâmera)
fovY = 2 × atan(alturaDeReferência / (2 × zoom))
clip = pontoLocal · mundo · view · projeção
```

A rotação local e a orientação de base são matrizes separadas. Um pai de rotação constante muda o espaço dos filhos. A topologia tem IDs únicos e exige pais anteriores aos filhos. Um importador deverá verificar a ordem exata dos eixos e a mudança de base do AE em fixtures de mundo, além de implementar o ponto de interesse de uma câmera de dois nós. A projeção por Zoom e o deslocamento de câmera têm efeitos distintos sobre distância relativa e parallax. [Adobe, câmeras](https://helpx.adobe.com/after-effects/desktop/work-with-layers/camera-layer/cameras-lights-points-interest.html).

## Fases do movimento

Os offsets internos de profundidade começam em +519 e −519. O negativo vai a zero de F246 a F286 com influências de 70,1668% e 49,6177%. O positivo começa em F261 e vai a zero em F324 com 53,5207% e 79,7038%. A soma local sai de zero, passa pelo máximo amostrado de 398,444 em F278 e volta a zero. Nesse par sem rotação ou escala os termos podem ser somados. A posição mundial final inclui os outros pais.

As velocidades centrais por frame atingem picos sucessivos em F270, F285, F318, F378, F381 e F401, referentes ao primeiro offset, recuperação, ponte, giro/deslocamento, roll e travessia. Em F285 a ponte começa antes do fim dos offsets. A posição e o giro próprios da lente continuam durante o último estágio. O motor permite fases sobrepostas por meio de canais independentes avaliados no mesmo instante. A transferência é uma decisão de composição. Uma métrica futura pode acompanhar a velocidade da posição projetada e dos cantos de cada palavra para identificar pausas indesejadas ou rastros longos nos intervalos de leitura.

A demo usa o mesmo par de handles com amplitude de 1,55 unidade e tempos escalados por 1,24 a partir de 1,93 s. A excursão estimada do par local é de aproximadamente 1,19 unidade, perto de 2,59 s. A ponte já começou nesse momento e o grupo anterior está saindo. Na segunda travessia a câmera atravessa profundidades e completa uma volta enquanto as faixas espaciais e a palavra seguinte já aparecem. Antes e depois das passagens, os grupos têm tempo de leitura. Posições, acentos e tamanhos são autorais e a seed altera a microvariação da câmera.

## Overshoot e wiggle

A contribuição observada de overshoot amortece senoidalmente a componente assinada da velocidade pouco antes da chegada. Na implementação de um gesto terminal, `ε = min(0,1 / fps, d/2)` evita que segmentos curtos amostrem o gesto anterior.

```text
τ = t − tempoDoÚltimoKey
Δ(t) = ganhoEmSegundos × v(tempoDoÚltimoKey − ε)
       × sin(2π × frequênciaHz × τ) × exp(−decaimento × τ), se τ > 0
resultado = valorBase + Δ(t)
```

Sem trecho de chegada, a contribuição é zero. Ganho 0,05 corresponde ao controle de amplitude 50 no material auditado. Frequência 2 Hz e decaimento 3 ou 4 por segundo reproduzem os valores desse material. O canal de escala 1,82 ganha aproximadamente +0,6962 e depois −0,2561. O canal de escala 1,00 ganha +0,5284 e depois −0,2496. Os canais adicionais confirmam que o sinal muda junto com a velocidade. A função atual atua após o key terminal de cada gesto. Aplicação entre vários keys e a variante Floor ficam para uma futura extensão com amostras próprias.

O wiggle do AE foi observado na orientação, com seed 12345, frequência 2 e amplitude 2 nas unidades do canal. A microvariação do LyricSpace usa hash por seed e canal em pontos de tempo e interpolação quíntica entre eles. A mesma seed e o mesmo tempo retornam o mesmo valor após seek. Essa é uma escolha do LyricSpace. O ruído interno do AE não foi reconstruído. [Adobe, expressões](https://helpx.adobe.com/after-effects/desktop/work-with-expressions/expression-language-reference/expression-language-reference.html).

## Acabamento e limites da evidência

Os dois pares do plugin auditado têm shutter interno 180° e seis amostras. O preview integra três amostras numa exposição equivalente de meio quadro. Usa máscaras de glifos com textura plana, mistura premultiplicada, fundo com gradiente e grão e elementos de profundidade. A comparação matemática cobre os canais locais selecionados e o overshoot antes e depois da expressão. O pipeline e a cena do AE têm peças adicionais.

A fonte 3D aberta e o pré-render da composição principal divergem em acabamento e elementos visíveis. A auditoria registrou vínculos de expressão ausentes e valores opacos do Element. Os dados comprovam trajetórias locais, hierarquias, tempos, curvas e contribuições medidas. Matrizes mundiais subframe a subframe, paridade de efeitos e diagnóstico dos vínculos ausentes dependem de evidência adicional. [Medições e reprodução](STATUS.md).
