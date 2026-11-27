# Actividad 7.2.11 — Impresión del ticket de venta ESC/POS (req. 16, CU-19)

Entregable: módulo de impresión ESC/POS e impresión del ticket de venta.

## Qué hace

- **Ticket original al despachar.** Cuando el operador marca un pedido como "Enviado" o "Listo para recoger" (`POST /api/v1/operacion/pedidos/{folio}/estado`, de la 7.2.23 de Aldo), la API imprime el ticket en la impresora de caja del restaurante.
- **Si la impresora falla, el despacho no se pierde.** El cambio de estado ya quedó guardado y la respuesta trae un aviso. El panel del operador lo muestra y el operador usa **"Reimprimir ticket"** (CU-19 FA 2.a).
- **Reimpresión.** El botón "Reimprimir ticket" del panel del operador (pedidos Enviado, Listo para recoger o Entregado) imprime el mismo ticket con la banda **"*** REIMPRESIÓN ***"**.
- **Hoja de prueba de la impresora** (administrador). Imprime los estilos (negrita, doble tamaño, banda invertida) y la misma frase con acentos y ñ en 3 páginas de códigos, para elegir la que se ve bien.
- **Vista previa** del ticket en texto de 48 columnas, para revisar sin impresora o imprimir desde el navegador (7.2.19).

El ticket sigue el diseño 5.4 (PNG 5 y 6):
- **Encabezado:** nombre del restaurante a doble tamaño, dirección y teléfono.
- **Pedido:** "PEDIDO EN LÍNEA", folio con fecha y hora de impresión, y artículos con cantidad, producto y variante, importe, indicaciones (`> ...`) y precio unitario si la cantidad es mayor que 1.
- **Pago:** subtotal, costo de envío (solo si hay), TOTAL a doble tamaño y método de pago.
- **Entrega:** banda "ENTREGA A DOMICILIO" con cliente, teléfono, dirección y referencias, o banda "RECOGER EN EL LOCAL" con el nombre del cliente a doble tamaño.
- **Pie:** "Comprobante de pedido. No es factura." y "¡Gracias por tu pedido!", con corte de papel.

## Endpoints

| Método y ruta | Rol (matriz 4.2) | Respuesta |
| --- | --- | --- |
| `POST /api/v1/operacion/pedidos/{folio}/estado` (de Aldo) | OPERADOR | Al pasar a Enviado o Listo para recoger devuelve `{ folio, estado, ticket: { impreso, impresora, aviso } }`. En los demás estados, la respuesta de siempre. |
| `GET /api/v1/operacion/pedidos/{folio}/ticket?reimpresion=false` | OPERADOR | 200 `{ texto, lineas: [{ texto, alineacion, negrita, doble, invertido }] }`. |
| `POST /api/v1/operacion/pedidos/{folio}/ticket` con cuerpo opcional `{ "reimpresion": true }` | OPERADOR | 200 `{ folio, impresora, fecha, reimpresion }`. |
| `POST /api/v1/impresion/caja/prueba` | ADMINISTRADOR | 200 con la impresora usada. |

Los dos primeros endpoints del ticket solo ven los pedidos del restaurante del operador, igual que el resto de la operación: el restaurante sale del método de pago del pedido y el operador debe ser un empleado activo con rol OPERADOR. La hoja de prueba usa el restaurante del administrador.

Errores (ProblemDetails, documentados en Swagger):

| Código | Cuándo |
| --- | --- |
| 400 | Folio de más de 30 caracteres o campos no permitidos en el cuerpo. |
| 401 | Sin sesión. |
| 403 | Otro rol. |
| 404 | El pedido no existe o es de otro restaurante. |
| 409 | El pedido está cancelado, o no hay una impresora de caja activa. |
| 503 | La impresora no respondió (apagada, sin red o IP equivocada) o la base de datos no está disponible. |

## Módulo de impresión (`backend/infrastructure/LoyOrder.Infrastructure.Impresion`)

- `EscPos` convierte un `DocumentoImpresion` (capa Application, también lo usará la 7.2.20 para las comandas) en comandos ESC/POS:
  - `ESC @` para iniciar y `ESC t n` para la página de códigos.
  - `ESC a` para la alineación, `ESC E` para la negrita, `GS !` para el doble tamaño y `GS B` para el blanco sobre negro.
  - Al final, avance de 4 líneas y `GS V 66 0` para el corte parcial.
- **Seguridad:** los caracteres de control del texto (nombre, dirección o indicaciones) se cambian por espacios. Así un cliente no puede meter comandos a la impresora, como `ESC p`, que abre el cajón.
- `ImpresoraTcp` envía por TCP a `direccion_ip:Impresion:Puerto` con tiempo límite. Si la impresora no responde lanza `ImpresoraNoDisponibleException`, que la API convierte en 503 (o en aviso, en el despacho). El registro solo guarda el nombre de la impresora y el tipo de error.
- No usa paquetes de NuGet.

## Configuración

| Clave | Para qué |
| --- | --- |
| `Impresion:Puerto` | Puerto RAW de la impresora. 9100 por defecto. |
| `Impresion:PaginaCodigos` | `PC850`, `PC858` (por defecto) o `WPC1252`. Se elige con la hoja de prueba. |
| `Impresion:TiempoEsperaSegundos` | De 1 a 60. 5 por defecto. |
| `Ticket:Direccion` y `Ticket:Telefono` | Opcionales. La tabla `restaurante` aún no tiene dirección ni teléfono (CU-08). Si están vacías, esas líneas no se imprimen. |

La fecha del ticket usa la zona horaria de los pedidos (`Pedidos:ZonaHoraria`, `America/Mexico_City`).

**Impresora de caja.** La pantalla de estaciones (7.2.21 de Gabriel) administra solo impresoras de cocina. La de caja se da de alta con SQL, como administrador de la base:

```sql
INSERT INTO impresora (id_restaurante, nombre, direccion_ip, tipo, activa) VALUES (1, 'Caja', '192.168.123.100', 'Caja', true);
```

No hay migración: la API solo lee `pedido`, `detalle_pedido`, `cliente`, `direccion`, `metodo_pago`, `empleado`, `restaurante` e `impresora`.

## Prueba con la impresora térmica (Easytime POS891, Ethernet)

1. Conectar la impresora con el cable de red al mismo router que la computadora donde corre la API.
2. Imprimir su hoja de autoprueba para ver su IP: apagarla, mantener presionado el botón de avance de papel y encenderla.
3. Dar de alta la impresora de caja con su IP (SQL de arriba).
4. Arrancar la API y entrar a Swagger (`http://localhost:5080/swagger`) con una cuenta de ADMINISTRADOR. Llamar `POST /api/v1/impresion/caja/prueba`.
5. En la hoja impresa, ver qué bloque (PC850, PC858 o WPC1252) muestra bien "áéíóú ñ ¿Sí? ¡Listo!". Si no es PC858, arrancar la API con `$env:Impresion__PaginaCodigos = "PC850"` (o la que salió bien).
6. Con una cuenta de OPERADOR, despachar un pedido desde el panel: debe salir el ticket. Luego probar "Reimprimir ticket".

**La API debe estar en la misma red física que la impresora.** En el despliegue de 3 equipos por Tailscale, la impresora solo se alcanza desde la API que esté en el mismo lugar que ella.

## Pruebas

```powershell
dotnet run --project tests\unit\LoyOrder.Application.Tests
dotnet run --project tests\unit\LoyOrder.Infrastructure.Impresion.Tests
dotnet run --project tests\integration\LoyOrder.Api.Tests
$env:LOYORDER_PRUEBAS_ADMIN = "Host=localhost;Username=postgres;Password=<tu contraseña>"
dotnet run --project tests\integration\LoyOrder.Persistence.Tests
cd frontend; npm test
```

| Proyecto | Pruebas | Qué se agregó |
| --- | ---: | --- |
| `LoyOrder.Application.Tests` | 43 | 8: 48 y 24 columnas con textos largos, ticket a domicilio igual al diseño, recoger con reimpresión y sin envío, caracteres de control, impresión en la caja del restaurante del operador, pedido cancelado, ajeno o sin impresora, despacho con impresora caída y hoja de prueba del administrador. |
| `LoyOrder.Infrastructure.Impresion.Tests` (nuevo) | 9 | Bytes ESC/POS (inicio, página de códigos, estilos y corte), acentos y ñ en PC858 y WPC1252, texto con `ESC p` bloqueado, hoja de prueba, configuración inválida, envío real por TCP a un servidor local, impresora apagada e impresora que no responde dentro del tiempo límite. |
| `LoyOrder.Api.Tests` | 154 | 27: vista previa, impresión, reimpresión con marca, 400, 404, 409 (cancelado y sin impresora), 503, despacho con impresora caída (estado guardado) y con impresora (imprime solo al despachar), roles de la matriz 4.2 y 401. |
| `LoyOrder.Persistence.Tests` | +1 grupo | Datos reales del pedido, operador de otro restaurante, administrador e inactivo sin acceso, impresora de caja activa, restaurante del administrador y vista previa. |
| Frontend (`npm test`) | 52 | 2: aviso del ticket al despachar y reimpresión sin sesión. |

## Límites de esta entrega

- La impresora de caja se da de alta con SQL; no hay pantalla para ella.
- No se guarda un historial de impresiones del ticket (la tabla `comanda` es para cocina). El original sale al despachar y el operador reimprime a mano.
- Si el operador repite "Marcar Enviado" sobre un pedido que ya estaba despachado, el ticket se vuelve a imprimir.
- La dirección y el teléfono del restaurante vienen de la configuración hasta que el CU-08 los guarde en la base.
- La página de códigos correcta depende del modelo de impresora; se confirma con la hoja de prueba.
- Las comandas por estación (normal, adicional, anulación y prioritaria) son la 7.2.20; reutilizarán este módulo.
