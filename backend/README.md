# Backend LoyOrder — actividad 7.1.3

ASP.NET Core / C# .NET 10 y PostgreSQL.

- `presentation/LoyOrder.Api`: HTTP, validación de parámetros y errores ProblemDetails.
- `application/LoyOrder.Application`: coordinación de consultas y contrato del repositorio.
- `domain/LoyOrder.Domain`: modelo Categoria basado en 3.3.
- `infrastructure/LoyOrder.Infrastructure.Persistence`: SQL parametrizado Npgsql.
- `infrastructure/LoyOrder.Infrastructure.Loyverse`: módulo del equipo, conservado sin cambios.

La presentación conecta aplicación e infraestructura por inyección de dependencias. Dominio no depende de otras capas. La API no modifica el catálogo oficial, no ejecuta migraciones y no crea tablas al arrancar.

## Ejecución paso a paso (PowerShell, desde la raíz del repositorio)

1. Comprobar SDK con `dotnet --list-sdks`. Debe aparecer 10.0.x. Si falta, instalar el SDK .NET 10 desde https://dotnet.microsoft.com/download/dotnet/10.0 y abrir otra terminal.
2. Ejecutar `dotnet build LoyOrder.sln`. Resultado esperado: compilación sin errores.
3. Para probar HTTP sin PostgreSQL: `dotnet run --project tests/integration/LoyOrder.Api.Tests`. Debe terminar con pruebas aprobadas y código 0. Usa servidor HTTP real y repositorio falso; no demuestra la consulta SQL contra una BD real.
4. Crear una base PostgreSQL vacía `loyorder` y ejecutar una sola vez `database/scripts/LoyOrderEsquema.sql` usando pgAdmin o psql. El script es del equipo; no ejecutarlo sobre una BD con tablas existentes. Configurar un usuario con acceso de lectura a `categoria`.
5. En la misma terminal, definir la conexión (sustituir los valores):

```powershell
$env:ConnectionStrings__LoyOrder='Host=localhost;Port=5432;Database=loyorder;Username=loyorder;Password=TU_CLAVE;Timeout=3;Command Timeout=5'
$env:ASPNETCORE_URLS='http://localhost:5080'
dotnet run --project backend/presentation/LoyOrder.Api
```

Se espera `Now listening on: http://localhost:5080`. Las variables del proceso tienen prioridad sobre appsettings.json. `.env.example` es una plantilla: ASP.NET Core no carga archivos `.env` automáticamente. `DATABASE_URL` del esqueleto anterior no es utilizada por esta API. No guardar la conexión real en archivos versionados.

6. En otra terminal comprobar:

```powershell
Invoke-RestMethod http://localhost:5080/health/live
Invoke-RestMethod http://localhost:5080/health/ready
Invoke-RestMethod 'http://localhost:5080/api/v1/categorias?idRestaurante=1&pagina=1&tamano=20'
```

Live responde 200 sin depender de PostgreSQL. Ready comprueba acceso a los campos de categoria y responde 200 si están disponibles, o 503 si no. La consulta lista categorías activas del restaurante; en una BD vacía devuelve `[]`. No añade datos inventados. Parámetros omitidos, no numéricos o fuera de rango devuelven 400; rutas inexistentes 404; errores inesperados 500. Errores usan `application/problem+json` con traceId, sin exponer conexiones o excepciones internas.

También se incluyen peticiones en `presentation/LoyOrder.Api/LoyOrder.Api.http`.

## Alcance y siguientes actividades

Únicamente infraestructura base y una lectura pública de categorías para comprobar las capas. No implementa autenticación, altas de usuarios, pedidos, modificaciones de catálogo, sincronización ni registro de ventas. Las rutas administrativas deben incorporar autorización al desarrollarse. Esta ejecución HTTP local es para desarrollo; el despliegue deberá configurar HTTPS y el origen permitido del frontend según la arquitectura del equipo.

## Pruebas existentes de Loyverse

`dotnet run --project tests/unit/LoyOrder.Infrastructure.Loyverse.Tests`

No ejecutar la demo de integración con credenciales reales como parte de estas pruebas.

## Integración con Git

Los cambios se entregan sin subir a GitHub. Conservar los cambios locales de cada compañero. Copiar solamente los archivos enumerados en `docs/ACTIVIDAD_7_1_3.md` al repositorio actualizado. Revisar la diferencia de LoyOrder.sln si el equipo agregó más proyectos. Después de compilar y probar:

```powershell
git status
git diff
git add backend tests/integration/LoyOrder.Api.Tests LoyOrder.sln global.json .env.example docs/DECISIONES_TECNICAS.md docs/ACTIVIDAD_7_1_3.md README.md
git commit -m "feat: agregar base de API REST por capas"
git pull --rebase origin main
git push origin main
```

Revisar `git diff --cached` antes de confirmar. Si hay conflictos, resolverlos antes de continuar; no sobrescribir el trabajo del equipo.
## Autenticación por correo (7.2.2)

La verificación por código y la recuperación por enlace se habilitan con
`CorreoAutenticacion__Habilitado=true`. El valor predeterminado es desactivado
hasta integrar las pantallas de React y la migración de Gabriel.

### Configuración del servidor

| Variable | Valor |
| --- | --- |
| `CorreoAutenticacion__Habilitado` | `true` para activar el flujo. |
| `CorreoAutenticacion__UrlFrontend` | Origen de React, por ejemplo `http://localhost:5173` en Development. En producción usar HTTPS. El backend añade `/restablecer-contrasena`. |
| `CorreoAutenticacion__ClaveCodigo` | Clave independiente en Base64, de al menos 32 bytes aleatorios, para proteger los códigos en PostgreSQL. |
| `CorreoAutenticacion__Smtp__Host` | Servidor SMTP del proveedor. |
| `CorreoAutenticacion__Smtp__Puerto` | Puerto del proveedor, `587` por defecto. |
| `CorreoAutenticacion__Smtp__Seguridad` | `StartTls` o `SslOnConnect`. `None` solo se admite en Development con SMTP de loopback y sin credenciales. |
| `CorreoAutenticacion__Smtp__Remitente` | Dirección autorizada para enviar correo. |
| `CorreoAutenticacion__Smtp__Usuario`, `CorreoAutenticacion__Smtp__Contrasena` | Credenciales del proveedor si las requiere. Se configuran juntas. |

Se conservan `ConnectionStrings__LoyOrder` y `Auth__SigningKey` con el mismo
propósito. Configurar las claves en variables de entorno o en el gestor de
secretos del servidor. Activar el flujo solo después de aplicar la migración
sobre la base existente y comprobar `/health/ready` con la cuenta de BD de la API.

La migración acordada añade `usuario.correo_verificado_en`,
`usuario.version_sesion` y `public.desafio_correo`. La tabla debe tener
`id_desafio` UUID, `id_usuario` BIGINT, `correo` VARCHAR(150), `proposito`
VARCHAR(30), `secreto_hash` BYTEA, `creado_en`, `expira_en`, `usado_en`,
`invalidado_en` TIMESTAMPTZ e `intentos` SMALLINT NOT NULL DEFAULT 0.
Los propósitos son `VERIFICAR_CORREO` y `RESTABLECER_CONTRASENA`.
El rol `loyorder_api` requiere SELECT, INSERT y UPDATE en la tabla nueva.
La tabla debe referenciar `usuario(id_usuario)`. Definir un índice único
parcial en `(id_usuario, proposito)` donde `usado_en IS NULL` e
`invalidado_en IS NULL`, además de un índice para el historial por usuario,
propósito y fecha de creación.

### Contrato HTTP

Todas las operaciones usan POST y el prefijo `/api/v1/auth`.

| Ruta | JSON | Respuesta con el flujo activo |
| --- | --- | --- |
| `/registro` | Campos de registro actuales | 201 con los campos actuales, `correoVerificado: false` e `idDesafio`. |
| `/login` | `correo`, `contrasena` | 200 con JWT si la cuenta está verificada. Si las credenciales son válidas y falta verificar, 403 con `codigo: "CORREO_NO_VERIFICADO"` e `idDesafio`. |
| `/correo/confirmar` | `idDesafio`, `codigo` de seis dígitos como texto | 204. |
| `/correo/reenviar` | `idDesafio` | 202 con el nuevo `idDesafio`. |
| `/contrasena/solicitar` | `correo` | 202 con el mismo `mensaje` para correos válidos, exista o no la cuenta. |
| `/contrasena/restablecer` | `token`, `nuevaContrasena` | 204. Invalida la versión anterior del JWT. |

Un desafío inválido, usado o vencido responde 400 con
`codigo: "DESAFIO_INVALIDO_O_VENCIDO"`. Los límites devuelven 429. El código
vence en 10 minutos, el enlace en 30 y los desafíos se invalidan tras cinco
intentos fallidos. Hay 60 segundos de espera entre emisiones y un máximo de
cinco por usuario y propósito en una hora. El identificador de verificación
permite reenviar durante 24 horas desde su emisión. Después, un login con las
credenciales correctas permite volver a iniciar la verificación.

El 201 y el 202 indican que la operación se aceptó. La entrega se intenta
desde una cola en memoria. Si SMTP falla o la API se reinicia con envíos
pendientes, el usuario puede reenviar el código o solicitar otro enlace tras
el intervalo indicado. La recuperación por enlace se habilita para cuentas
activas con el correo verificado. Las cuentas anteriores pueden verificar su
buzón tras demostrar su contraseña actual.
