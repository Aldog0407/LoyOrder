using LoyOrder.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
namespace LoyOrder.Api;
public sealed class Errores(IProblemDetailsService problemas, ILogger<Errores> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error, CancellationToken ct)
    {
        var (codigo, titulo) = error switch
        {
            CredencialesInvalidasException => (401, "Correo o contraseÒa incorrectos"),
            CuentaInactivaException => (403, "No es posible acceder con esa cuenta."),
            CorreoDuplicadoException => (409, "No se pudo registrar ese correo."),
            SolicitudInvalidaException => (400, error.Message),
            DependenciaNoDisponibleException => (503, "Base de datos no disponible o sin configurar."),
            _ => (500, "Ocurri√≥ un error inesperado.")
        };
        // No registrar conexiones, credenciales ni detalles internos de PostgreSQL.
        logger.LogWarning("Solicitud fallida {TraceId}: {Tipo}", context.TraceIdentifier, error.GetType().Name);
        context.Response.StatusCode = codigo;
        return await problemas.TryWriteAsync(new() { HttpContext = context,
            ProblemDetails = new() { Status = codigo, Title = titulo } });
    }
}
