using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CambiarEstadoPedido(string Estado,string? Motivo);
[ApiController,Route("api/v1/operacion/pedidos"),Authorize(Roles=Roles.Operador)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class OperacionPedidosController(OperacionPedidos pedidos) : ControllerBase
{
    private long Operador => long.Parse(User.FindFirstValue("sub")!,CultureInfo.InvariantCulture);
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) => Ok(await pedidos.ConsultarAsync(Operador,ct));
    [HttpPost("{folio}/venta/reintentar")]
    public async Task<IActionResult> Reintentar(string folio,[FromServices] IVentaRepository ventas,CancellationToken ct)
    {
        await ventas.ReintentarAsync(Operador,folio,ct);
        return Accepted();
    }
    [HttpPost("{folio}/estado")]
    public async Task<IActionResult> Cambiar(string folio,CambiarEstadoPedido datos,CancellationToken ct) =>
        Ok(await pedidos.CambiarAsync(Operador,folio,datos.Estado,datos.Motivo,ct));
}
