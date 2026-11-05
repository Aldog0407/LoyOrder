# Actividad 7.1.3 — Base de la API REST por capas

Se preparó sobre main, commit base ec40f85, sin modificar el SQL ni el módulo de Loyverse del equipo. No se hizo commit ni push.

## Alcance

Capas de presentación, aplicación, dominio y persistencia; configuración por variables del proceso; validación de consultas; errores ProblemDetails; lectura de categorías activas por restaurante; health/live y health/ready; pruebas HTTP ejecutables e instrucciones.

Endpoints:
- GET /health/live: servicio activo (200).
- GET /health/ready: disponibilidad de PostgreSQL y campos usados en categoria (200/503).
- GET /api/v1/categorias?idRestaurante=1&pagina=1&tamano=20: consulta paginada de categorías activas (200/400/503).

No introduce tablas ni altera precios, inventario o estados. No adelanta autenticación ni flujos de pedidos. Un restaurante sin categorías devuelve una lista vacía.

## Verificación

SDK .NET 10.0.403 instalado en una carpeta de trabajo aislada para esta comprobación (no cambia el SDK global del equipo).
Compilación de los ocho proyectos de la solución: correcta, cero advertencias y errores.
15 pruebas nuevas HTTP/aplicación: aprobadas. Incluyen parámetros inválidos, listas vacías, 404, 503, 500 y formato de errores sin detalles internos.
21 pruebas previas del cliente Loyverse: aprobadas.
No se ejecutó la demo contra Loyverse real ni se usaron tokens.
Pendiente de verificación en el entorno del equipo: consulta SQL contra una instancia real de PostgreSQL con el esquema 7.1.2 y conexión válida. Las pruebas HTTP usan un repositorio falso y no sustituyen esa comprobación.

## Integración

El ZIP completo contiene una copia del código fuente del repositorio y la actividad 7.1.3. No contiene .git, SDK, dependencias descargadas, binarios ni secretos. Extraer en una carpeta nueva para revisarlo; para integrarlo en un checkout del equipo, copiar únicamente los archivos listados abajo, revisando primero los cambios concurrentes del equipo. No reemplazar el repositorio completo ni borrar archivos.

Los archivos existentes modificados se deben combinar si recibieron cambios después de ec40f85. Particularmente revisar LoyOrder.sln, README.md, backend/README.md, .env.example y docs/DECISIONES_TECNICAS.md.

Ver backend/README.md para ejecución, conexión PostgreSQL, comprobaciones y comandos Git. Primero comprobar `dotnet --list-sdks`: debe aparecer .NET 10.

## Archivos nuevos o modificados

- `backend/application/LoyOrder.Application/Categorias.cs`
- `backend/application/LoyOrder.Application/LoyOrder.Application.csproj`
- `backend/domain/LoyOrder.Domain/Categoria.cs`
- `backend/domain/LoyOrder.Domain/LoyOrder.Domain.csproj`
- `backend/infrastructure/LoyOrder.Infrastructure.Persistence/CategoriaRepository.cs`
- `backend/infrastructure/LoyOrder.Infrastructure.Persistence/LoyOrder.Infrastructure.Persistence.csproj`
- `backend/presentation/LoyOrder.Api/ApiHost.cs`
- `backend/presentation/LoyOrder.Api/CategoriasController.cs`
- `backend/presentation/LoyOrder.Api/Errores.cs`
- `backend/presentation/LoyOrder.Api/LoyOrder.Api.csproj`
- `backend/presentation/LoyOrder.Api/LoyOrder.Api.http`
- `backend/presentation/LoyOrder.Api/Program.cs`
- `backend/presentation/LoyOrder.Api/appsettings.json`
- `global.json`
- `tests/integration/LoyOrder.Api.Tests/LoyOrder.Api.Tests.csproj`
- `tests/integration/LoyOrder.Api.Tests/Program.cs`
- `.env.example`
- `LoyOrder.sln`
- `README.md`
- `backend/README.md`
- `docs/DECISIONES_TECNICAS.md`

## Fuentes técnicas

- Manejo de errores ASP.NET Core: https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0
- Npgsql, data sources y parámetros: https://www.npgsql.org/doc/basic-usage.html

La confirmación de ASP.NET Core/C# y PostgreSQL provino de Aldo en esta sesión. Los nombres de categoria y sus campos provienen del diccionario 3.3 y del SQL existente.
