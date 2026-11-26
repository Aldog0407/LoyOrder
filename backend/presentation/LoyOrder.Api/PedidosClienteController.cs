using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalificarPedido([Range(1, 5)] short Calificacion, [StringLength(2000)] string? Comentario);

[ApiController, Authorize(Roles = Roles.Cliente), Route("api/v1/pedidos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PedidosClienteController(PedidosCliente pedidos) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Historial([FromServices] HistorialPedidos consulta,
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, CancellationToken ct = default) =>
        Ok(await consulta.ConsultarAsync(IdUsuario, pagina, tamano, ct));

    [HttpGet("{folio}")]
    public async Task<IActionResult> Consultar(string folio, CancellationToken ct) =>
        Ok(await pedidos.ConsultarAsync(IdUsuario, folio, ct));

    [HttpPost("{folio}/calificacion")]
    public async Task<IActionResult> Calificar(string folio, CalificarPedido datos, CancellationToken ct)
    {
        await pedidos.CalificarAsync(IdUsuario, folio, datos.Calificacion, datos.Comentario, ct);
        return NoContent();
    }

    private long IdUsuario => long.Parse(User.FindFirstValue("sub")!, CultureInfo.InvariantCulture);
}
