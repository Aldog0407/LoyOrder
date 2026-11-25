# Actividad 7.2.3 — Sincronización de catálogo e inventario desde Loyverse (req. 5 y 6, CU-10)

Entregable: funcionalidad de sincronización de catálogo e inventario desde Loyverse.

## Qué hace

La API trae de Loyverse las categorías, los artículos con sus variantes y precios, y las existencias de la tienda vinculada, y los guarda en `categoria`, `producto` y `variante_producto`. Loyverse sigue siendo la fuente de verdad: LoyOrder solo lee de Loyverse y nunca escribe en él.

Se puede ejecutar de dos formas:

- **Automática.** Una tarea en segundo plano corre cada `restaurante.intervalo_sincronizacion` minutos (de 1 a 15; 2 por defecto). Solo se activa si hay token configurado.
- **Manual.** El administrador usa "Sincronizar ahora": `POST /api/v1/sincronizacion`.

Las dos formas comparten un candado, así que nunca corren dos sincronizaciones al mismo tiempo. Si se pide una mientras otra está en curso, la API responde 409.

## Reglas de la sincronización

| Situación | Qué hace LoyOrder |
| --- | --- |
| Primera vez, o la última completa tiene más de 24 h | Trae todo el catálogo. |
| Ya hubo una sincronización correcta en las últimas 24 h | Trae solo los artículos modificados desde el inicio de esa corrida (`updated_at_min`). |
| El restaurante no tiene tienda y la cuenta tiene una sola | La vincula automáticamente (`restaurante.id_tienda_loyverse`). Con 0 o con más de 1 tienda, responde 409 y pide configurarla. |
| Artículo sin categoría en Loyverse | Lo pone en la categoría "Sin categoría" del restaurante. |
| Variante con precio variable o no disponible en la tienda | Se guarda con `disponible = false` y precio 0. Así no se puede pedir en línea. |
| Artículo o variante eliminados en Loyverse | Se marcan `disponible = false`. **No se borran filas**: los pedidos anteriores las siguen usando. |
| Categoría eliminada en Loyverse | Se marca `activo = false`. |
| Descripción con HTML | Se guarda como texto plano. |
| Existencias de variantes que no están en el catálogo | Se ignoran. |
| Categorías sin estación de preparación asignada | Se avisa en el resumen (CU-10 FA 4.a). La sincronización sigue. |

Primero se descarga todo y después se guarda en **una sola transacción**. Si Loyverse falla a la mitad, la base de datos no queda con un catálogo a medias.

Cada corrida deja dos filas en `sincronizacion`, una de tipo `Catálogo` y otra de tipo `Inventario`. Cada fila tiene estado `En proceso`, `Correcta` o `Fallida`, sus fechas y un resumen en `resumen` (jsonb) con los conteos. Cuando una corrida falla, `detalle_error` guarda un mensaje fijo, por ejemplo "El token de Loyverse no es válido" o "No fue posible conectar con Loyverse". Nunca guarda el token ni las respuestas de Loyverse o de PostgreSQL.

## Endpoints (solo ADMINISTRADOR)

| Método y ruta | Respuesta |
| --- | --- |
| `POST /api/v1/sincronizacion` | 200 con el resumen: categorías, productos y variantes nuevos, actualizados y eliminados; existencias actualizadas; si fue completa; aviso de categorías sin estación. |
| `GET /api/v1/sincronizacion` | 200 con las últimas 20 corridas del historial. |

Códigos de error: 401 sin sesión. 403 para cliente u operador. 409 si falta el token, si falta la tienda o si ya hay una sincronización en curso. 502 si Loyverse rechaza el token. 503 si no se puede conectar con Loyverse o con la base de datos.

## Configuración

El token **nunca** va en `appsettings.json`, en el repositorio ni en Trello. Usen siempre el token de la **cuenta de prueba** de Loyverse, nunca el de la cuenta real del restaurante.

| Clave | Para qué |
| --- | --- |
| `Loyverse:Token` (o variable `Loyverse__Token` / `LOYVERSE_ACCESS_TOKEN`) | Token de acceso de la cuenta de prueba. Sin él, la sincronización automática no se activa y la manual responde 409. |
| `Sincronizacion:Automatica` | `true` por defecto. `false` apaga la tarea programada. |
| `Sincronizacion:IdRestaurante` | Opcional. Si no se indica, se usa el primer restaurante registrado. |
| `Sincronizacion:EsperaInicialSegundos` | Espera antes de la primera corrida automática. 15 por defecto. |

## Instalación en la base de datos

Correr una vez, después del esquema, de los objetos y de la migración de autenticación por correo:

```powershell
psql -U postgres -h localhost -d loyorder -f database\scripts\LoyOrderSincronizacion.sql
```

Solo agrega la columna `resumen` a `sincronizacion` y se puede repetir sin problema. `deploy\bd\preparar_bd.ps1` ya la corre. El usuario `loyorder_app_user` ya tenía INSERT y UPDATE sobre las tablas del catálogo, y sigue sin DELETE.

## Demostración en una laptop

1. Guardar el token de la cuenta de prueba (una sola vez; queda fuera del repositorio):
   ```powershell
   dotnet user-secrets set "Loyverse:Token" "<token de la cuenta de prueba>" --project backend\presentation\LoyOrder.Api
   ```
2. Arrancar la API como en `docs/ACTIVIDAD_7_2_1.md` ("Arrancar la API para el navegador"), con `ConnectionStrings__LoyOrder` apuntando a la base local.
3. Después de unos 15 segundos, en la consola debe aparecer "Sincronización con Loyverse correcta: ... productos nuevos ...".
4. Para la sincronización manual: iniciar sesión como administrador (`POST /api/v1/auth/login`) y llamar `POST /api/v1/sincronizacion` con el `accessToken`. Se puede hacer desde `LoyOrder.Api.http` en VS Code o en Visual Studio.
5. Comprobar en la base de datos:
   ```sql
   SELECT tipo, estado, fecha_inicio, resumen FROM sincronizacion ORDER BY id_sincronizacion DESC LIMIT 4;
   SELECT p.nombre, v.nombre, v.precio, v.existencias, v.disponible
   FROM producto p JOIN variante_producto v USING (id_producto) ORDER BY p.nombre;
   ```
6. En el Back Office de la cuenta de prueba, cambiar un precio o eliminar un artículo, esperar el intervalo y repetir la consulta.

En el despliegue de 3 equipos, `deploy\api\iniciar_api.ps1` pide el token de prueba al arrancar. Es opcional.

Para las capturas de evidencia, tapen el token, las contraseñas y cualquier dato personal.

## Pruebas

```powershell
dotnet run --project tests\unit\LoyOrder.Application.Tests
dotnet run --project tests\unit\LoyOrder.Infrastructure.Loyverse.Tests
dotnet run --project tests\integration\LoyOrder.Api.Tests
# Contra PostgreSQL real: crea y borra la base temporal loyorder_pruebas_sync (no toca loyorder)
$env:LOYORDER_PRUEBAS_ADMIN = "Host=localhost;Username=postgres;Password=<tu contraseña>"
dotnet run --project tests\integration\LoyOrder.Persistence.Tests
```

| Proyecto | Pruebas | Qué cubre |
| --- | ---: | --- |
| `LoyOrder.Application.Tests` (nuevo) | 19 | Reglas de la tabla anterior, completa o incremental, vinculación de tienda, fallas registradas, candado. Usa un Loyverse y un repositorio falsos. |
| `LoyOrder.Infrastructure.Loyverse.Tests` | 24 | Se agregaron 3 del adaptador: precio y disponibilidad de la tienda vinculada, existencias solo de esa tienda y traducción de errores de Loyverse. |
| `LoyOrder.Api.Tests` | 67 | Se agregaron 11: 401, 403 para cliente y operador, 200 para administrador, 502, 503 y 409. |
| `LoyOrder.Persistence.Tests` (nuevo) | 5 | Con PostgreSQL 16 real y los scripts del repositorio: primera corrida, segunda corrida incremental sin duplicados, historial, falla registrada y permisos de `loyorder_api`. |

Ninguna prueba usa la cuenta real de Loyverse ni necesita token.

## Límites de esta entrega

- El token se lee de la configuración de la API. Guardarlo cifrado en la configuración del restaurante corresponde a CU-08 y se conecta registrando un `IProveedorTokenLoyverse` propio.
- La pantalla del panel de administración para "Sincronizar ahora" y para el historial todavía no se hace. Los endpoints ya están listos para 7.2.4 o para el panel.
- El menú del frontend sigue con datos de demostración hasta 7.2.4.
- No se usan webhooks de Loyverse; la sincronización es por consulta periódica.
- El límite de uso de la API de Loyverse lo maneja el cliente de 7.2.2 (reintentos y espera).
