# Arquitectura — Deep Arquitectura

## Objetivo

1. Mantener **idéntico** el sitio público (diseño, textos, animaciones, CTA a WhatsApp).
2. Migrarlo al stack que usamos en Azure: **.NET 10 + Blazor + EF Core + Azure SQL + App Service**.
3. Sumar un **panel de gestión** con base de datos de **clientes** y **seguimiento de obras**.
4. Sumar un **agente Community Manager** que genera contenido para Instagram, TikTok y Facebook.

## Decisiones

| Tema | Decisión | Por qué |
|---|---|---|
| Hosting | 1 sola app **Blazor Web App** en **Azure App Service (Linux)** | Un único deploy y un único recurso para mantener. No justifica separar API + WASM (como FarmHome) para un solo usuario de panel. |
| Sitio público | **Static SSR** (HTML puro, sin Blazor JS) | Igual de liviano que el sitio Vite, mejor SEO, mismo `style.css` y `main.js`. |
| Panel `/admin` | **Interactive Server** (SignalR) | Formularios y tablas reactivas sin escribir API ni JS. |
| Base de datos | **Azure SQL serverless** con oferta gratuita + **EF Core** con migraciones | Relacional (clientes → obras → etapas/bitácora/cobros), gratis a este volumen, se pausa sola. |
| Auth | Cookie con **un único admin**, contraseña hasheada (PBKDF2) en App Settings | Suficiente para un estudio de una persona. Ver *Próximos pasos* para multiusuario. |
| Agente CM | **Microsoft Agent Framework + Azure OpenAI** (mismo patrón que TurnoBot) con *tools* sobre la base | El agente lee obras y novedades reales y guarda **borradores**; nunca publica solo. |
| Marca | `marketing/marca.md` es la única fuente de verdad | La leen tanto el agente de la app como el subagente `contenido-redes` de Claude Code. |

## Diagrama

```
                    ┌────────────────────────── Azure App Service (Linux, .NET 10) ───────────────────────────┐
 Visitante ──HTTPS──▶  /  /electricidad  /gas   (Static SSR + main.js) ──POST /api/consultas──┐                │
                    │                                                                         ▼                │
 Profesional ─HTTPS─▶  /admin/*  (Blazor Interactive Server, cookie auth) ──▶ EstudioService ──▶ EF Core ─────────┼──▶ Azure SQL (serverless)
                    │        └── /admin/redes ──▶ CommunityManagerAgent ──tools──▶ CmTools ──┘                 │
                    └──────────────────────────────────────│───────────────────────────────────────────────────┘
                                                           ▼
                                                    Azure OpenAI (gpt-4o-mini)
```

## Modelo de datos

```
Cliente 1─* Obra 1─* EtapaObra
   │          ├──* RegistroAvance (bitácora; flag UsableEnRedes)  *─0..1 EtapaObra
   │          └──* Pago
   └─* Consulta (lead del formulario web; se "convierte" en Cliente)

Publicacion *─0..1 Obra   (contenido para redes; Borrador → Aprobada → Publicada | Descartada)

Capataz *─* Obra (CapatazObras)      Capataz 1─* SesionCapataz (tokens de los teléfonos)
RegistroAvance *─0..1 Capataz        RegistroAvance 1─* MedioRegistro (foto / video en Blob)

ArancelVersion 1─* Presupuesto *─0..1 Cliente
                    │  └─1 PresupuestoItem* (renglones congelados)
                    └─0..1 Obra   (se crea al aceptarlo)
```

- **Consulta**: lo que entra por el formulario del sitio. Estados `Nueva → Contactada → Convertida | Descartada`. Convertir crea el cliente (o lo vincula si ya existe ese email).
- **Obra**: código correlativo `OB-AAAA-NNN`, servicio (planos municipales, relevamiento, diseño, electricidad, gas), estado (`Presupuestada, EnCurso, EnTramiteMunicipal, Pausada, Finalizada, Cancelada`), expediente municipal, fechas, presupuesto.
- **EtapaObra**: se generan desde una plantilla según el servicio (`Services/PlantillasEtapas.cs`) y se pueden ajustar. El **% de avance** = etapas completadas / etapas no omitidas.
- **RegistroAvance**: bitácora diaria. Si se marca *Sirve para redes*, el agente CM la puede usar.
- **Pago**: cobros parciales → saldo contra el presupuesto.
- Enums guardados como **texto** (base legible). Borrar un cliente con obras está **bloqueado** (FK Restrict).

## Presupuestos (escala arancelaria del Colegio)

- **La escala es un dato, no código.** Cada tabla semestral del Colegio de MMO (Regional 1) es una `ArancelVersion`
  con vigencia y un JSON (`Presupuestos/Arancel.cs`): valor básico, tabla de uso y destino (coeficientes), escalas
  progresivas por tramo (arts. 63, 78 arquitectura/ingeniería/instalación, 109, 116), porcentajes del art. 79,
  valores fijos (arts. 28, 58, 108, carpeta) y reglas del registro diferenciado (Res. 202).
- La primera versión (1er semestre 2026, VB $741.000) se carga sola al arrancar (`ArancelSemilla.cs`).
  Las siguientes se cargan en **/admin/aranceles → Nueva versión**: duplica la anterior, al cambiar el valor básico
  ofrece escalar todos los montos en la misma proporción, y se corrige lo que haga falta contra el PDF.
- **Cálculo** (`CalculadoraHonorarios`, función pura y testeada contra los montos acumulados del PDF):
  monto de obra = Σ m² × VB × coeficiente (o manual) → honorarios por escala progresiva × % art. 79, recargo 1ª categoría,
  reducción art. 117, días por tramos, bonificación solo sobre honorarios, gastos y aportes aparte.
- **Presupuesto**: guarda la entrada (JSON, para reabrir/recalcular) y los renglones tal como se emitieron, con la versión
  de arancel usada. Si después se edita la escala, lo emitido no cambia. Número `PR-AAAA-NNN`.
- **Criterios tomados para el MVP** (el PDF no los aclara; se cambian desde /admin/aranceles sin tocar código):
  - Cocheras (grupo 4) = destino *Accesorio*: en el registro diferenciado siguen al destino principal
    (cochera de una vivienda → alícuota de vivienda); solas, 1 %.
  - Art. 78 arquitectura 2ª clase: el 3er tramo usa el mismo 10 % que el 2º (el PDF no lo indica).
  - Columna «10 %» de la tabla de uso y destino: no interviene en el cálculo.
  - Instalaciones 2ª clase, 3er tramo: se respeta el 1,50 % del PDF.
- Flujo: cliente → **Presupuesto** → imprimir/PDF o WhatsApp → *Aceptado → crear obra* (obra con etapas y el total como monto).

## App Android del capataz ("Deep Obra")

```
 Capataz (Android, .NET MAUI) ──HTTPS + Bearer──▶ /api/app/*  ──▶ CapatazService ──▶ Azure SQL
   │  cola offline en el teléfono                        └──────────────▶ IAlmacenMedios ──▶ Azure Blob (privado)
   └─ fotos / videos / novedades / etapas                Panel /admin/obras/{id} ◀── /admin/medios/{id} (con login)
```

- **Quién ve qué:** `Capataz *─* Obra` (tabla `CapatazObras`). Cada pedido de la API verifica que la obra esté asignada;
  si el estudio le quita la obra, la desactiva o le cambia el PIN, pierde el acceso en el momento.
- **Login:** usuario + PIN (hash PBKDF2) que da el estudio en `/admin/capataces`. Devuelve un token aleatorio;
  en la base se guarda solo su SHA-256 (`SesionesCapataz`), vence a los 90 días sin uso y se revoca desde el panel.
  El teléfono lo guarda en el almacenamiento seguro de Android.
- **Táctil, sin IA** (desde 2026-11-18; antes era 100 % por voz con Azure OpenAI y se cambió por el costo): pantallas con
  botones grandes, igual que Deep Cuadrilla. Donde hay que escribir hay un botón 🎤 de **dictado** con el reconocedor del
  teléfono (no usa IA ni cuesta nada; funciona sin señal si el teléfono tiene el idioma).
- **Pantallas:** Mis obras → Obra (arriba lo que espera al capataz: órdenes del estudio sin leer con «Leí y entendí»,
  avances de la cuadrilla por validar, tareas atrasadas sin causa) → Fotos y novedades · Etapas · Gasto · Material que
  sobró · Nota al estudio · Tareas atrasadas. Abajo, las últimas novedades de la bitácora.
- **Validar a la cuadrilla:** aprobar, corregir (cantidad o %) o rechazar con motivo (lo ve el albañil); «aprobar los N»
  manda solo los que se ven en pantalla (lo que llegue después se revisa aparte). Las fotos del avance se bajan con
  `GET /api/app/medios/{id}` (solo de sus obras); los videos se ven en el panel.
- **API** (`CampoCapataz`): `GET /obras/{id}` trae además `PorValidar`, `Ordenes` (sin leer por ese capataz) y `Atrasadas`
  (con causa sugerida por clima o avisos de falta de material); `POST /obras/{id}/validaciones` (lote, re-aplicar deja lo
  mismo), `/atrasos`, `/sobrantes` (Ref; a otra obra solo a una de las suyas), `/notas-pedido` (Ref; las fotos se suben
  con ese Ref a `/medios`), `/libro/leido` (solo órdenes de esa obra) y `/gastos` (**multipart**: JSON en `datos` + hasta
  5 comprobantes en `archivo`; o llega todo o nada, idempotente por Ref). Gastos y sobrantes llevan `Ref` (migración
  `CapatazTactil`).
- **Envío automático:** todo lo que carga el capataz se copia primero al teléfono (`envios.json` + archivos) y una cola en
  segundo plano lo sube en orden: al momento si hay señal, al volver la conexión y cada 30 s. Cada envío lleva un `Ref`
  (GUID) generado en el teléfono → reintentar nunca duplica. La fecha es la del momento en que se cargó.
  - Lo que el capataz ya resolvió (avance validado, orden leída, causa anotada, etapa cambiada) deja de verse como pendiente
    en el teléfono mientras está en la cola y hasta que la obra se vuelve a bajar del servidor (`Envio.Claves`).
  - **Envíos rechazados** por el estudio (obra quitada, dato inválido, novedad que nunca llegó tras 10 intentos): no se
    reintentan solos; aparece un aviso y la tarjeta de envíos se pone roja (tocarla: reintentar / borrar / ver detalle).
  - Cada envío guarda **de qué capataz es**: si en el teléfono entra otro, lo pendiente del anterior no se manda con su
    cuenta. Al cerrar sesión se avisa si quedan envíos y se borran los rechazados.
  - Si el servidor responde 409 a un archivo (no tiene su novedad), se vuelve a mandar la novedad (idempotente por Ref)
    y se sigue con los demás envíos.
- **Archivos:** fotos a 2560 px / calidad 80 (~1 MB, cámara del teléfono con `MediaPicker`); videos hasta 500 MB. En
  producción van a un contenedor privado de Azure Blob (`Medios:BlobConnectionString`); en desarrollo a `App_Data/medios`.
  El panel los sirve con login y soporta rangos (se puede adelantar un video).
- **Etapas:** el capataz las marca En curso / Terminada / Reabrir (omitir lo decide el estudio). Las novedades proponen la
  etapa en curso.
- En la bitácora del panel se ven las fotos y videos con el nombre del capataz, y se pueden marcar *aptas para redes*
  (las usa el agente CM).

**Pendiente para una v2:** subida en segundo plano con la app cerrada (WorkManager), subida por partes para videos muy
grandes con mala señal, notificación al estudio cuando llega una novedad, comprobantes en PDF desde el teléfono, y versión iOS.

## App Android de los albañiles ("Deep Cuadrilla")

```
 Albañil (Android, .NET MAUI, táctil) ──HTTPS + Bearer──▶ /api/albanil/* ──▶ AlbanilService ──▶ Azure SQL
   │  cola offline (cola.json + archivos)                          └──────▶ IAlmacenMedios ──▶ Azure Blob
   └─ fichada, avances con fotos/videos, pedidos, problemas, partes
 Panel: /admin/albaniles (cuentas) · /admin/obras/{id} (tareas y cuadrilla) · /admin/campo (pedidos, problemas, asistencia, partes)
```

- **Proyecto:** `src/DeepArquitectura.Albanil` (`com.deeparquitectura.cuadrilla`). Igual que la del capataz, queda fuera de
  `DeepArquitectura.slnx` (el CI corre en Linux sin el workload de Android). Build: `dotnet build src/DeepArquitectura.Albanil`;
  publicar con `-p:ServidorApp=https://<webAppUrl>`.
- **Modelo:**
  ```
  Capataz 1─* Albanil *─* Obra (AlbanilObras)     Albanil 1─* SesionAlbanil
  Obra 1─* TareaObra *─0..1 EtapaObra, *─0..1 Albanil (null = cualquiera de la cuadrilla)
  RegistroAvance +AlbanilId +TareaObraId +Cantidad +PorcentajeTarea   (los avances y problemas van a la bitácora, con fotos)
  Obra 1─* JornadaAlbanil (entrada/salida + GPS)   PedidoMaterial 1─* PedidoMaterialItem
  ProblemaObra ─0..1 RegistroAvance (mismo Ref: ahí cuelgan las fotos)   ParteDiario 1─* ParteDiarioItem (1 por albañil/obra/día)
  ```
- **Quién ve qué:** el albañil pertenece a la **cuadrilla de un capataz** y ve una obra solo si está asignado **y** la obra es
  de su capataz. Si el estudio le saca la obra al capataz, la cuadrilla deja de verla; cambiar de capataz le quita las obras que
  no son del nuevo. Todo lo que manda guarda `AlbanilId` + `CapatazId` (el capataz que tenía en ese momento) + `ObraId`.
- **Login:** usuario + PIN (mismas reglas y hash que el capataz), sesión propia en `SesionesAlbanil` (un token de capataz no
  sirve acá), 90 días deslizantes, se revoca desde la ficha del albañil. Limpieza diaria junto con las de capataces.
- **Tareas** (las carga el estudio en la obra): con unidad (m², m³, ml…) y cantidad objetivo, el albañil informa cuánto hizo en
  cada reporte y el servidor **suma todos los reportes** (un reintento o un envío atrasado no descuadra nada); sin unidad, se
  informa el % total y vale el del reporte más reciente según la fecha del teléfono. «Terminé esta tarea» la completa.
  Empezar una tarea pone en curso su etapa. Canceladas desaparecen de la app.
- **Fichada:** entrada y salida con ubicación (si hay GPS y permiso; si no, se ficha igual). La salida referencia el Ref de
  la entrada (409 si la entrada todavía no llegó). El panel muestra horas, 📍 a Google Maps y «sin salida» si se olvidó.
- **Pedidos de material** (varios renglones, urgente, nota) y **problemas** (tipo, urgencia, descripción, fotos): el estudio
  los responde en `/admin/campo` y el albañil ve el estado y la respuesta en la app. El inicio del panel muestra cuántos hay.
- **Parte diario:** horas por tarea + resumen; reenviarlo el mismo día lo reemplaza (máx. 24 h).
- **Offline:** misma idea que la app del capataz (cola persistente, Ref por envío, reintento cada 30 s y al volver la señal,
  409 → se reenvía lo anterior y se sigue). La app guarda la última copia de cada obra y la jornada abierta para funcionar sin señal.
- **Fotos:** 2560 px / calidad 80 vía `MediaPicker` (cámara o galería, varias a la vez); videos hasta 500 MB.

## Circuito de datos de obra

Todo lo que entra desde el campo se valida, se cierra y se usa. Lo **aprobado** (no lo informado) es lo que cuenta para
rendimientos, curva y certificados. Servicios en `Services/`; migración `CircuitoDatosObra`.

| # | Qué | Dónde se carga | Dónde se ve |
|---|---|---|---|
| 1 | **Validación de avances**: cada avance de tarea entra `Pendiente`; el capataz lo aprueba, corrige (cantidad o %) o rechaza con motivo. Rechazar un «terminé» reabre la tarea. `TareaObra` guarda informado (`CantidadHecha`/`Porcentaje`) y aprobado (`CantidadAprobada`/`PorcentajeAprobado`); el cálculo está en `CalculoTareas`. | Capataz desde su app (Validar avances, con las fotos) o panel | App: «a confirmar / aprobado / rechazado: motivo». Panel: En obra → Validar |
| 2 | **Recepción de materiales**: cuánto llegó de cada renglón del pedido + foto del remito. Pedido → `Entregado` o `EntregaParcial`; si falta algo se crea solo un problema «Falta material». | App: «Llegó material» / «Llegó este pedido» | En obra → Materiales recibidos; pedidos con «llegó N» |
| 3 | **Rendimientos**: cantidad aprobada ÷ horas del parte diario, por rubro (`TareaObra.Rubro`) y por persona; por jornada de 8 h. | — | `/admin/rendimientos`, rinde X/h en cada tarea |
| 4 | **Plan vs. real**: tareas con inicio/fin previstos y peso; curva semanal ponderada (plan lineal vs. real aprobado) y atrasadas. | Panel (tarea) | Ficha de obra (SVG), inicio, app («ATRASADA»), portal |
| 5 | **Controles por etapa** (antes de tapar): plantillas (hormigón, mampostería, sanitaria, eléctrica, gas, revoques, cubierta…), OK/observado con foto. Completar una etapa con controles sin aprobar avisa. | App (etapas en curso) | `/admin/obras/{id}/controles` |
| 6 | **Libro de obra**: órdenes de servicio numeradas (estudio → obra) con lecturas; notas de pedido numeradas (obra → estudio) con fotos. | Panel (órdenes); apps de la cuadrilla y del capataz (notas, «leí y entendí») | `/admin/obras/{id}/libro` (imprimible) |
| 7 | **Ubicación de la obra**: lat/lon/radio (a mano o «tomar de las fichadas» = mediana). Cada fichada guarda su distancia. | Panel | App avisa antes de fichar lejos; panel «a 2 km» |
| 8 | **Clima** (Open-Meteo, sin clave; solo viajan las coordenadas): 7 días + 3 de pronóstico cada 6 h (`Clima:Habilitado`). | Automático | Ficha de obra, app, resumen (días desfavorables) |
| 9 | **Fotos fijas**: puntos con encuadre fijo y frecuencia; la app muestra la foto anterior para encuadrar igual. | App | `/admin/obras/{id}/fotos-fijas` (antes/después, ▶ evolución), portal, agente CM (`ListarFotosFijas`) |
| 10 | **Resumen diario**: por obra, con alertas (urgentes, controles observados, sin protección, fichadas lejos, sin salida, atrasos, por validar). Con Azure OpenAI además se redacta en texto. | Automático | `/admin/resumen`, inicio del panel |
| 11 | **Certificados**: % físico aprobado acumulado − anterior × monto de obra (`Obra.MontoPresupuesto`); detalle congelado en JSON; «Cobrado» crea el pago. | Panel | `/admin/obras/{id}/certificados`, imprimible |
| 12 | **Portal del cliente**: `/seguimiento/{token}` (token aleatorio, se regenera o apaga). Avance, etapas, curva, fotos fijas y lo marcado «visible para el cliente». Sin nombres del personal, sin montos, `noindex`, HTML estático. | Panel | Cliente |
| 13 | **Dictado por voz** en todas las notas (reconocedor del teléfono, sin permiso de micrófono). | App | — |
| 14 | **Protección al fichar**: casco, calzado, guantes, protección ocular (+ arnés si trabaja en altura). Si falta algo se ficha igual y se crea un problema de Seguridad (Alta si es altura sin arnés). | App | Asistencia («⛑ sin…»), problemas, resumen |
| 15 | **Oficios y subcontratistas**: oficio de lista + empresa; una tarea sin asignar puede ser «solo para electricistas». | Panel | App (solo ve las de su oficio) |

- Lo que el servidor crea solo (problema por EPP o por faltante) usa un Ref derivado del envío (`DeterministaRef`):
  un reintento del teléfono no lo duplica.
- **Pendiente**: aviso por email/WhatsApp del resumen y de lo urgente (hay que elegir canal), time-lapse en video.

## App del cliente ("Mi obra", `/mi-obra`)

Para el comitente de una obra, separada del sitio de marketing. Es una **web instalable (PWA)** servida por el mismo
servidor: funciona en iPhone y Android sin tiendas (manifest + service worker en `wwwroot/mi-obra/`, íconos en `img/app/`).
Las pantallas son HTML estático (Blazor SSR, sin `blazor.web.js`): livianas con datos móviles; los formularios son POST
comunes con Post/Redirect/Get.

- **Acceso**: el estudio genera desde la obra (`/admin/obras/{id}/cliente`) un **código de obra** `DEEP-XXXX-XXXX`
  (alfabeto sin 0/O/1/I/L) y un **PIN de 6 dígitos** (PBKDF2; se muestra una sola vez, con el texto para WhatsApp/email).
  Tabla `AccesosCliente` (uno por obra). El código acepta minúsculas, espacios y sin guiones.
- **Sesión**: segundo esquema de cookie `Cliente` (`deeparq.cliente`, path `/mi-obra`, 180 días deslizantes), separado
  del panel. Lleva obra + sello; dar PIN nuevo o desactivar cambia el sello y cierra las sesiones (validación cacheada 1 min).
- **Fuerza bruta**: 10 intentos / 15 min por IP (`login-cliente`) y, por código, 8 PIN errados → bloqueado 15 min.
- **Pantallas**: Inicio (% de avance, etapa actual, pendientes, saldo), Avance (curva y etapas con trabajos, solo lo
  aprobado), Fotos (fotos fijas antes/ahora + bitácora marcada visible), Certificados y pagos (solo emitidos; los
  adicionales aprobados se suman al total), Planos y documentos, Mensajes y Aprobaciones.
- **Aprobaciones** (`AprobacionesCliente`): título, descripción, opciones (o aprobar/no aprobar), costo adicional, fecha
  límite y adjunto. Queda nombre, fecha e IP de quien respondió; la respuesta es atómica (vale la primera) y no se cambia
  desde la app. El estudio solo puede anular las pendientes.
- **Documentos** (`DocumentosObra`): PDF, imágenes, docx, xlsx hasta 50 MB, en el mismo almacenamiento de medios.
  PDF e imágenes se abren en el navegador. Un adjunto de aprobación se ve aunque el documento no esté marcado visible.
- **Mensajes** (`MensajesObra`): idempotentes por un Ref del formulario (doble toque con poca señal).
- **Sin señal**: el service worker muestra la última versión vista de cada pantalla y guarda las fotos; al cerrar sesión
  se borra todo lo guardado.
- **Panel**: `/admin/app-cliente` (bandeja de todas las obras) y un indicador en el inicio.
- El portal de solo lectura `/seguimiento/{token}` sigue existiendo, para compartir con terceros sin dar acceso a pagos.
- **Pendiente**: avisar al cliente cuando hay novedades (push web o WhatsApp) y que el cliente adjunte fotos a los
  mensajes. (Los pagos que informa el cliente ya están: ver «Datos del proceso».)

## Datos del proceso (comercial → costos → cierre → postventa)

Entradas de datos en los huecos del proceso (migración `DatosProceso`). Servicios: `EstudioService` (origen y estados de
consultas), `Relevamientos`, `CostosObra`, `TramitesObra`, `Postventa`, `Indicadores`, `Adjuntos`. Tests: `DatosProcesoTests`.

1. **Origen de la consulta**: `main.js` guarda el primer contacto (utm + sitio de origen + página de entrada, 90 días,
   un link con utm lo pisa) en `localStorage` y lo manda con la consulta. El servidor deduce el **canal** (Instagram,
   Facebook, TikTok, WhatsApp, buscador, cartel/QR, recomendación, directo) y, si `utm_campaign=pub-{id}`, la publicación
   que la trajo. En Redes cada publicación muestra su link con seguimiento y cuántas consultas trajo. El canal se puede
   corregir en Consultas y pasa al cliente al convertirla. Se guarda cuándo se contactó por primera vez (tiempo de respuesta).
2. **Motivo de pérdida**: descartar una consulta o marcar un presupuesto como rechazado exige motivo (precio, plazo, eligió
   a otro, no respondió…). Los presupuestos guardan `DecididoEn` (tiempo de decisión del cliente).
3. **Relevamiento** (`/admin/relevamientos`): ficha de la primera visita desde el celular (medidas, superficie calculada,
   estado, servicios, hallazgos de una lista, pedido y presupuesto del cliente, ubicación del GPS, fotos). Se arranca desde
   una consulta, un cliente o una obra; si la obra no tenía ubicación, la toma.
4. **Precios del material**: en `/admin/obras/{id}/costos` se carga el precio unitario de cada renglón recibido, el proveedor,
   el N° de factura y la foto/PDF. Indicadores compara precios por material y proveedor (nombres normalizados).
5. **Gastos de obra** (`GastosObra`): subcontratos, fletes, alquileres, compras sin remito, trámites; con etapa y comprobante.
   El capataz los carga en su app con la foto del comprobante (un solo envío, no duplica si reintenta). Lo que entra con remito NO va
   como gasto (se cuenta por la recepción con precio).
6. **Costo de mano de obra**: valor hora por persona (Personal de obra). Se copia en cada fichada (un aumento no cambia lo
   ya trabajado). La rentabilidad reparte las horas de cada día entre etapas según el parte diario.
   **Rentabilidad** = monto de obra + adicionales aprobados − (materiales con precio + mano de obra + gastos − recuperado por
   sobrantes), por obra y por etapa; avisa si faltan precios o valores hora.
7. **Causa de atraso** (`TareasObra.CausaAtraso`): en la obra, cada tarea atrasada tiene un selector; el sistema sugiere
   «clima» (≥2 días desfavorables desde el inicio previsto) o «falta de material» (avisos sobre la tarea). El capataz la anota
   en su app (Tareas atrasadas). Indicadores suma tareas y días de atraso por causa.
8. **Trámites** (`/admin/obras/{id}/tramites`): trámites por organismo con historia de movimientos (presentado, observado,
   aprobado…), expediente, vencimiento y respuesta adjunta. Plantillas por servicio (Colegio/municipio, Ecogas, ERSeP/EPEC).
   El inicio del panel avisa los que vencen en 7 días; el cliente los ve en `/mi-obra/tramites`; Indicadores mide días hasta
   aprobar y observaciones por organismo.
9. **Sobrantes** (`MaterialesSobrantes`): qué sobró y qué se hizo (devuelto, a otra obra, depósito, desperdicio), con valor
   recuperado; también desde la app del capataz. Indicadores: % de desperdicio por material contra lo recibido.
10. **Acta de entrega** (`/admin/obras/{id}/cierre`): observaciones de la recorrida con fotos «antes/después»; se publica y
    el cliente firma en `/mi-obra/entrega` (nombre + IP, atómico). Con pendientes solo puede firmar «con reservas» y
    explicando. Al firmar arranca la garantía (meses configurables) y se le pide la encuesta de entrega.
11. **Reclamos de garantía**: el cliente reporta en `/mi-obra/garantia` (rubro de una lista, lugar, fotos, urgente;
    idempotente por Ref). El estudio responde en `/admin/postventa` y anota causa raíz, etapa, responsable y costo
    (obligatorio al cerrar). Indicadores: reclamos por rubro y causa, costo y días para resolver.
12. **Encuestas y permiso**: al completar una etapa (si el cliente tiene la app) y al firmar el acta se crea una encuesta
    0–10 + comentario que aparece en el inicio de su app (NPS en Indicadores). En `/mi-obra/compartir` el cliente autoriza
    (y puede retirar) el uso de fotos, un testimonio y si va con su nombre. El agente de redes ve el permiso por obra
    (`PermisoFotos`), excluye las obras que dijeron que no y solo usa testimonios reales (`ListarTestimonios`).
13. **Pagos informados**: el cliente avisa un pago en `/mi-obra/pagos` (monto, fecha, medio, certificado, comprobante
    obligatorio si es transferencia; idempotente). El estudio lo confirma en `/admin/postventa`: se crea el pago y, si cubre
    el certificado informado, el certificado queda pagado (transacción dentro de la estrategia de reintentos).

Detalles técnicos:
- **Adjuntos** (`Adjuntos`): fotos jpg/png/webp/heic o PDF hasta 15 MB, máximo 5 por envío, colgados de un solo dueño. El
  cliente solo abre los de sus reclamos, sus pagos y las observaciones de un acta publicada de su obra (`/mi-obra/adjuntos/{id}`);
  nunca los de gastos, facturas ni relevamientos. Se sirven con `nosniff`.
- Los formularios de la app del cliente con archivos van a endpoints mínimos (`ClienteProcesoEndpoints`) con antiforgery
  a mano; el resultado se muestra con un aviso de una sola vez guardado en una cookie corta (no en la URL).
- Las sumas de montos se hacen en memoria (SQLite de los tests no suma decimales).

## Agente Community Manager

- Instrucciones = guía de marca (`marketing/marca.md`) + reglas del subagente `contenido-redes` (formato de pieza, adaptación por red, calendarios, voseo).
- Herramientas (`Agents/CmTools.cs`):
  - `ListarObras` / `ListarNovedadesParaRedes` → materia prima real. **No exponen datos personales** (nombre, teléfono, email, dirección); como mucho la localidad. Hay un test que lo verifica.
  - `ListarPublicacionesRecientes` → no repetir temas.
  - `GuardarBorrador` → deja la pieza en la tabla `Publicaciones` como **Borrador**.
- Publicar en las redes es manual (el profesional aprueba y marca *Publicada*).

## Seguridad

- `/admin/*` exige login; login con antiforgery, *rate limit* (10 intentos / 15 min por IP) y cookie HttpOnly.
- `/api/consultas`: validación, *honeypot* anti-bots y *rate limit* (5 / 10 min por IP).
- Secretos solo en App Settings / user-secrets (nunca en el repo). HTTPS obligatorio, HSTS, headers básicos.
- Azure SQL solo acepta conexiones desde la subred de la app (VNet integration + service endpoint; requiere B1+ y misma
  región, si no se cae a "servicios de Azure"). La app entra con su **identidad administrada**, sin contraseña.
- Sesiones vencidas de los teléfonos: se borran solas una vez por día (`LimpiezaSesiones`).
- **Sin claves a la vista:** la clave del panel (y la de Azure OpenAI si se usa) está en **Key Vault** y la app la lee con
  su identidad (referencia `@Microsoft.KeyVault`); el Blob Storage no acepta claves (`allowSharedKeyAccess: false`) y la
  app entra con su identidad (rol Storage Blob Data Contributor, `Medios:BlobUrl`).
- APK: el servidor (`ServidorApp`) es obligatorio y https fuera de Debug.

## Próximos pasos (fuera del MVP)

2. **Dominio propio** + certificado administrado en App Service.
3. Opcional: en vez de dejar la identidad de la app como admin Entra del servidor SQL, un usuario contenido con `db_owner`
   solo en la base.
4. **Multiusuario** (ASP.NET Core Identity) si se suman colaboradores.
5. Publicación programada vía **Meta Graph API** (Instagram/Facebook) una vez aprobados los borradores.
6. Aviso por email/WhatsApp cuando entra una consulta nueva.
7. Portal del cliente para ver el avance de su obra (link privado de solo lectura).

## Despliegue en Azure (pruebas, desde 2026-11-18)

- Grupo **`rg-deeparq`** en **West US 3** (en esta suscripción Central US, East US/East US 2, South Central, Canada
  Central y Brazil South no tienen cupo de App Service B1, y West US 2 / East US 2 no aceptan servidores SQL nuevos).
- App: `https://app-deeparquitectura-do4qpunyynw2q.azurewebsites.net` (B1 Linux, ~USD 12,41/mes). SQL serverless con la
  oferta gratuita (`useFreeLimit`, pausa a los 60 min; si se agota el cupo mensual se pausa, no cobra). Application
  Insights con tope de 0,5 GB/día. Key Vault `kv-deeparquit-do4qpuny`.
- **Tareas propias de la app** (clima y limpieza de sesiones) corren **una vez por día a las 6 (Córdoba)**
  (`Clima:HoraLocal`): cada corrida despierta la base al menos una hora, y con el clima cada 6 h se agotaba el cupo gratis.
- Publicar a mano: `dotnet publish -c Release` → zip con **`tar.exe -a -c -f web.zip *`** (el `Compress-Archive` de
  PowerShell 5.1 arma el zip con `\` y el deploy en Linux falla) → `az webapp deploy --type zip`. El workflow de GitHub
  necesita el secreto `AZURE_CREDENTIALS` y la variable `AZURE_WEBAPP_NAME` (todavía sin cargar).
- Tras el primer deploy, la referencia a Key Vault puede quedar en *AccessToKeyVaultDenied* (el permiso tarda en
  propagarse): cambiar cualquier App Setting la vuelve a leer.
- APKs de prueba: `dotnet publish -c Release -f net10.0-android -p:ServidorApp=<url> -p:AndroidPackageFormats=apk`.
  Van firmados con la clave de depuración (sirven para instalar a mano; para Play Store hace falta una clave propia).
