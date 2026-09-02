# civilization

[English](README.md) | Español

Una simulación histórica determinista y sin interfaz gráfica, con una interfaz de terminal.

Este proyecto es un **esqueleto arquitectónico con tres sistemas deliberadamente simples**. La
población crece, los gobernantes envejecen y son sucedidos, los estados conquistan territorios
adyacentes cuando son mucho más fuertes a nivel local, los territorios que ya no pueden gobernar
se independizan como nuevos estados y los estados que pierden todo su territorio quedan retirados.
Todavía no hay modelos de economía, ejércitos, tecnología, religión, cultura, dinastías ni facciones.
El objetivo de esta etapa es permitir que esos sistemas se puedan añadir más adelante de forma
económica y segura.

## Estructura

| Proyecto | Función |
| --- | --- |
| `src/Civ.Engine` | Estado, identificadores, RNG, efectos, eventos, canalización, invariantes, generación del mundo y persistencia. Sin E/S. |
| `src/Civ.Systems` | Contenido de la simulación: población, sucesión, cohesión/secesión, expansión y ciclo de vida de los estados. Hace referencia al motor, pero **no puede modificar el estado**. |
| `src/Civ.Terminal` | Observador interactivo. |
| `src/Civ.Batch` | Ejecutor sin interfaz para múltiples semillas. |
| `tests/Civ.Engine.Tests` | Determinismo, invariantes, seguridad de referencias, semántica de efectos, ciclo de vida de los estados y persistencia. |

## Ejecución

```bash
dotnet test
```

```bash
dotnet run --project src/Civ.Batch -c Release -- --seeds 24 --years 2000 --width 16 --height 12 --polities 14 --parallel --verify
```

El ejecutor por lotes acepta como argumentos las constantes de ambos conjuntos de reglas: expansión
(`--min-pressure`, `--reach`, `--mobilisation`, `--overextension`, `--defence`, `--strain`,
`--shock`, `--pressure-scale`, `--max-permille`) y cohesión (`--capacity`, `--distance-strain`,
`--disconnection`, `--size-strain`, `--secession-permille`, `--min-breakaway`). Así, un barrido de
ajuste nunca requiere recompilar.

```bash
dotnet run --project src/Civ.Terminal -c Release -- --seed 1815 --width 8 --height 5 --polities 5
```

## Reglas que este esqueleto debe garantizar

**Los sistemas no pueden escribir en el estado.** Todos los métodos que modifican `WorldState`,
`Region`, `Polity` y `EntityTable` son `internal` para `Civ.Engine`. Los sistemas viven en un
ensamblado separado, por lo que intentar hacerlo genera un error de compilación en lugar de depender
de una convención de revisión de código. Un sistema lee una instantánea y emite efectos;
`EffectApplier` es el único código del repositorio que modifica el estado.

**Los efectos se aplican en las barreras de fase.** Un año ejecuta
`Environment → Population → Economy → Culture → Rulership → Polity → Diplomacy → Bookkeeping`.
Todos los sistemas de una fase ven el mismo estado al inicio de esa fase y sus efectos se aplican
juntos al final. Por eso, los sistemas de una misma fase no pueden ver el trabajo de los demás, lo
que permite reordenarlos ahora y paralelizarlos más adelante; el orden entre grupos es declarado y
no accidental.

**Se prefieren los cambios incrementales conmutativos.** `AdjustRegionPopulation(delta)` se combina
con otros sistemas sin conflicto. Las escrituras absolutas (`SetRegionController`) se resuelven
según el orden de la canalización y la colisión queda registrada como `EffectConflict`, en vez de
descartarse silenciosamente.

**Los eventos solo provienen del aplicador.** Se emite un `SimEvent` cuando el estado cambió de
verdad, de modo que la crónica sea un informe de la simulación y no una historia narrada en paralelo.
`RecordEvent` es la única excepción permitida para observaciones sin cambios de estado. Los eventos
guardan una copia de los nombres que necesitan, porque una entrada del año 400 debe seguir
mostrándose correctamente en el año 1800.

**La relevancia está en el modelo, no en la interfaz.** Cada evento tiene una categoría de
importancia. La población cambia en todas las regiones cada año; solo se informan los cruces de
hitos. Sin esto, el registro se convertiría en una pared de texto donde nada se distingue.

**Los flujos aleatorios se derivan de los nombres de los sistemas.**
`hash(seed, streamId, year, entityIndex)` se construye en cada llamada y nunca se arrastra hacia
adelante. Añadir, eliminar o reordenar un sistema no puede desplazar las tiradas de ningún otro; esa
propiedad evita que cada función nueva invalide el trabajo de balance. La prueba
`DeterminismTests.AddingSystemsDoesNotChangeExistingSystemsOutcomes` lo verifica.

**Una partida es `(versión del motor, configuración, semilla)`.** No se usa la hora del sistema,
`Guid.NewGuid`, hashes de cadenas del framework, iteración dependiente del orden de diccionarios ni
números de punto flotante en el estado. `WorldHasher` produce un hash canónico exacto y el ejecutor
por lotes repite cada semilla para compararlo.

**Los estados y gobernantes nunca se eliminan.** Los estados disueltos y los gobernantes muertos se
marcan y se conservan como registros históricos. `EntityTable` permite eliminar con incremento de
generación porque los personajes y ejércitos lo necesitarán, y demostrar ahora la detección de
referencias obsoletas es mucho más barato que incorporarla después.

**Los invariantes se comprueban en cada turno de las pruebas.** La corrupción estructural del ciclo
de vida de los estados es acumulativa y silenciosa; una sola región huérfana en el año 200 puede ser
invisible hasta que otro sistema intente leerla mucho más tarde.

## Limitaciones conocidas

- Un sistema que emite `FoundPolity` no recibe el nuevo identificador durante ese turno. El aplicador
  lo asigna y lo incluye en el evento; cualquier acción sobre el nuevo estado debe esperar al turno
  siguiente.
- Los sistemas no deben conservar estado entre turnos. Todo lo persistente pertenece a `WorldState`,
  donde puede incluirse en el hash, guardarse y comprobarse.
- Las partidas guardadas solo contienen el estado del mundo. La historia se reproduce repitiendo la
  simulación y no se guarda.
- `EntityTable.RestoreSlot` reconstruye la lista de espacios libres por índice ascendente en lugar
  del orden LIFO original. Esto no importa mientras las regiones y los estados nunca se eliminen;
  debe revisarse antes de guardar cualquier tipo de entidad que sí se elimine.

## El sistema de expansión

`OpportunisticExpansionSystem` se ejecuta en la fase `Diplomacy`. Cada estado puntúa todas las
regiones extranjeras o sin dueño de su frontera y, si la mejor puntuación supera un umbral, realiza
como máximo un intento por año contra ese objetivo. **No** es un modelo de guerra: no hay ejércitos,
frentes, bajas ni tratados de paz, y será reemplazado. Existe para demostrar que la canalización
predeterminada puede modificar fronteras políticas de forma segura durante milenios.

La presión es el ataque dividido por la defensa, expresado como porcentaje:

- **ataque** = población propia adyacente al objetivo, más una parte de la población total del
  estado, ajustada por estabilidad y penalizada por la distancia a la capital y por la cantidad de
  territorio que ya controla.
- **defensa** = población del territorio objetivo y, si tiene dueño, defensa organizada ajustada por
  la estabilidad de ese estado más la fuerza que puede proyectar según la distancia desde su capital.

Por debajo del umbral no ocurre nada, aunque pasen siglos: no existe una probabilidad plana de
conquista por año. Por encima, el margen determina la probabilidad anual, así que el azar decide
*cuándo* ocurre, nunca *si* es posible.

### Lo que mostraron realmente los barridos

Hubo que corregir cuatro problemas antes de que el mundo pudiera cambiar, y todos se descubrieron
mediante ejecuciones por lotes:

1. **Una tensión de conquista plana congelaba todo.** Un coste fijo de estabilidad dejaba a cada
   conquistador temporalmente lo bastante débil como para que la víctima recuperara de inmediato la
   región. Ahora la tensión es proporcional a lo marginal que fue la conquista, por lo que absorber
   a un vecino débil casi no tiene coste.
2. **Una defensa puramente local impedía que la debilidad se acumulara.** Si la defensa dependía solo
   de la región objetivo, perder territorio nunca facilitaba acabar con el estado y ninguno podía
   desaparecer.
3. **Una penalización de alcance fuerte estabiliza mucho.** Un estado que se reduce se concentra
   alrededor de su capital, mientras uno que crece se extiende lejos de la suya. Con el valor
   original, la penalización por paso dominaba todos los demás términos y congelaba el mapa durante
   dos mil años.
4. **El mundo era el verdadero problema.** La fertilidad se elegía de forma independiente para cada
   región, haciendo que todas las partes del mapa y todos los estados fueran estadísticamente
   iguales. Ninguna regla de conquista puede amplificar una asimetría inexistente. `WorldGenerator`
   ahora genera un campo de riqueza de baja frecuencia, de modo que algunos estados poseen núcleos
   fértiles y otros territorios marginales.

Antes de esos cambios, una partida de 2000 años producía unas 380 «conquistas» que eran tres celdas
fronterizas cambiando de dueño repetidamente, mientras el resto del mapa permanecía idéntico.

### Comportamiento aislado

Antes de incorporar la cohesión, 24 semillas × 2000 años produjeron 1752 expansiones y 114
extinciones, sin violaciones de invariantes. Sin embargo, la cantidad de estados solo podía bajar,
porque la conquista era la única fuerza sobre el grafo político y nada creaba estados nuevos.

## El sistema de cohesión y secesión

`CohesionSecessionSystem` se ejecuta en la fase `Polity`, antes de la expansión, y actúa como
contrapeso. Cada región controlada genera **tensión** y el estado responde con un presupuesto de
**autoridad**:

- **tensión** = distancia a la capital *medida a través del territorio del propio estado*, o una
  penalización fija alta si no existe una ruta, más un término por cada región adicional y otro
  término limitado cuando la región es más rica que la provincia promedio.
- **autoridad** = capacidad administrativa base, ajustada por estabilidad.

Cuando la tensión supera la autoridad, una región queda descontenta. Las regiones descontentas se
agrupan en **componentes conectados**, y la más grande se separa como un único estado sucesor
contiguo, no como celdas aisladas. El margen determina la probabilidad anual; como en la conquista,
el estado del mundo decide si la secesión es posible y el azar solo decide cuándo ocurre.

El crecimiento produce directamente la tensión que causa la fragmentación, conectando ambos
sistemas: el término de tamaño aumenta con cada conquista, y el territorio tomado al otro lado de
un rival se convierte en un enclave muy difícil de conservar. Como la cohesión se ejecuta antes de
la expansión, un estado separatista queda activo y vulnerable el mismo año en que nace; la
reconquista por su antiguo dueño es común.

La capital nunca se separa, por lo que ningún estado puede ser reemplazado por completo por su propio
sucesor. Es una decisión de modelado, no una medida de seguridad: si se elimina esa protección no se
rompe ningún invariante, porque `PolityLifecycleSystem` reubicaría la capital o retiraría el estado.

### La capacidad administrativa es la constante sensible

Determina el tamaño de equilibrio de un estado y su intervalo útil es estrecho. Barridos de 2500
años en un mundo de 192 regiones con 12 estados iniciales:

| capacidad | resultado |
| --- | --- |
| 62 | fragmentación en unos 56 estados pequeños de 3,3 regiones; mayor participación 6 % |
| 130 | 113 estados creados y 105 extinguidos; el total oscila alrededor de 13 |
| **150** | **94 creados y 101 extinguidos; el total oscila alrededor de 11; mayor participación 15 %** |
| 280 | 4 secesiones en seis partidas; la conquista prácticamente no tiene oposición |
| 380 | ninguna secesión; descenso monótono |

### Comportamiento actual

20 semillas × 3000 años, 192 regiones y 12 estados iniciales: **305 estados creados, 320
extinguidos**, 491 reconquistas, 2369 expansiones, 10 conflictos entre efectos, **0 violaciones de
invariantes** y reproducción exacta de las 20 semillas.

```text
año      0   12.0 estados  mayor 15.0%
año    600   12.2 estados  mayor 14.8%
año   1200   11.7 estados  mayor 15.3%
año   1800   11.9 estados  mayor 15.0%
año   2400   11.5 estados  mayor 14.8%
año   3000   11.3 estados  mayor 15.0%
```

La creación y la destrucción están casi equilibradas, y **15 de 20 partidas superaron en algún
momento su cantidad inicial de estados**. El total realmente oscila en lugar de limitarse a bajar.
Según la semilla, se mueve entre 8 y 18. El tamaño promedio es de 17,4 regiones.

Un fragmento de la crónica de una partida dice:

```text
[  430] Dominion of Daneigard ceased to exist (no remaining territory).
[  620] Kingdom of Zoroath broke away from Dominion of Tavoukal with 7 region(s), seated at Eluth.
```

### Lo que todavía no hace

El equilibrio es *rígido*. La participación del mayor estado se mantiene en el 15 % y alcanza un
máximo del 20 % en todas las semillas, por lo que ninguna partida produce un imperio dominante. No
aparece ninguno de los dos extremos indeseados, que era el objetivo de esta etapa, pero todavía no
es cierto que los imperios surjan y caigan de forma dramática. Para eso, la tensión y la autoridad
deben variar con el tiempo en vez de ser constantes fijas, algo que puede aportar un modelo de
tecnología o administración.

## La capa de gobernantes

`RulerSuccessionSystem` se ejecuta en su propia fase `Rulership`, antes de `Polity`, por lo que la
capacidad de un nuevo gobernante entra en vigor antes de que la regla de cohesión la consulte ese
mismo año. Cada estado activo tiene exactamente un gobernante vivo; el aplicador asigna uno siempre
que se funda un estado, sin importar el camino de creación, así que ningún llamador puede romper el
invariante por omisión.

Un gobernante tiene un identificador estable, un nombre generado, año de nacimiento, capacidad
administrativa (0–100, calculada como la media de tres tiradas uniformes para concentrar la mayoría
cerca de 50), año de ascenso y año de muerte. **La edad se deriva del año de nacimiento y nunca se
almacena.** Los gobernantes muertos se conservan para siempre, de modo que un evento de ascenso del
año 90 todavía pueda mostrarse en el año 3000 aunque tanto el gobernante como su estado hayan
desaparecido.

`CohesionRules.EffectiveCapacity(ability)` transforma la capacidad sobre la base: 50 → exactamente
la `AdministrativeCapacity` configurada, 0 → 75 % y 100 → 125 %. La conversión vive en las reglas y
no en el gobernante porque expresa cómo esta simulación valora la administración, no una propiedad
intrínseca de la persona.

El sistema emite exactamente dos tipos de efecto: muerte y ascenso. Nunca modifica territorio,
estabilidad ni fronteras. Todo lo posterior surge porque la regla de cohesión existente lee un valor
distinto.

El final de un reinado y la muerte del gobernante son hechos distintos. `DeathYear` solo se asigna
cuando hay una muerte real; `ReignEndYear` y `EndReason` registran el fin del reinado, que también
puede deberse a la desaparición del propio estado. Un gobernante cuyo estado cae queda archivado
con vida y nunca vuelve a reinar.

### Momento de las mediciones

Tres ventanas definen todas las estadísticas de los gobernantes, y originalmente las tres eran
incorrectas:

- El territorio «al ascender» es el estado al *inicio* del año del ascenso, es decir, al final del
  año anterior. Medirlo al final del año eliminaba por definición las pérdidas del primer año de un
  sucesor débil.
- Un reinado posee los años `[accession, reignEnd - 1]`. El año del ascenso pertenece al sucesor.
  Por eso, los reinados consecutivos cubren toda la línea temporal sin duplicar ni omitir años.
- El diagnóstico del mecanismo se captura dentro de la fase mediante `MechanismObserverSystem`, que
  se ejecuta en `Polity` junto con la cohesión y lee el mismo estado previo a los efectos. Recalcularlo
  desde el mapa al final del año mediría las consecuencias de la decisión en lugar de la decisión.

El origen del índice proviene del año de la propia simulación y no de la configuración. Un mundo
entregado a `Simulation.Resume` puede encontrarse en cualquier año, y obtener el origen desde la
configuración desplazaría todas las ventanas una posición.

### Resultado medido: 20 semillas × 3000 años

28 086 muertes naturales, 322 reinados terminados por la caída del estado y 0 destituciones. Hay
**15 reinados de cero años, todos por extinción del estado**: un estado se separó y fue anexionado
en el mismo año. Ninguno terminó por muerte. Reinado medio de 26,7 años. **0 violaciones de
invariantes** y reproducción exacta de las 20 semillas.

```text
mecanismo (observado en fase, desde el estado que lee cohesión)
  todos los estado-años :  95,823 / 764,105 = 12.54% difieren de un administrador promedio
    gobernantes débiles expusieron 203,178 región-años; los fuertes conservaron 57,060 región-años
  estados de 20+ regiones: 86,980 / 256,602 = 33.90%

cambio territorial por reinado, según capacidad (regiones)
  0-19   -0.22 | 20-39 -0.04 | 40-59 +0.03 | 60-79 +0.06 | 80-100 +0.09

pérdida grave (>=25% del territorio en algún momento de los siguientes 25 años completos)
  tras una sucesión 2.8%   años ordinarios 2.9%   (control)
  por capacidad del sucesor: 0-19 3.6% | 20-39 3.1% | 40-59 2.6% | 60-79 2.7% | 80-100 2.2%
```

### La comparación A/B, que responde realmente a la pregunta

Mismas semillas y configuraciones; la banda de capacidad del gobernante es la única diferencia.
**A** asigna a todas las capacidades el 100 % de la base, por lo que los gobernantes existen pero no
tienen efecto mecánico. **B** utiliza el intervalo actual de 75–125 %.

```text
                                    A          B      B - A
mayor participación, media final % 14.95      13.75      -1.20
mayor participación, media pico %  17.35      17.30      -0.05
tamaño promedio de estado          17.44      14.93      -2.51
cantidad final de estados          11.25      13.15      +1.90
estados creados (total)              305        345        +40
estados extinguidos (total)          320        322         +2
secesiones (total)                   305        345        +40
reconquistas (total)                 491        559        +68
expansiones (total)                2,369      2,719       +350
violaciones de invariantes             0          0          0
mecanismo: estado-años cambiados    0.00%     12.54%    +12.54

cambio territorial por reinado, según capacidad
  0-19    -0.002   -0.224   -0.222
  20-39   +0.012   -0.039   -0.051
  40-59   +0.019   +0.031   +0.012
  60-79   +0.013   +0.057   +0.044
  80-100  -0.013   +0.087   +0.099
```

El brazo A es el control que permite interpretar el resto. Su tasa del mecanismo es exactamente
0,00 %, confirmando que el diagnóstico mide lo que afirma, y su columna territorial por banda es
ruido plano sin gradiente. Por eso, el gradiente monótono de B es el efecto del tratamiento y no un
artefacto de la medición.

**Los gobernantes sí cambian el equilibrio, y lo hacen hacia la fragmentación.** El tamaño promedio
de los estados baja un 14 %, los estados creados aumentan un 13 % y la cantidad final sube casi dos.
Lo que no cambia es el techo: la media de participación máxima es 17,35 % frente a 17,30 %, y el
máximo entre todas las semillas es 20 % en ambos brazos. Los gobernantes reducen el suelo, pero
dejan el techo exactamente donde estaba.

Es el mismo hecho estructural visto con más claridad: **la administración solo elimina un freno**.
Determina lo que un estado puede conservar, nunca lo que puede conquistar. Un buen administrador
conserva su herencia y nada más, mientras uno débil pierde provincias que rara vez se recuperan.

### Administración y expansión: resultado negativo, función desactivada

`ExpansionRules.OverextensionTerm(held, administration)` ajusta el término de sobreextensión según
la capacidad. Está **desactivado por defecto (100/100)**. En bandas de 125/75 a 200/20, con 50
semillas y 3000 años, alteró el número de expansiones pero nunca la distribución de participación
máxima: ninguna partida superó el 25 % bajo ninguna configuración, y la expansión adicional no se
concentró en los estados grandes. La implementación se conserva y sigue probándose —las pruebas la
activan explícitamente—, pero la administración solo afecta la cohesión.

El diagnóstico lo explicó: triplicar la tasa *base* de expansión movió el techo en todos los brazos,
incluido el de gobernantes inertes. La sobreextensión no es lo que limita el tamaño imperial. Un
modificador sobre una tasa ya cercana a cero no puede producir imperios.

## Capacidad militar

Cada gobernante tiene una segunda capacidad independiente: `Military`, entre 0 y 100 y calculada
como la media de tres tiradas uniformes. Solo regula el **ritmo de campaña**, es decir, la rapidez
con que un estado aprovecha una oportunidad que el cálculo de presión existente ya consideró viable.

```text
basePermille = clamp((pressure - MinPressure) / PressurePerPermille, 1, MaxAttemptPermille)
permille     = clamp(basePermille * CampaignTempoPercent(military) / 100, 1, MaxCampaignPermille)
```

El umbral de viabilidad está completamente antes de este cálculo, así que ningún comandante puede
hacer viable un objetivo imposible y la selección del objetivo no cambia.
`CampaignTempoPercent` consta de dos segmentos rectos que se unen en capacidad 50: 50 % con
capacidad 0, **exactamente 100 % con 50** y 800 % con 100. Una sola recta sobre una banda asimétrica
dejaría el valor neutral en 175 % y cambiaría silenciosamente todos los resultados existentes.

`MaxCampaignPermille` vale 1000, es decir, certeza. Su función es mantener la probabilidad dentro de
sus límites, no imponer un segundo techo: la base ya está limitada por `MaxAttemptPermille`, y volver
a aplicar ese límite después de multiplicar eliminaría por completo la bonificación.

### Barrido de bandas de ritmo

Cohesión, alcance, movilización y todas las demás reglas de expansión permanecen fijas. Brazo C,
10 semillas × 3000 años:

| banda | media pico % | máximo pico % | partidas >=20 % | partidas >=25 % | tamaño prom. | años sobre 20 % |
| --- | --- | --- | --- | --- | --- | --- |
| control (100/100) | 18.10 | 20 | 2 | 0 | 14.40 | 7.5 |
| 50 / 300 | 18.70 | 22 | 3 | 0 | 15.32 | 26.7 |
| 50 / 500 | 17.80 | 20 | 2 | 0 | 15.67 | 7.5 |
| **50 / 800** | **19.40** | **22** | **5** | 0 | **16.07** | **55.5** |
| 25 / 1200 | 18.70 | 22 | 4 | 0 | 15.06 | 46.5 |

500 fue indistinguible del control y 1200 no superó a 800, así que la respuesta no es monótona con
diez semillas. Sin embargo, 800 fue el mejor candidato según todas las medidas y quedó adoptado.

### Experimento de tres brazos

**A**: gobernantes inertes (capacidad 100 %, ritmo 100 %). **B**: solo administración (banda de
capacidad, ritmo 100 %). **C**: administración y capacidad militar (ambas bandas). La sobreextensión
es plana en los tres.

50 semillas × 3000 años, sin violaciones de invariantes en ningún brazo; determinismo comprobado por
separado con 20 semillas en los tres brazos.

```text
métrica                                  A          B          C
mayor participación, media final %   15.22      13.56      14.00
mayor participación, media pico %    18.50      18.62      19.34
mayor participación, máximo pico %   26.00      26.00      26.00
partidas con pico >= 20%                 17         17         22
partidas con pico >= 25%                  1          1          1
partidas con pico >= 30% / 40%            0          0          0
media de años sobre 20%                20.72      23.08      46.22
tamaño promedio de estado              16.69      14.72      15.61
cantidad final de estados              11.84      13.40      12.60
estados creados / extinguidos        938/946    877/807  1126/1096
secesiones / reconquistas           938/1559   877/1443  1126/2187
expansiones                            7,205      7,697     11,344
conflictos entre efectos                  30         24         62
violaciones de invariantes                 0          0          0

distribución de participación máxima (partidas por intervalo)
  0-17%   18  18  10
  18-19%  15  15  18
  20-21%  12  11  14
  22-24%   4   5   7
  25-29%   1   1   1
  30%+     0   0   0
```

**El mecanismo está claramente concentrado.** Expansiones por reinado según la banda militar del
comandante que actúa; A y B son los controles y permanecen planos:

```text
banda      A       B       C
0-19    0.105   0.109   0.072
20-39   0.103   0.104   0.084
40-59   0.106   0.105   0.122
60-79   0.102   0.102   0.271
80-100  0.107   0.097   0.397
```

Un comandante de 80–100 conquista 5,5 veces más que uno de 0–19. Los brazos sin la banda no
muestran gradiente, así que esto es efecto del tratamiento y no un artefacto.

**Surgen cuatro tipos de gobernante**, según el cambio territorial por reinado:

```text
combinación                 A          B          C
baja adm. / baja mil.   +0.017     -0.029     -0.106
baja adm. / alta mil.   +0.015     -0.032     +0.023
alta adm. / baja mil.   +0.015     +0.062     +0.015
alta adm. / alta mil.   +0.020     +0.051     +0.143
ambas >= 70             -0.009     +0.067     +0.300
ambas <= 30             -0.003     -0.083     -0.388
```

Un conquistador sin administrador casi no gana territorio (+0,023); un administrador sin comandante
conserva, pero no crece (+0,015); juntos son un orden de magnitud mejores (+0,143, y +0,300 para
gobernantes con más de 70 en ambas capacidades). Ninguna categoría está codificada de forma
explícita.

### Lo que todavía no hace

El techo de participación máxima no cambió. El máximo es 26 % en los tres brazos, exactamente una
partida de cada uno alcanza 25–29 % y ninguna llega al 30 %. Lo que C cambia es la *zona media* de la
distribución: diez partidas menos permanecen por debajo de 17 %, 22 de 50 superan el 20 % frente a
17, y el tiempo por encima del 20 % se duplica de 23 a 46 años por partida.

Por eso, el brazo C produce **potencias medianas más grandes y duraderas, no imperios**. El imperio
temporal del 25–35 % que buscaba el experimento no apareció con ningún ritmo probado. Igual que en
el resultado administrativo, la restricción dominante está en la retención —cohesión y alcance— y
no en la velocidad de conquista.

## Alcance administrativo: dos experimentos rechazados

`CohesionRules.DistanceStrainTerm(distance, administration)` ajusta todo el término de distancia
conectada según la capacidad administrativa del gobernante. Solo afecta la distancia conectada; la
desconexión, el tamaño, la prosperidad, el alivio por estabilidad y la capacidad administrativa no
cambian. Una provincia aislada de su capital sigue aislada sin importar quién gobierne.

**Está desactivado por defecto.** Se construyeron y midieron dos formas de conversión. Ambas
funcionaron exactamente según el diseño. Ninguna cambió el mundo.

### Primer intento: una banda simétrica y por qué falló

125 % con capacidad 0, 100 % con 50 y 75 % con 100. Mecánicamente impecable y muy concentrada:
54,9 % de los estado-años cambiaron para estados de más de 30 regiones, frente a 0,01 % por debajo
de 10. Sin embargo, el mundo quedó *más* fragmentado.

```text
estado-años cambiados                    8.97%
región-años expuestos por gobernantes débiles 100,558
región-años conservados por gobernantes fuertes 20,332
```

Una banda simétrica expuso cinco veces más territorio del que conservó porque la mayoría de las
provincias remotas está *por debajo* del umbral de descontento: aumentar la tensión empuja muchas
por encima, mientras reducirla solo ayuda a las pocas que ya lo superaban. Ampliar la banda empeoró
el resultado de forma monótona (tamaño promedio 14,40, 13,58 y 12,33 para 125/75, 150/50 y 175/25,
frente a 16,07 sin ella).

### Segundo intento: un beneficio unilateral

El diagnóstico pidió un único cambio —eliminar la mitad que eleva la tensión—, por lo que la
conversión quedó plana hasta capacidad 50 y desciende por encima de ella, exponiendo solo el extremo
fuerte:

```text
administración 0-50   -> 100% de tensión por distancia conectada
administración 51-100 -> descenso lineal hasta DistanceStrainAtStrongestPercent
```

La mitad de los gobernantes queda inerte por construcción y `region-years exposed` es **cero**, no
solo un valor pequeño. Una prueba lo verifica durante un barrido completo de múltiples semillas.

### El contrafactual: el mecanismo es perfecto

Misma capacidad modificada por el gobernante, multiplicador de distancia neutral y todo lo demás
idéntico, leído dentro de la fase desde el mismo estado que ve la cohesión. Brazo B al 50 %, 50
semillas × 3000 años y 192 regiones:

```text
estado-años cambiados                    2.89%
región-años conservados                107,293
región-años expuestos                        0

estado-años cambiados %, por tamaño <10  0.00 | 10-19  0.26 | 20-29  5.33 | 30+ 37.69
región-años cambiados, por distancia 0-1 12 | 2-3 5,115 | 4-5 45,179 | 6+ 56,987
región-años conservados, por adm.  0-39 0 | 40-59 34,583 | 60-79 64,858 | 80-100 7,852
```

Nada cambia por debajo de capacidad 40, en estados de menos de 10 regiones ni a una distancia de un
paso de la capital. El objetivo previsto se alcanzó con precisión.

### El control equivalente y por qué es necesario

Un beneficio unilateral reduce la tensión por distancia promedio de todo el mundo, por lo que
«imperios más grandes» tendría una explicación sencilla: la distancia se volvió más barata. Por
eso, el brazo C usa un multiplicador plano para todos, igual a la media de la conversión de B sobre
la distribución de capacidades. Esa media se calcula enumerando exactamente `(d1+d2+d3)/3` antes
de simular un solo año, para que el control no pueda ajustarse según los resultados que debe explicar.

```text
multiplicador medio esperado (distribución de capacidad) 93.33%
real, ponderado por tensión, brazo B                     92.62%
real, ponderado por tensión, brazo C                     92.86%
```

La diferencia es menor a un cuarto de punto. Ambos brazos aplican realmente el mismo descuento
promedio a personas distintas.

### Tres brazos, 50 semillas × 3000 años, todos verificados

```text
métrica                                A          B          C
mayor participación, media pico %  19.34      19.56      19.40
mayor participación, mediana pico %19.00      19.00      19.00
mayor participación, máximo pico % 26.00      26.00      26.00
partidas pico >= 20% / 25% / 30%   22/1/0     22/2/0     23/1/0
media de años sobre 20%             46.22      57.50      46.14
tamaño promedio de estado           15.61      15.40      16.20
cantidad final de estados           12.60      12.76      12.10
secesiones / reconquistas       1126/2187  1084/2100   953/1769
expansiones                         11,344     11,305      9,511
conflictos entre efectos               62         50         47
violaciones de invariantes              0          0          0

episodios imperiales al 20%             44         48         48
duración media (años)                 53.45      60.96      50.29
administración media en el pico       54.34      51.71      51.60
```

Medianas idénticas, máximos idénticos y 22 frente a 22 partidas por encima del 20 %. Los imperios
del brazo B alcanzan su máximo con administradores de media **51,7 frente a 54,3 en la base**, es
decir, más baja. Con 20 semillas parecía que B había producido el primer imperio del 25 % del
proyecto; con 50 semillas, A y C también alcanzan 26 %, y el episodio del 25 % de B resulta haber
llegado a su máximo bajo un administrador de 44, dentro del tramo plano donde el modificador no hace
nada.

### Por qué nada cambió, medido en lugar de supuesto

El observador suma ahora la tensión calculada por cada término de la regla:

```text
distancia conectada, proporción de toda la tensión 40.2%
término de tamaño, proporción de toda la tensión    61.9%
tensión eliminada por el beneficio                   2.97%
```

(Las dos proporciones superan el 100 % porque el término de prosperidad es negativo para las
provincias más pobres que el promedio de su estado.)

Una reducción del 50 % en el extremo superior de la capacidad, aplicada al mayor término geográfico,
elimina **tres por ciento de la tensión total**. En comparación, la banda de capacidad desplaza la
autoridad un 25 %. Nunca hubo una magnitud suficiente para cambiar el resultado.

### La siguiente restricción dominante: `SizeStrainPerRegion`

El tamaño representa el 62 % de toda la tensión. Con 3 puntos por región, un estado de 30 regiones
soporta 87 puntos de tensión en *cada* provincia antes de considerar la geografía, frente a un
presupuesto de autoridad de 150: el 58 % del presupuesto se consume solo por ser grande. A diferencia
de la distancia, el techo se mueve cuando cambia este término. Diez semillas × 3000 años, 192
regiones, sin ningún otro cambio:

| tensión de tamaño por región | media de participación máxima | máximo | tamaño promedio | estados finales |
| --- | --- | --- | --- | --- |
| 3 (publicado) | 19.4% | 22% | 16.1 | 12.1 |
| 2 | 20.6% | 25% | 19.8 | 9.8 |
| 1 | 24.0% | 31% | 24.3 | 8.2 |

Ya se rechazaron tres canales entre gobernante y territorio: sobreextensión, distancia simétrica y
distancia unilateral. Los tres modificaban términos equivalentes a pocos puntos porcentuales del
presupuesto de tensión. El techo imperial lo determina el término que todavía no se ha variado.

### Validación de configuración

Las conversiones aceptadas que dependan de un gobernante deben asignar exactamente 100 % a una
administración de 50. `CohesionRules.Validate` lo exige desde el constructor de
`CohesionSecessionSystem` y desde `BatchOptions.Parse`, de modo que un conjunto de reglas inválido
falle antes de simular un año y no después de producir datos inutilizables para veinte semillas.

Esto cierra un error real. Durante el experimento simétrico, las bandas `100/50` y `100/25`
superaban todo lo demás: media máxima de 20,70, siete de diez partidas por encima del 20 % y los
primeros episodios del 25 % del proyecto. Eran inválidas: una banda *lineal* unilateral mueve su
propio punto neutral, así que con `100/50` un administrador promedio pagaba el 75 % de la tensión
por distancia y todos los estados recibían un descuento. El resultado no lo indicaba.

```text
banda 125/ 75: capacidad 0 -> 125%, capacidad 50 -> 100%, capacidad 100 ->  75%   VÁLIDA
banda 100/ 50: capacidad 0 -> 100%, capacidad 50 ->  75%, capacidad 100 ->  50%   RECHAZADA
banda 100/ 25: capacidad 0 -> 100%, capacidad 50 ->  62%, capacidad 100 ->  25%   RECHAZADA
```

La conversión actual ni siquiera puede expresar esas bandas: no existe un extremo débil por donde
introducirlas. El validador detecta el mismo error en la banda de capacidad, donde dos datos de
prueba usaban silenciosamente `40/200` y daban a cada gobernante promedio un 20 % adicional de
capacidad.

Todavía se necesita un multiplicador global constante como control, por lo que vive en una opción
separada llamada `ExperimentalConstantDistancePercent`. Está exento de la regla del punto neutral,
no puede combinarse con la conversión por gobernante y queda desactivado por defecto. Una prueba
verifica que ninguna línea de comandos pueda activarlo sin nombrarlo explícitamente.
