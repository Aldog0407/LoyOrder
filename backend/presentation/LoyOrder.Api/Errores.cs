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
            CheckoutCambioException => (409, error.Message),
            PedidoNoEncontradoException => (404, "Pedido no encontrado."),
            CalificacionNoPermitidaException => (409, "Solo se puede calificar una vez un pedido entregado."),
            ProductoMenuNoEncontradoException => (404, "Producto del menú no encontrado."),
            EstacionNoEncontradaException => (404, "Estación, impresora o asignación no encontrada."),
            ConflictoEstacionException => (409, error.Message),
            AdministradorEstacionException => (403, "No es posible administrar las estaciones."),
            TicketNoPermitidoException => (409, error.Message),
            ImpresoraNoConfiguradaException => (409, error.Message),
            ImpresoraNoDisponibleException => (503, "No se pudo conectar con la impresora. Revisa que esté encendida y conectada a la red."),
            SolicitudInvalidaException => (400, error.Message),
            OrigenNoAutorizadoException => (502, "El token de Loyverse no es válido"),
            OrigenNoDisponibleException => (503, "No fue posible conectar con Loyverse, intenta más tarde"),
            OrigenNoConfiguradoException => (409, "Falta configurar el token de Loyverse."),
            RestauranteNoConfiguradoException => (409, error.Message),
            SincronizacionEnCursoException => (409, "Ya hay una sincronización en curso."),
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
