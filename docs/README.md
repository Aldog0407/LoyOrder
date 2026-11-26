# Documentación

Guardar aquí la planeación, diagramas SOHDM, decisiones técnicas y documentación de la API.

- `DESPLIEGUE.md`: despliegue temprano en 3 equipos (frontend, API y BD) por VPN Tailscale (actividad 7.1.7).
- `ACTIVIDAD_7_2_3.md`: sincronización de catálogo e inventario desde Loyverse (CU-10, req. 5 y 6).

## Avance de Aldo al 24 de noviembre de 2026

Se integraron los avances de checkout y Mapbox junto con `b77a566`, que incorpora sincronización de catálogo e inventario y la guía de despliegue del equipo.

| Actividad | Estado comprobado |
| --- | --- |
| 7.1.3 API por capas | Conservada y compilada junto con los módulos actuales. |
| 7.2.1 Autenticación y roles | Sesiones renovables, roles y acceso protegido. Reparado el borrador de configuración eliminado durante la integración. |
| 7.2.2 Recuperación y correo | Pantallas React conectadas al backend. Aldo confirmó con Gmail y su PostgreSQL local el registro, la verificación, el inicio de sesión y la recuperación completa, incluyendo el rechazo de la contraseña anterior. |
| 7.2.10 Seguimiento | Consulta persistida por folio, artículos y eventos, restringida al propietario. Pendiente unirla al alta de pedidos del checkout. |
| 7.2.18 Calificación | Persistencia por cliente sobre pedidos Entregados, una vez y con reintentos seguros. Pendiente el recorrido desde el checkout persistido. |
| 7.2.13 Venta en Loyverse | Pendiente integrar el cierre, los IDs reales de catálogo y pago, y la confirmación/reconciliación del recibo. |
| 7.2.15 Notificaciones de pedidos | Pendiente conectar los eventos de pedido con notificaciones persistidas y reintentos. El correo de recuperación es un flujo diferente. |
| 7.2.23 Despacho | Pendiente conectar el estado del pedido, la impresión de Carlos y la notificación. |
| 7.3.1 Pruebas | Después de integrar los cambios del equipo pasan 79 comprobaciones de API, 25 de frontend, 19 de aplicación y 24 del cliente Loyverse. La suite HTTP temporal desactiva la limitación de frecuencia para ejecutar los recorridos juntos. La aplicación conserva sus límites. Las comprobaciones anteriores de seguimiento y calificación utilizaron PostgreSQL 18 temporal con rol restringido. No se ha medido cobertura. |
| 7.4.1 Documentación OpenAPI | Pendiente. Los contratos de integración actuales están en los README de backend y frontend. |

Para la revisión del viernes 27 de noviembre, la integración prioritaria es crear pedidos reales desde el checkout y operar sus estados. El backend de seguimiento consulta el esquema actual de Gabriel. Los folios locales UUID del checkout tienen 39 caracteres y la columna `pedido.folio` admite 30, por lo que el alta definitiva debe devolver un folio compatible. La generación y reserva del folio definitivo se debe ejecutar en el servidor al guardar el pedido.

La compilación de React avisa de un paquete JavaScript grande al incluir Mapbox. La optimización de carga está pendiente. El despliegue, impresión física, catálogo real, pagos y venta en Loyverse aún requieren integración. Este avance no permite asignar un porcentaje de terminación global únicamente a partir del número de pantallas.


## Avance del 25 de noviembre de 2026

El checkout ahora crea pedidos persistidos y el historial consulta PostgreSQL. El operador acepta, prepara, despacha y entrega pedidos de su restaurante. Los cambios de estado generan avisos persistidos con reintentos SMTP. Se comprobó visualmente el recorrido hasta la calificación y se probaron concurrencia, permisos y fallos de dependencias con PostgreSQL temporal.

El estado del 24 de noviembre que aparece arriba es histórico. Las conexiones de checkout, historial, estados y avisos que figuraban pendientes ya están implementadas. La impresión física y la venta confirmada en Loyverse continúan pendientes. La prueba de stock real espera el token de Carlos. También falta aplicar las migraciones nuevas y verificar este recorrido en el entorno local del equipo.

Contrato, migraciones y evidencia en [Pedidos del 25 de noviembre](ACTIVIDAD_PEDIDOS_2026_11_25.md).


## Avance adicional de la noche del 25 al 26 de noviembre

Se integró el registro persistido de ventas Entregadas con Loyverse, con reclamación exclusiva, copia de datos, reconciliación y reintentos por operador. La cuenta real de pruebas devolvió recibos confirmados; Aldo ya verificó la sincronización del catálogo. Ver [actividad 7.2.13](ACTIVIDAD_7_2_13.md). Las menciones anteriores a esta actividad pendiente describen el estado previo. El jueves 26 es el último día de trabajo previsto por Aldo y el viernes 27 se reserva para exponer. Siguen pendientes Swagger, evidencia de correos en su entorno, cobertura y la integración física de despacho.

Aldo confirmó el recorrido completo por la pantalla de operación y el recibo de Loyverse en su PostgreSQL local durante la prueba del 26 de noviembre. La cuenta de operador se creó mediante registro y verificación y luego se asignó al restaurante desde pgAdmin.

Aldo confirmó también la recepción en Gmail de los avisos del pedido y los cambios de estado en su recorrido local.
