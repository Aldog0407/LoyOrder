# 7.4.1 · Documentación OpenAPI y Swagger

## Acceso

Con la API ejecutándose en Development en el puerto habitual, abrir `http://localhost:5080/swagger`. El documento OpenAPI 3.1 se obtiene en `http://localhost:5080/openapi/v1.json`. Ambos están deshabilitados en Production y esa condición está comprobada por la suite HTTP.

La documentación se genera desde las rutas, DTO, anotaciones de respuestas y metadatos de autorización de los controladores. No requiere conexión a PostgreSQL para consultar el contrato. Los servicios de negocio conservan sus dependencias habituales cuando se ejecutan operaciones.

Se incorporaron `Microsoft.AspNetCore.OpenApi` 10.0.15 y `Swashbuckle.AspNetCore.SwaggerUI` 11.0.1. La interfaz incluye sus recursos en el paquete y no depende de una CDN. No hay cambios de tablas ni migraciones nuevas para esta actividad.

## Contenido del contrato

Incluye acceso y renovación de sesión, verificación y recuperación por correo, catálogo, categorías, opciones de compra, creación e historial de pedidos, seguimiento, calificación, operación por restaurante, reintento de ventas, sincronización y salud del servicio.

Se documentan modelos de entrada/salida, códigos de respuesta, errores ProblemDetails y el esquema Bearer. Los roles CLIENTE, OPERADOR y ADMINISTRADOR se obtienen de los mismos metadatos que protege la API. El inicio de sesión y la recuperación se documentan sin JWT obligatorio. En logout se admite la sesión o el refreshToken, como en su implementación.

Los límites y reglas del negocio que no pueden inferirse del tipo JSON se explican en las descripciones y contratos de actividad. Por ejemplo, la creación reutiliza el mismo idOperacion al reintentar y la contraseña requiere símbolo además de 8–128 caracteres. Consultar también `ACTIVIDAD_7_2_1.md`, `ACTIVIDAD_7_2_13.md` y `ACTIVIDAD_PEDIDOS_2026_11_25.md`.

## Recorrido para la exposición

1. Abrir Swagger y desplegar `GET /health/live`.
2. Pulsar Try it out y Execute. Debe responder 200 y `estado: Activo`.
3. Mostrar una ruta protegida, su rol y su modelo de entrada, sin necesidad de ejecutar una compra.
4. Para una consulta autenticada, iniciar sesión con la cuenta propia usando `/api/v1/auth/login`, copiar solo su accessToken y pegarlo en Authorize. Swagger añade el encabezado Bearer.
5. Consultar una ruta correspondiente al rol de esa cuenta. Otra área devuelve 403, conforme a la matriz de permisos.

Execute hace una petición real a la API. Las escrituras actúan sobre la base configurada. El token de Loyverse permanece en el servidor y nunca se pega en Authorize. Las contraseñas de las cuentas se introducen localmente y no se incluyen en este documento.

## Verificación

La suite HTTP comprueba generación del documento, esquema Bearer, acceso público de login, rol OPERADOR, contratos de sesión y checkout, respuesta 201 al crear un pedido, ruta de reintento de venta y ausencia de Swagger/OpenAPI en Production. El resultado es 127 comprobaciones aprobadas, incluyendo los recorridos anteriores de negocio.

Se revisó además la interfaz Swagger en un servidor temporal y se ejecutó `GET /health/live`, con respuesta 200 y `estado: Activo`. La compilación de la solución pasa con cero errores y cero advertencias. Las pruebas usan claves efímeras y desactivan SMTP, sincronización y ventas para aislarse de las credenciales de una PowerShell de trabajo.

Referencias de implementación: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0 y https://learn.microsoft.com/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0.
