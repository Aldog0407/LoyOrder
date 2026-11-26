# 7.2.13 · Registrar venta en Loyverse al cerrar el pedido

## Comportamiento

Se reutiliza `registro_venta`, que el procedimiento de Gabriel crea al marcar Entregado. Un trabajador de la API procesa esos registros, conserva el folio en `order`, la modalidad en `note` y envía exactamente un pago a Loyverse. El resultado se guarda con el número de recibo, que es el identificador único que devuelve esa API. `id_recibo_loyverse` y `numero_recibo` conservan ese mismo valor. No existe un UUID de recibo independiente en este contrato.

Solo se procesan pedidos Entregados. Cancelar o crear un pedido no registra una venta. El pedido permanece Entregado aunque Loyverse no responda. Este registro no ejecuta un cobro bancario.

## Instalación y configuración

Aplicar completo `database/scripts/LoyOrderVentas.sql` sobre la base existente. Es una migración repetible que añade `iniciado_en`, `proximo_intento`, `proceso` y `solicitud` a `registro_venta`; conserva los datos anteriores. El script de preparación de despliegue también la incluye.

Configurar el token fuera del repositorio mediante `LOYVERSE_ACCESS_TOKEN` o `Loyverse__Token`. Para activar el trabajador, en la PowerShell de la API:

```powershell
$env:Ventas__Habilitado='true'
dotnet run --no-launch-profile --project backend/presentation/LoyOrder.Api
```

Por defecto permanece deshabilitado. Al habilitarlo, `/health/ready` comprueba que la migración exista. El trabajador revisa pendientes cada cinco segundos cuando no hay trabajo. Al activarlo puede registrar todos los pedidos Entregados pendientes de la base, por lo que debe apuntar a la cuenta y tienda correctas.

La tienda, el método de pago y cada variante deben tener identificadores reales de la misma cuenta. Antes del primer envío se comprueba su existencia y disponibilidad remota. Los registros creados con los identificadores `PRUEBA-LOCAL-*` no se envían. Los precios unitarios provienen de la compra guardada, no del precio actual del catálogo. Se envían sin impuestos adicionales, conforme al total que maneja actualmente LoyOrder. La comparación del recibo impide dar por confirmada una venta cuyo total difiera. Si el restaurante necesita desglose fiscal adicional, debe definirse e integrarse en el cálculo del checkout y su contrato.

Para pedidos con costo de envío, configurar `Ventas__IdVarianteEnvio` con el UUID de una variante real destinada a ese concepto en la misma tienda. Se envía como una línea de cantidad uno y precio igual al envío guardado. Sin ese vínculo, el registro queda Fallido; no se omite el envío ni se registra un total incompleto. El recorrido real comprobado en esta entrega es Recolección, sin costo de envío.

## Estados, reintentos y duplicados

- **Pendiente**: todavía no se intentó el envío.
- **En proceso**: se guardó una reclamación exclusiva y una copia de los datos antes de hablar con Loyverse.
- **Confirmado**: se guardó un recibo que coincide con tienda, folio, pago, artículos, cantidades, precios, total y correlación de operación.
- **Fallido**: faltan vínculos, falló una consulta previa o la API rechazó explícitamente el POST con un error definitivo. Admite hasta cinco intentos automáticos con espera creciente.
- **Por verificar**: se perdió la respuesta del POST, hubo una respuesta ambigua o el recibo no coincide. El sistema solo consulta; no vuelve a publicar esa venta automáticamente. Tras veinte verificaciones se requiere revisión.

La reclamación se guarda en PostgreSQL y usa `FOR UPDATE SKIP LOCKED`. Un token por trabajo evita que un proceso antiguo sobrescriba a otro. Si En proceso dura más de tres minutos, se recupera únicamente para consultar el recibo. La copia de solicitud y su correlación permanecen guardadas durante el reintento.

La búsqueda usa los filtros `store_id`, `order` y `source=LoyOrder`, recorre todas las páginas y detecta múltiples coincidencias. Se añade a la nota un identificador estable de operación para evitar asociar accidentalmente un pedido con una venta antigua de igual folio e importe. Ante colisiones se solicita revisión. Loyverse no aporta una clave de idempotencia para este POST; la protección implementada conserva la incertidumbre y evita reenvíos automáticos después de una respuesta ambigua. No se promete una transacción distribuida ni exactamente una entrega ante cambios externos al sistema.

El folio admite hasta 20 caracteres en la API de Loyverse, aunque la columna local permita 30. Se valida ese límite antes de enviar. Los folios habituales `LY-00000002` son compatibles.

## Interfaz y API

El listado de operación añade `estadoVenta`, `numeroRecibo` y `errorVenta`. La cuenta OPERADOR ve el resultado debajo de los pedidos Entregados.

`POST /api/v1/operacion/pedidos/{folio}/venta/reintentar` requiere OPERADOR activo del restaurante del pedido. Devuelve 202 al solicitar procesamiento, 401 sin sesión, 403 con otro rol, 404 para un pedido inexistente o ajeno y 409 si ya está en proceso. Un confirmado no se vuelve a enviar. Fallido reinicia su contador; Por verificar conserva ese estado y solicita únicamente otra consulta. La pantalla presenta “Reintentar registro de venta” o “Consultar recibo otra vez”, respectivamente.

## Evidencia

Se verificó el acceso a tiendas, pagos y artículos de la cuenta de pruebas. Aldo confirmó que el catálogo sincronizado aparece en su frontend.

Se registraron dos ventas técnicas de un Helado de vainilla de $40 cada una en la cuenta de pruebas, con los recibos `0004` y `0005`. Los folios fueron `LOP-67577eb05890` y `LOP-018878fdf41f`. El segundo incorpora la correlación estable de operación. Se usó una base PostgreSQL temporal separada del entorno de Aldo y el rol restringido de aplicación. La primera reconciliación volvió a asociar el recibo `0004` existente y una consulta remota confirmó una sola coincidencia para ese folio. Dos intentos previos con folios técnicos demasiado largos fueron rechazados con 400 y no crearon recibos.

Las suites incluyen pruebas de envío con precios guardados, un pago, errores definitivos y ambiguos, no reenvío incierto, correlación, paginación, vínculos ficticios, reclamación concurrente, recuperación tras interrupción, permisos del operador y confirmación persistida. Resultado final de las suites: 123 comprobaciones de API, 32 del cliente/adaptador Loyverse, 17 grupos de integración PostgreSQL y 42 de frontend aprobados. La solución .NET compila con cero errores y cero advertencias, y React pasa revisión estática y compilación. Vite conserva el aviso previo de tamaño del paquete por Mapbox. No se ha medido cobertura. Aldo confirmó que llegaron al buzón del cliente los avisos del pedido y de sus cambios de estado. El aviso de cancelación incluye el motivo del operador. Aldo creó y verificó una cuenta nueva de operador, completó un pedido del catálogo sincronizado desde su interfaz y confirmó que aparece Confirmado con número de recibo en su propio entorno. Falta comprobar el costo de envío con una variante real configurada. La impresión pertenece a la integración de despacho y no queda cubierta por esta actividad.

Referencia del contrato del proveedor: https://developer.loyverse.com/docs/ y su especificación https://developer.loyverse.com/docs/API-Reference__v1.0.yaml, consultadas el 26 de noviembre de 2026.
