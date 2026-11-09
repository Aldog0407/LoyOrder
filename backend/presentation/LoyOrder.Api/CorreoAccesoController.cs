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

[ApiController, AllowAnonymous, EnableRateLimiting("acceso"), Route("api/v1/auth")]
public sealed class CorreoAccesoController(CorreoAcceso? correo = null, IColaCorreo? cola = null) : ControllerBase
{
    [HttpPost("correo/confirmar")]
    public async Task<IActionResult> Confirmar(ConfirmarCorreo datos, CancellationToken ct)
    {
        if (correo is null) return NotFound();
        await correo.ConfirmarAsync(datos.IdDesafio, datos.Codigo, ct);
        return NoContent();
    }

    [HttpPost("correo/reenviar")]
    public async Task<IActionResult> Reenviar(ReenviarCorreo datos, CancellationToken ct)
    {
        if (correo is null) return NotFound();
        return Accepted(new { idDesafio = await correo.ReenviarAsync(datos.IdDesafio, ct) });
    }

    [HttpPost("contrasena/solicitar")]
    public IActionResult Solicitar(SolicitarContrasena datos)
    {
        if (cola is null) return NotFound();
        if (!cola.IntentarEncolar(new SolicitudRecuperacion(datos.Correo))) throw new CorreoNoDisponibleException();
        return Accepted(new { mensaje = "Si hay una cuenta asociada, recibirás un correo con las instrucciones." });
    }

    [HttpPost("contrasena/restablecer")]
    public async Task<IActionResult> Restablecer(RestablecerContrasena datos, CancellationToken ct)
    {
        if (correo is null) return NotFound();
        await correo.RestablecerAsync(datos.Token, datos.NuevaContrasena, ct);
        return NoContent();
    }
}
