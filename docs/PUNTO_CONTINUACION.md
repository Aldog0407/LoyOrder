# Punto de continuación · LoyOrder · 26 de noviembre de 2026

## Carpeta y Git

Trabajar en `LoyOrder-Git`, sobre main. No crear ramas. El commit `fa178f1` contiene pedidos, operación y avisos; `62ec8a4` registra y reconcilia ventas en Loyverse. El siguiente commit de esta sesión cierra OpenAPI/Swagger. Antes de continuar, consultar `git status -sb` y `git log -3 --oneline`. El 26 es el último día de trabajo y el 27 es la exposición.

## Comprobado

- Aldo probó registro, verificación y recuperación de contraseña por Gmail.
- Su catálogo ya sincroniza con la cuenta de pruebas de Loyverse.
- Creó y verificó la cuenta `loyorder.equipo5+operador@gmail.com` y le asignó OPERADOR en su restaurante desde pgAdmin.
- Confirmó un pedido del catálogo sincronizado, lo preparó y entregó desde la interfaz, vio la venta Confirmada con recibo y recibió los avisos por correo.
- Las pruebas aisladas comprobaron transacciones, permisos, reintentos y reconciliación. No se deben confundir sus datos temporales con la base de Aldo.
- Swagger genera los contratos y permite probar GET /health/live. La suite de API tiene 127 comprobaciones aprobadas; aplicación 32, adaptador Loyverse 32, PostgreSQL 17 grupos y frontend 42. Esos números no representan cobertura.

## Arranque

Abrir dos PowerShell en el proyecto. La API se ejecuta con `dotnet run --no-launch-profile --project backend/presentation/LoyOrder.Api`; el frontend con `cd frontend` y `npm run dev`. Direcciones habituales: API `http://localhost:5080`, frontend `http://localhost:5173` y Swagger `http://localhost:5080/swagger`.

Si la PowerShell es nueva, cargar localmente conexión PostgreSQL, configuración SMTP, clave de códigos y token de Loyverse. No guardar secretos en Git ni pedir que se envíen por chat. Para los recorridos completos, configurar `CorreoAutenticacion__Habilitado=true`, `Notificaciones__Habilitado=true` y `Ventas__Habilitado=true` después de configurar sus dependencias. El token de Loyverse se acepta por `LOYVERSE_ACCESS_TOKEN` o `Loyverse__Token`. Las variables del proceso se pierden al cerrar esa ventana. Reiniciar la API requiere volver a iniciar sesión.

La base local ya recibió las migraciones de sincronización, autenticación por correo, notificaciones, checkout y ventas. No volver a ejecutar el esquema ni las semillas. Comprobar `/health/ready` antes del recorrido.

Se abrió temporalmente el horario para probar de madrugada. El horario original a restaurar es 09:00–22:00; no asumir que ya se restauró sin confirmación de Aldo.

## Prioridad pendiente

1. Medir cobertura y generar reporte reproducible para 7.3.10. No inventar porcentajes a partir del número de pruebas.
2. Reunir evidencia y un guion corto del recorrido de exposición.
3. Despacho 7.2.23: los estados y avisos funcionan; la impresión sigue pendiente. Aldo confirmó el 26 que el módulo ESC/POS de Carlos todavía no está disponible. No marcar impresión ni despacho completo como terminados.
4. Para venta con costo de envío, vincular `Ventas__IdVarianteEnvio` a una variante real y comprobar ese recorrido. El flujo confirmado con Loyverse fue Recolección.
5. Validar el despliegue entre equipos con sus responsables. No prometer pruebas físicas sin impresora disponible.

Ver `ACTIVIDAD_7_2_13.md`, `ACTIVIDAD_7_4_1.md` y los estados históricos de `docs/README.md`.
