using System.ComponentModel.DataAnnotations;
using LoyOrder.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LoyOrder.Api;

public sealed record ConfirmarCorreo(Guid IdDesafio, [Required, RegularExpression("^[0-9]{6}$")] string Codigo);
public sealed record ReenviarCorreo(Guid IdDesafio);
public sealed record SolicitarContrasena([Required, EmailAddress, StringLength(150)] string Correo);
public sealed record RestablecerContrasena([Required, StringLength(97)] string Token,
    [Required, StringLength(128, MinimumLength = 8)] string NuevaContrasena);

[ProducesResponseType(typeof(ErrorApi),400)]
[ProducesResponseType(typeof(ErrorApi),401)]
[ProducesResponseType(typeof(ErrorApi),403)]
[ProducesResponseType(typeof(ErrorApi),404)]
[ProducesResponseType(typeof(ErrorApi),409)]
[ProducesResponseType(typeof(ErrorApi),429)]
[ProducesResponseType(typeof(ErrorApi),500)]
[ProducesResponseType(typeof(ErrorApi),503)]
[ApiController, AllowAnonymous, EnableRateLimiting("acceso"), Route("api/v1/auth")]
public sealed class CorreoAccesoController(CorreoAcceso? correo = null, IColaCorreo? cola = null) : ControllerBase
{
    [Microsoft.AspNetCore.Http.EndpointSummary("Verificar el correo con el código de seis dígitos")]
    [ProducesResponseType(204)]
    [HttpPost("correo/confirmar")]
    public async Task<IActionResult> Confirmar(ConfirmarCorreo datos, CancellationToken ct)
    {
        if (correo is null) return NotFound();
        await correo.ConfirmarAsync(datos.IdDesafio, datos.Codigo, ct);
        return NoContent();
    }

    [Microsoft.AspNetCore.Http.EndpointSummary("Reenviar el código de verificación")]
    [ProducesResponseType(typeof(DesafioApi), 202)]
    [HttpPost("correo/reenviar")]
    public async Task<IActionResult> Reenviar(ReenviarCorreo datos, CancellationToken ct)
    {
        if (correo is null) return NotFound();
        return Accepted(new { idDesafio = await correo.ReenviarAsync(datos.IdDesafio, ct) });
    }

    [Microsoft.AspNetCore.Http.EndpointSummary("Solicitar recuperación sin revelar si existe la cuenta")]
    [ProducesResponseType(typeof(MensajeApi), 202)]
    [HttpPost("contrasena/solicitar")]
    public IActionResult Solicitar(SolicitarContrasena datos)
    {
        if (cola is null) return NotFound();
        if (!cola.IntentarEncolar(new SolicitudRecuperacion(datos.Correo))) throw new CorreoNoDisponibleException();
        return Accepted(new { mensaje = "Si hay una cuenta asociada, recibirás un correo con las instrucciones." });
    }

    [Microsoft.AspNetCore.Http.EndpointSummary("Restablecer la contraseña mediante el token recibido")]
    [ProducesResponseType(204)]
    [HttpPost("contrasena/restablecer")]
    public async Task<IActionResult> Restablecer(RestablecerContrasena datos, CancellationToken ct)
    {
        if (correo is null) return NotFound();
        await correo.RestablecerAsync(datos.Token, datos.NuevaContrasena, ct);
        return NoContent();
    }
}
