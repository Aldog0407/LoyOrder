# Pedidos persistidos, operación y notificaciones — 25 de noviembre de 2026

## Resultado y alcance

El checkout React crea pedidos mediante la API y conserva dirección, artículos, precios, estado inicial e historial en PostgreSQL. El historial del cliente consulta sus pedidos guardados. El operador consulta y cambia los estados de los pedidos de su restaurante. Cada evento genera un aviso persistido que un proceso de fondo puede enviar por SMTP.

Este avance integra el checkout (7.2.6), el historial y repetición (7.2.14), el seguimiento (7.2.10), la calificación (7.2.18), los avisos (7.2.15) y la parte de estados del despacho (7.2.23). La impresión física y el registro confirmado del recibo en Loyverse siguen pendientes. Elegir un método de pago no cobra dinero.

## Actualizar una base existente

Conservar los datos existentes. En pgAdmin, abrir cada archivo completo sobre la base `loyorder` y ejecutarlo íntegro, en este orden, deteniéndose ante el primer error.

1. `database/scripts/LoyOrderSincronizacion.sql`, si aún no se aplicó con los cambios de Carlos.
2. `database/scripts/LoyOrderNotificaciones.sql`.
3. `database/scripts/LoyOrderCheckout.sql`.

Estas migraciones se pueden repetir. Se presupone que ya se aplicaron el esquema, los objetos y permisos de Gabriel y `LoyOrderAutenticacionCorreo.sql`. No ejecutar nuevamente el esquema ni la semilla sobre una base en uso. Si la ventana de consultas tiene una transacción abortada, ejecutar `ROLLBACK;` antes de abrir la migración completa. La comprobación `/health/ready` detecta la falta de las nuevas columnas. El script de preparación de despliegue también aplica estas migraciones; sus pasos posteriores de red y cuentas corresponden al despliegue de tres equipos, no son necesarios para actualizar únicamente las tablas locales.

`pedido` añade `id_operacion` y `solicitud_hash`, con unicidad por cliente y operación. Los pedidos anteriores conservan valores NULL. Se ajusta hacia arriba la secuencia para aceptar semillas con identificadores explícitos. `notificacion` añade `id_historial`, `intentos`, `proximo_intento` y `ultimo_error`. Un índice único impide encolar dos veces el mismo evento. Se reutilizan las tablas `direccion`, `detalle_pedido`, `historial_estado`, `empleado` y `registro_venta` existentes.

## Contrato de la API

Todas las rutas siguientes requieren sesión. Los roles son exclusivos y el servidor obtiene la identidad desde el JWT.

| Método y ruta | Rol | Resultado |
| --- | --- | --- |
| GET `/api/v1/pedidos/opciones` | CLIENTE | Restaurante, horario, costo de envío, disponibilidad de apertura y métodos de pago con ID. |
| POST `/api/v1/pedidos` | CLIENTE | 201 con `idPedido`, `folio`, `fechaCreacion`, `estado`, `subtotal`, `costoEnvio` y `total`. |
| GET `/api/v1/pedidos?pagina=1&tamano=20` | CLIENTE | Hasta 20 pedidos propios por defecto; tamaño máximo 50. |
| GET `/api/v1/pedidos/{folio}` | CLIENTE | Detalle propio y eventos guardados; 404 si pertenece a otra cuenta. |
| POST `/api/v1/pedidos/{folio}/calificacion` | CLIENTE | Califica un pedido propio entregado; conserva las reglas anteriores. |
| GET `/api/v1/operacion/pedidos` | OPERADOR | Hasta 50 pedidos de su restaurante, activos primero. |
| POST `/api/v1/operacion/pedidos/{folio}/estado` | OPERADOR | Estado actualizado mediante el procedimiento de Gabriel. |

Ejemplo de confirmación para recolección. Sustituir los ID y precios por los obtenidos del catálogo y opciones actuales. Generar un UUID nuevo para una compra nueva y conservarlo para reintentar esa misma compra.

```json
{
  "idOperacion": "175dc922-688f-4d73-9603-f86acbe143ea",
  "tipoEntrega": "Recolección",
  "idMetodoPago": 1,
  "totalEsperado": 100.00,
  "articulos": [{"idVariante": 1, "cantidad": 1, "precioEsperado": 100.00}],
  "direccion": null
}
```

Para `Domicilio`, enviar `direccion` con `calle`, `numero`, `referencias` opcional, `latitud` y `longitud`. Se requieren calle de hasta 150 caracteres, número de hasta 20 y coordenadas válidas. Las referencias admiten hasta 1000 caracteres. No enviar identidad, rol, estado ni importes definitivos calculados por el cliente. Se rechazan campos adicionales. El servidor valida 1–100 variantes distintas, de 1–20 unidades por variante, importes positivos con hasta dos decimales y las capacidades de las columnas SQL.

El servidor calcula precios y envío desde PostgreSQL. Un cambio de precio, cierre del restaurante, falta de stock o reutilización del UUID con datos diferentes devuelve 409. Datos inválidos devuelven 400; sesión ausente 401; rol incorrecto 403; dependencia no disponible 503. El horario se interpreta en `Pedidos__ZonaHoraria`, por defecto `America/Mexico_City`; es una decisión de esta implementación que el equipo puede configurar. Se admite horario nocturno. Apertura y cierre iguales se consideran cerrado.

La compra se guarda en una transacción. Reenviar el mismo UUID y contenido devuelve el mismo pedido, incluso si el restaurante cerró después del primer envío. El folio se genera en el servidor como `LY-00000002`. Una respuesta incierta por red permite reintentar sin duplicar el pedido. React conserva el intento mientras mantiene ese estado de sesión y consulta también el historial persistido.

Si una variante controla inventario, se consulta nuevamente Loyverse antes de guardar. Sin conexión, vínculo o existencias suficientes, la compra se bloquea. Esta consulta no reserva existencias ni descuenta stock; otra venta concurrente puede modificarlo. La comprobación contra la tienda real está pendiente del token de Carlos. Las pruebas locales emplean un origen controlado y no demuestran integración con inventario real.

El cambio de estado recibe `{"estado":"En preparación","motivo":null}`. El procedimiento existente impone las transiciones. Para recolección, el recorrido es Recibido → En preparación → Listo para recoger → Entregado. Para domicilio se utiliza Enviado. Cancelar exige motivo. Se valida que el empleado activo pertenezca al restaurante del pedido. Un cambio válido inserta historial y aviso en la misma transacción. Repetir el estado actual no duplica el evento. Al entregar, el procedimiento deja `registro_venta` pendiente; esto no prueba que Loyverse haya recibido la venta.

## Correo de pedidos

La cola de pedidos se guarda en PostgreSQL aunque el envío esté deshabilitado. Para procesarla, conservar la configuración SMTP y las claves del flujo de autenticación y añadir, en la misma PowerShell donde se inicia la API:

```powershell
$env:Notificaciones__Habilitado='true'
dotnet run --project backend/presentation/LoyOrder.Api
```

Se requiere también `CorreoAutenticacion__Habilitado='true'` y SMTP configurado. Sin la opción de notificaciones, los avisos quedan pendientes. El destinatario debe tener cuenta activa y correo verificado. El trabajador procesa filas bloqueadas con `SKIP LOCKED`, registra el resultado, espera entre reintentos y limita cada aviso a cinco intentos. Solo procesa eventos nuevos con `id_historial`; no envía las notificaciones antiguas de semillas. Un fallo de SMTP conserva el pedido.

Los avisos fallidos con menos de cinco intentos se retoman tras reiniciar la API. El envío tiene semántica de al menos una vez: si SMTP acepta el correo pero se pierde la confirmación antes de guardar el resultado, puede repetirse. El identificador del mensaje permanece estable, pero no garantiza que todos los proveedores eliminen duplicados. El quinto fallo deja el aviso en Fallida para revisión. No se expone la contraseña SMTP en respuestas ni registros.

## Evidencia y pendientes

- 118 comprobaciones de API, 32 de aplicación y 24 del cliente Loyverse aprobadas.
- 15 grupos de integración con PostgreSQL 18 temporal y rol restringido aprobados, incluidos concurrencia, transacciones, reintentos, permisos por restaurante y fallos de SMTP.
- 42 pruebas de frontend aprobadas. Compilación de producción y revisión estática aprobadas. Vite conserva la advertencia del paquete grande por Mapbox. La solución .NET compila con cero errores y cero advertencias.
- Recorrido visual comprobado en React y PostgreSQL temporal: registro/inicio de sesión de prueba, menú desde la API, compra por recolección, historial tras volver a iniciar sesión, operación hasta Entregado y calificación de 5/5 guardada.
- Tres avisos de estado recibidos por un receptor SMTP local durante el recorrido. No se enviaron a buzones reales en esta prueba.
- Pendiente aplicar estas nuevas migraciones y probar el recorrido en el PostgreSQL de Aldo, comprobar avisos de pedidos con Gmail, obtener el token de Carlos y validar tienda, impresión y venta real. La verificación y recuperación por Gmail ya fueron comprobadas por Aldo el 24 de noviembre.
