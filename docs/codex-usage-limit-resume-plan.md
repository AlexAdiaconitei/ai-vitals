# Plan: reanudar hilos de Codex bloqueados por límite de uso

Estado: implementado con correcciones de revisión · Fecha: 2026-10-03 · Alcance: solo Codex (ChatGPT Desktop y CLI)

Este documento conserva el plan inicial y sus sondas. La política de ejecución vigente de 0.3.0 está en la [verificación de arquitectura actualizada](codex-resume-architecture-audit.md). La corrección del 2026-10-05 recupera tareas pendientes tras un reset no observado, reinicio o suspensión; elimina la caducidad de autorización y el tope fijo de tres tareas. Los límites temporales descritos más abajo pertenecen al diseño anterior.

Claude Code queda fuera: desde v2.1.234 continúa solo tras el reset del límite
([docs](https://code.claude.com/docs/en/interactive-mode#wait-for-a-usage-limit-to-reset)).

## Objetivo

Cuando un hilo de Codex se queda a medias por el límite de uso, AI Vitals:

- **Fase 1**: lo detecta, muestra cuándo se podrá continuar y, al resetear, avisa con un acceso directo para reabrirlo en Desktop.
- **Fase 2**: si el usuario lo ha armado y el hilo no está abierto en otra app, lo continúa solo abriendo una terminal con `codex resume`.

## Evidencia (verificada en local, 2026-10-03)

| Hecho | Fuente |
|---|---|
| Desktop lanza su propio `codex.exe app-server` por stdio, hijo de `ChatGPT.exe`; no es accesible desde fuera. | `Win32_Process` command lines |
| Cada hilo tiene un lock de SO en `~/.codex/thread-writer-locks/<thread-id>.lock` (fichero vacío, `LockFileEx`). Desktop retiene el lock de **todos** los hilos que ha cargado desde que arrancó, no solo el visible. | Sonda `LockFileEx` compartido + `FAIL_IMMEDIATELY` → `ERROR_LOCK_VIOLATION (33)` en los 10 locks |
| La CLI muestra "This conversation is open in another app" cuando el lock está tomado. | Reporte del usuario |
| El fallo por límite queda en el rollout: `event_msg` → `task_complete` con `error.codex_error_info = "usage_limit_exceeded"`. | `~/.codex/sessions/**/rollout-*.jsonl` |
| El rollout guarda por turno `turn_context` con `cwd`, `model`, `approval_policy`, `sandbox_policy`, `collaboration_mode.settings.reasoning_effort`. | idem |
| Hay hilos de subagente: `session_meta.parent_thread_id` presente. | idem |
| `~/.codex/session_index.jsonl` da `thread_name` por id. | idem |
| `codex resume [SESSION_ID] [PROMPT]` existe, acepta `-c key=value`. | `codex resume --help` (CLI 0.160) |
| `codex://threads/<thread-id>` abre el hilo en Desktop; los deep links no envían prompts. | [Codex commands](https://learn.chatgpt.com/docs/reference/commands.md) |
| AI Vitals ya recibe `resetsAt` por ventana vía `account/rateLimits/read|updated`. | `CodexUsageAdapter.cs:126,162`, `CodexObservationMapper.cs:102` |
| `~/.codex/sessions` pesa ~584 MB / 122 rollouts: no se puede leer entero en cada sondeo. | `du` |

## Términos de dominio (añadir a `plan/CONTEXT.md`)

- **Hilo bloqueado (blocked thread)**: hilo raíz de Codex cuyo último turno terminó con `usage_limit_exceeded` y no tiene turnos posteriores.
- **Reanudación armada (armed resume)**: permiso explícito del usuario para que AI Vitals continúe un hilo bloqueado tras el reset.
- **Retención del hilo (thread hold)**: el lock de escritura del hilo está tomado por otro proceso (Desktop o una CLI).

## Paso 0: spikes (antes de escribir código de producción)

1. **Fuente de detección.** Comprobar si `thread/list` + `thread/read` del app-server propio devuelven el último turno con `status: "failed"` y `codexErrorInfo` para un hilo **retenido por Desktop**, sin tomar su lock.
   - Si funciona → fuente principal = API documentada; el rollout queda como respaldo.
   - Si no → fuente principal = lectura de cola (tail) de rollouts.
2. **Hook `Stop` de Codex.** Comprobar si se dispara en un turno que falla por límite. Si lo hace, sirve como disparador inmediato (ya está instalado para el semáforo); si no, solo sondeo.
3. **`codex resume <id> "<prompt>"` con overrides.** Verificar que `-c approval_policy=… -c sandbox_mode=… -c model=… -c model_reasoning_effort=…` se aplican al hilo reanudado y que el turno arranca sin intervención.
4. **Liberación del lock.** Confirmar que al salir del todo de Desktop (incluida la bandeja) los locks quedan libres en segundos.

### Resultados (2026-10-03, CLI 0.160.0)

| Spike | Resultado | Evidencia |
|---|---|---|
| 0.1 | **OK.** `thread/read` (`includeTurns: false`) + `thread/turns/list` (`limit: 1`, `sortDirection: "desc"`) desde un app-server propio leen hilos retenidos por Desktop sin tocar el lock. El último turno trae `status: "failed"` y `error.codexErrorInfo: "usageLimitExceeded"` (enum confirmado en `generate-json-schema`). `thread.status` sale `notLoaded`: la API **no** revela la retención por Desktop, así que `CodexThreadHoldProbe` sigue siendo necesario. → Fuente principal: API; el rollout ya no hace falta. | Hilos `01a0fc2d…`, `01a0fc6d…` |
| 0.2 | **Negativo.** El hook `Stop` no se dispara en turnos que fallan por límite: en `logs_2.sqlite`, a `Turn error` le sigue `turn/completed` sin ningún `hook/started`. → Solo sondeo + `account/rateLimits/updated`. Efecto colateral: el semáforo de actividad se queda en amarillo/rojo en esas sesiones (bug aparte). | 4 de 4 fallos del 2026-10-02 23:11 UTC |
| 0.3 | **Parcial.** Con `codex exec resume`, `-c sandbox_mode` y `-c model_reasoning_effort` se aplican al `turn_context`; `approval_policy` queda en `never` porque `exec` lo fuerza. Sin `-c`, el hilo **no hereda** el contexto de su último turno: toma `config.toml` y valores por defecto (sandbox `read-only`). → Overrides obligatorios. Pendiente: la CLI interactiva `codex resume` (aprobaciones y arranque sin intervención), que requiere cuota. | Hilo desechable `01a0ff1b…` |
| 0.3 (interactiva) | **OK.** `codex resume <id> <prompt> -c …` arranca el turno sola, sin aviso de confianza en una carpeta no confiable, y aplica `approval_policy`, `sandbox_mode` y `model_reasoning_effort`. | Hilo desechable, 02:21 |
| 0.4 | **OK.** Al salir de ChatGPT, los locks se liberan y Codex borra los `.lock`. Si un proceso muere a la fuerza, el `.lock` puede quedar huérfano pero libre: hay que sondear el lock, no si existe el fichero. | Sonda tras salir de Desktop |
| E2E | **OK.** Prototipo (`run.ps1`) con ChatGPT cerrado: ambos hilos reales se lanzaron 1 s después del reset (04:16, 04:19) y arrancaron en 2 s con `never` / `danger-full-access` / `high`; el primero terminó bien a los 7 min. Un turno en curso en otro proceso se ve como `interrupted` desde un app-server ajeno: ese estado no sirve para decidir nada. | `resume-proto/run.log` + rollouts |

Notas: `thread/list` sin filtros no devuelve hilos de `codex exec` ni subagentes, solo hilos interactivos (Desktop, CLI, VS Code).

### Estado de la implementación (rama `feat/codex-resume-after-limit`)

Fases 1 y 2 implementadas en un solo corte:
- `AIVitals.Adapters.Codex/CodexPausedThreads.cs`: escáner (API), `CodexThreadLock`, `CodexTurnContextReader`, `CodexResumeLauncher` (lista blanca y deep link).
- `AIVitals.Application/CodexResume.cs`: `CodexResumeTracker`, una máquina de estados pura.
- `AIVitals.App/CodexResumeController.cs` + la tarjeta en Conexiones. Preferencias con esquema v5 (`CodexResume`).
- Se lanza la CLI como proceso de consola hijo de la app: Windows lo abre en la terminal por defecto. Sin `wt.exe` ni `pwsh` intermedios.

### Continuación con la CLI abierta (daemon compartido)

Desde la 0.157, cada sesión interactiva de `codex` vive dentro de un daemon compartido (`app-server --listen unix:// --managed-daemon`, socket `CODEX_HOME/app-server-control/app-server-control.sock`) y la CLI es un cliente suyo. El daemon retiene el lock de los hilos que tiene cargados, por eso la sonda los ve como "retenidos".

El prototipo verificó el 2026-10-03 que una conexión al socket AF_UNIX podía llamar a `thread/loaded/list`, `thread/resume` y `turn/start` y que la CLI mostraba el turno en directo con la configuración del hilo cargado. La implementación corregida utiliza `codex app-server proxy` por stdio, revalida el turno bloqueado y distingue los envíos aceptados de los resultados desconocidos. Esta revisión no ejecutó turnos reales.

Orden de continuación implementado (`CodexDaemonContinuation`, después `CodexResumeLauncher`):
1. Hilo cargado en el daemon → `turn/start` allí (si está `active`, no se envía nada).
2. Hilo no cargado y lock libre → CLI nueva con `codex resume … -c`.
3. Lock retenido fuera del daemon (Desktop o CLI con `--no-daemon`) → esperar y reintentar.

Pendiente: comprobar que ChatGPT Desktop, al volver a abrirse, puede abrir los hilos que avanzó la CLI, y si vuelve a bloquear los que tenía abiertos.

Otros hallazgos:
- Hay hilos bloqueados antiguos (p. ej. `01a0442c…`, 2026-08-29) que el usuario abandonó. El escáner debe ignorar los bloqueos de ventanas ya reseteadas hace más de 24 h.

## Contrato de la implementación corregida

Este contrato sustituye las propuestas de fases 1 y 2. Los spikes anteriores documentan el prototipo, no pruebas E2E de esta revisión.

- Detección por API: `thread/list` se pagina hasta ocho días de antigüedad; `thread/turns/list` pide un solo turno con `itemsView: "notLoaded"`. Solo se aceptan hilos raíz cuyo último turno haya fallado con `usageLimitExceeded`.
- Cada bloqueo se vincula a los identificadores de ventanas exhaustas que contienen el instante del fallo. Un bloqueo descubierto durante su misma ventana de cinco horas conserva la autorización aunque hayan pasado más de treinta minutos. Las ventanas largas solo adquieren bloqueos nuevos dentro de treinta minutos; no se atribuye una ventana semanal a un bloqueo antiguo de cinco horas. Sin vinculación guardada, un reset histórico exige continuación manual.
- Para continuar automáticamente deben haber pasado el reset y sesenta segundos. Todas las cuotas conocidas deben estar frescas y disponibles; los buckets vinculados deben tener lecturas posteriores a su reset. Una ventana caducada o ausente nunca equivale a cuota libre.
- Antes de actuar se relee el hilo y la cuota por API. El último turno debe seguir siendo el mismo `BlockedTurnId` y conservar el error de límite. La ruta del daemon repite esa verificación dentro del propio daemon antes de `turn/start`; la ruta de terminal vuelve a comprobarla antes de abrir la CLI.
- Hay un interruptor por bloqueo. La opción global arma únicamente bloqueos nuevos, no los que ya estaban en la lista. Un hilo que ya recibió un intento confirmado o ambiguo no se rearma automáticamente al bloquearse otra vez.
- Se guarda estado en preferencias v5: ventanas, autorización, aviso emitido, reservas de lanzamiento, resultado y contadores. Se persiste una reserva antes del envío. Si la app se cierra durante el envío, al arrancar presenta resultado desconocido y no lo repite.
- El reset real fija los plazos. Si la primera evaluación llega más de treinta minutos tarde, o la máquina despierta tarde tras una pausa larga, el hilo exige continuación manual. Los intentos por retención ya iniciados pueden seguir cada cinco minutos durante dos horas, sin ignorar una suspensión larga.
- Se separan los intentos dos minutos y se limita a tres por reset. Los contadores sobreviven a reinicios; los intentos ambiguos consumen un lugar por prudencia. Un lock retenido no consume el tope.
- La conexión al daemon utiliza WebSocket sobre su socket de control local, con HTTP Upgrade mediante `ClientWebSocket`. El proxy oficial solo transmite bytes: no convierte JSONL a WebSocket. Se cierra únicamente la conexión propia, nunca el daemon compartido. La negociación tiene un plazo de cinco segundos independiente del plazo total de ejecución.
- Un hilo cargado en `systemError` puede recibir la continuación si se revalida el mismo turno con `usageLimitExceeded`. Los estados activos, errores diferentes y turnos modificados no reciben otro prompt.
- Con la opción automática activada se sondea cada tres minutos, aunque la cuota todavía no esté disponible. La tarjeta muestra la última comprobación correcta y el número de bloqueos detectados. `codex-resume.jsonl` guarda solo categorías de fallo, IDs, fases, tiempos y contadores de cuotas; rota a `.previous` al llegar a 512 KiB.
- Se diferencia entre turno confirmado, fallo y resultado desconocido. Una terminal nueva se confirma leyendo un nuevo turno; crear un proceso no basta. El timeout no mata esa terminal ni causa un segundo envío. Un resultado desconocido se resuelve desde la CLI o app original, no mediante reintentos automáticos.
- La terminal nueva usa `--no-daemon` para que su configuración sea independiente. Se reproduce el contexto del turno bloqueado, incluido su `cwd`, modelo, esfuerzo, aprobaciones, raíces escribibles, red y exclusiones temporales. Se rechazan valores desconocidos, restricciones de lectura y perfiles personalizados que no puedan reproducirse mediante las opciones soportadas. No se sustituyen por los valores del `config.toml`.
- Los argumentos no pasan por un shell. Los shims npm/pnpm se resuelven a Node o se rechazan.
- Los títulos privados están ocultos por defecto. No se utiliza `preview` como etiqueta. El usuario puede habilitar los títulos; solo se mantienen en memoria. La cola persistida no contiene títulos, directorios, mensajes ni credenciales. La lectura acotada del final del rollout puede traer registros vecinos a memoria, que se descartan sin interpretarlos ni guardarlos.
- Las preferencias v4 mantienen los descartes y migran el interruptor global a desactivado, porque su semántica de autorización ha cambiado.

## Verificación de la revisión

Pruebas automatizadas cubren cuota parcialmente caducada, bucket ausente, margen del reset, reinicio con autorización, despertar tardío, bloqueo semanal histórico, reset desplazado, selección por hilo, desarme, espaciado, tope persistido, retención, descarte y resultado ambiguo tras un cierre.

Los contratos cubren paginación, revalidación de turnos modificados, timeout antes y después del envío, permisos completos soportados y rechazo de restricciones desconocidas, shims sin shell, solicitudes del servidor con ID coincidente y confirmación de inicio frente a salida del proceso.

Las sondas locales de lectura contra Codex CLI 0.160.0 verifican handshake y escaneo de metadatos. No reanudan tareas reales. Queda como comprobación manual de release: agotar cuota, armar un hilo y observar su continuación tras reset con esta versión, tanto en una CLI abierta como en una terminal nueva. Repetir con ChatGPT reteniendo el lock.

La [revisión de arquitectura del 2026-10-04](codex-resume-architecture-audit.md) añade verificación nativa del transporte del daemon y de permisos, aislamiento de historiales no compatibles, autorización explícita del backend y exclusión de instancias duplicadas.
