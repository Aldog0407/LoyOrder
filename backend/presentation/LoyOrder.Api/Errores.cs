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
            CredencialesInvalidasException => (401, "Correo o contraseña incorrectos"),
            CuentaInactivaException => (403, "No es posible acceder con esa cuenta."),
            CorreoDuplicadoException => (409, "No se pudo registrar ese correo."),
            DesafioInvalidoException => (400, "El código o enlace es inválido o venció."),
            LimiteCorreoException => (429, "Espera antes de solicitar otro correo."),
            CorreoNoDisponibleException => (503, "No se pudo procesar la solicitud de correo. Intenta más tarde."),
            SolicitudInvalidaException => (400, error.Message),
            DependenciaNoDisponibleException => (503, "Base de datos no disponible o sin configurar."),
            _ => (500, "Ocurrió un error inesperado.")
        };
        // No registrar conexiones, credenciales ni detalles internos de PostgreSQL.
        logger.LogWarning("Solicitud fallida {TraceId}: {Tipo}", context.TraceIdentifier, error.GetType().Name);
        context.Response.StatusCode = codigo;
        var problema = new ProblemDetails { Status = codigo, Title = titulo };
        if (error is DesafioInvalidoException) problema.Extensions["codigo"] = "DESAFIO_INVALIDO_O_VENCIDO";
        return await problemas.TryWriteAsync(new() { HttpContext = context, ProblemDetails = problema });
    }
}
