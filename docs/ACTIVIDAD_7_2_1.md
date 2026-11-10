# Actividad 7.2.1 — Autenticación y roles (verificada en entorno local)

## Qué incluye

Registro público únicamente de clientes, inicio de sesión para CLIENTE/OPERADOR/ADMINISTRADOR, hash de contraseñas con PasswordHasher de ASP.NET Core, JWT firmado con renovación de sesión, verificación de firma/emisor/audiencia/caducidad, autorización de categorías para CLIENTE y cierre de sesión con invalidación inmediata del token en servidor.

Cada petición autenticada comprueba en PostgreSQL que el usuario sigue activo y conserva el rol. Un cambio de rol o desactivación invalida el acceso. Los roles se obtienen de cliente y empleado, nunca de un valor elegido por el navegador. El registro rechaza campos extra como rol. El administrador no hereda permisos del operador o cliente.

El registro escribe usuario y cliente en una transacción, conservando los nombres y columnas del esquema. No agrega tablas ni modifica el SQL del equipo. Se normalizan correos a minúsculas y se serializan registros concurrentes del mismo correo mediante bloqueo transaccional de PostgreSQL. La contraseña de registro requiere entre 8 y 128 caracteres y al menos un símbolo (puntuación o símbolo Unicode; los espacios no cuentan). Política confirmada por Aldo durante las pruebas. Se aplica al registro en navegador y API; no se exige a las cuentas ya creadas al iniciar sesión.

Frontend: /login, /registro, cierre de sesión, validación de confirmación de contraseña, navegación según el rol, eliminación del selector que cambiaba libremente a administrador y limpieza del carrito/historial de demostración al cambiar de usuario. /operacion es solo una pantalla de acceso confirmado, no implementa gestión de pedidos. /admin conserva el dashboard de demostración existente.

## Endpoints

- POST /api/v1/auth/registro: nombre, correo, contrasena, telefono opcional. 201/400/409/429/503.
- POST /api/v1/auth/login: correo, contrasena. Devuelve accessToken, tokenType, expiresAt, refreshToken, idleExpiresAt, sessionExpiresAt y usuario. 200/400/401/403/429/503.
- GET /api/v1/auth/me: requiere Authorization: Bearer TOKEN; devuelve identidad sin hash. 200/401/503.
- POST /api/v1/auth/renovar: recibe `{refreshToken}`, verifica el estado de la cuenta y devuelve un JWT y un secreto de renovación nuevos. 200/400/401/429/503.
- POST /api/v1/auth/logout: recibe el JWT en Authorization o `{refreshToken}`. Revoca la sesión actual. 204/400/401/429/503.
- GET /api/v1/categorias: ahora requiere CLIENTE. Sin token 401, otros roles 403.

## Cómo probar sin PostgreSQL

Desde la raíz del repositorio:

```powershell
dotnet build LoyOrder.sln
dotnet run --project tests/integration/LoyOrder.Api.Tests
```

56 pruebas HTTP/aplicación aprobadas. Se verifica que solo una renovación simultánea pueda usar el mismo secreto y se comprueban ambos límites, el cierre de sesión y la invalidación tras cambiar la contraseña. Usan un repositorio falso que existe únicamente en el ejecutable de pruebas; no son cuentas disponibles en la API normal. Se prueban registro, contraseña incorrecta, usuario inexistente, cuenta desactivada, correo duplicado, prohibición de elegir rol, acceso de los tres roles, logout/reutilización del token, token alterado, vencido o con audiencia incorrecta. Mantienen las comprobaciones de errores y paginación de 7.1.3.

## Duración de la sesión

El JWT dura 15 minutos por defecto. React lo renueva con la actividad en la pestaña, aproximadamente una vez por minuto mientras haya interacción. La sesión vence después de 30 minutos de inactividad o 8 horas desde el inicio, lo que ocurra primero.

| Variable de la API | Minutos por defecto |
| --- | ---: |
| `Auth__AccesoMinutos` | 15 |
| `Auth__InactividadMinutos` | 30 |
| `Auth__SesionMaximaMinutos` | 480 |

React avisa dos minutos antes del límite de sesión. Al volver a entrar con la misma cuenta y rol en la pestaña abierta, restaura la ruta y conserva el carrito y el borrador de configuración. Cerrar sesión de forma explícita o entrar con otra cuenta descarta esos datos.

## Verificación con PostgreSQL real — 5 de noviembre de 2026

Aldo ejecutó el SQL del equipo sobre una base local `loyorder` en PostgreSQL 18 y comprobó las 22 tablas. La API devolvió `estado: Disponible` en `/health/ready`.

Pruebas realizadas por Aldo y confirmadas durante la sesión:

| Comprobación | Resultado |
| --- | --- |
| Registrar cliente desde el navegador y después iniciar sesión | Correcto con persistencia real |
| Login y consulta de identidad | HTTP 200 en login y me |
| Cerrar sesión | HTTP 204 en la solicitud fetch de logout |
| Cliente intentando navegar al panel administrativo | Permanece en el menú de cliente |
| Operador al iniciar sesión | Accede a /operacion |
| Administrador al iniciar sesión | Accede a /admin |
| Operador desactivado temporalmente | Se rechaza el inicio de sesión; se indicó reactivarlo al terminar |
| Consulta de categorías sin token | HTTP 401 |
| Consulta de categorías con cliente | HTTP 200 |
| Consulta de categorías con operador | HTTP 403 |
| Consulta de categorías con administrador | HTTP 403 |

La primera ejecución del comprobador no pudo probar al cliente porque se introdujo un valor sin formato de correo y recibió 400. Se consultó el correo registrado en PostgreSQL y Aldo confirmó después el 200 del cliente. Los cuatro escenarios de permisos quedaron comprobados entre ambas ejecuciones; no se afirma que la primera ejecución completa fuera exitosa.

Se preparó un restaurante local y dos cuentas de personal exclusivamente para pruebas. No se versionan contraseñas, hashes de esas cuentas, tokens, conexiones reales ni un volcado de la base.

Para repetir los permisos con la API encendida y las cuentas de prueba ya existentes:

```powershell
python tests/integration/LoyOrder.Api.Tests/probar_permisos_local.py
```

El script pide las credenciales localmente, no muestra contraseñas o tokens, comprueba los roles y cierra las sesiones que crea. No modifica las cuentas. Una lista vacía de categorías con 200 es válida: comprueba acceso, no la existencia de un catálogo sincronizado.

El alcance de autenticación de esta actividad queda implementado y verificado en desarrollo local. El despliegue compartido, la gestión de personal, la recuperación de contraseña y las funciones completas de cada panel corresponden a otras actividades.

## Arrancar la API para el navegador

En una terminal PowerShell desde la raíz:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://localhost:5080'
# Configurar esta conexión solo cuando se tenga una BD de pruebas con el SQL del equipo:
$env:ConnectionStrings__LoyOrder='Host=localhost;Port=5432;Database=loyorder;Username=TU_USUARIO;Password=TU_CLAVE;Timeout=3;Command Timeout=5'
dotnet run --project backend/presentation/LoyOrder.Api
```

En otra terminal, desde la raíz del repositorio: `cd frontend`, ejecutar `npm ci` la primera vez y después `npm run dev`. Abrir http://localhost:5173/login. El frontend ahora usa React y Vite; el servidor Python anterior fue retirado por la migración.
Si Vite estaba abierto antes de modificar su configuración, detenerlo con Ctrl+C y reiniciarlo. Recargar el navegador con Ctrl+F5.
Sin PostgreSQL, los formularios se muestran pero registrar/iniciar debe responder 503; no se inventan cuentas o accesos exitosos. Coordinar la conexión de pruebas con Brayan. No publicar contraseñas ni conexiones reales.

Para repetir el recorrido de navegador, registrar un cliente desde /registro, iniciar sesión desde /login, ver el menú y cerrar sesión. Usar cuentas locales de prueba para comprobar los roles del personal. No convertir usuarios reales en empleados como parte de una demostración. La gestión de personal y recuperación de contraseña corresponden a otras actividades.

## Límites de esta entrega

Las sesiones se guardan en memoria de UNA instancia de API: al reiniciarla todos los tokens pierden acceso. Para varias instancias se necesita un almacén compartido, a acordar con el equipo; no se ha añadido una tabla fuera del modelo. El token del navegador solo vive en memoria: recargar la página requiere entrar de nuevo. Esto evita almacenarlo en localStorage. El ejemplo está configurado para frontend local en localhost:5173 y API en localhost:5080.

En Production la API exige Auth__SigningKey (Base64 de al menos 32 bytes aleatorios); no hay clave fija incluida. En Development se genera una clave temporal. Para despliegue deben configurarse HTTPS y los orígenes del frontend definitivos. No se almacena el token Loyverse ni se reutiliza para autenticar usuarios.

El catálogo, pedidos, historial y métricas del frontend siguen simulados y no se han integrado a PostgreSQL en esta actividad. La restricción de páginas mejora la navegación, pero la autorización efectiva la aplica la API. Los módulos futuros deben usar [Authorize(Roles = ...)] con los roles exactos de la matriz y comprobar propiedad del pedido cuando corresponda.

## Referencias

- Matriz 4.2 y CU-01 del equipo, aportados por Aldo.
- https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/security/authorization/roles?view=aspnetcore-10.0


## Adaptación posterior a React

Después de la migración del frontend del equipo (base ea5061e), se reimplementaron los formularios como componentes React, se incorporó `AuthProvider` para compartir la sesión y se protegieron las rutas según la matriz 4.2. Se reutilizaron el cliente HTTP y la política de contraseñas. El backend, los endpoints, el SQL y las cuentas existentes no se modificaron.

Se conservaron el menú, carrito, dashboard y configuración del restaurante de Madeline. Se eliminó la navegación libre a administrador. Al cambiar de usuario se limpian los datos de pedidos y dirección de demostración, conservando los ajustes del restaurante.

Validación de esta adaptación: compilación de producción, análisis ESLint y 11 pruebas Node aprobadas (incluyen las 3 previas de configuración). Revisión de navegador contra la API real de ASP.NET Core con repositorios temporales falsos: registro, rechazo de contraseña sin símbolo, login de cliente/operador/administrador, cierre de sesión, historial limpio y configuración administrativa conservada. No se usaron ni modificaron cuentas de PostgreSQL en esa revisión. Queda por repetir el recorrido React con la base real del equipo; los resultados reales de la sección anterior corresponden a la interfaz previa.
