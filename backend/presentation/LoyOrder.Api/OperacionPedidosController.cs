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
[ProducesResponseType(typeof(ErrorApi),400)]
[ProducesResponseType(typeof(ErrorApi),401)]
[ProducesResponseType(typeof(ErrorApi),403)]
[ProducesResponseType(typeof(ErrorApi),404)]
[ProducesResponseType(typeof(ErrorApi),409)]
[ProducesResponseType(typeof(ErrorApi),429)]
[ProducesResponseType(typeof(ErrorApi),500)]
[ProducesResponseType(typeof(ErrorApi),503)]
[ApiController,Route("api/v1/operacion/pedidos"),Authorize(Roles=Roles.Operador)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class OperacionPedidosController(OperacionPedidos pedidos) : ControllerBase
{
    private long Operador => long.Parse(User.FindFirstValue("sub")!,CultureInfo.InvariantCulture);
    [Microsoft.AspNetCore.Http.EndpointSummary("Consultar pedidos del restaurante y su registro de venta")]
    [ProducesResponseType(typeof(IReadOnlyList<PedidoOperativo>), 200)]
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) => Ok(await pedidos.ConsultarAsync(Operador,ct));
    [Microsoft.AspNetCore.Http.EndpointSummary("Reintentar una venta fallida o volver a verificar un recibo incierto")]
    [ProducesResponseType(202)]
    [HttpPost("{folio}/venta/reintentar")]
    public async Task<IActionResult> Reintentar(string folio,[FromServices] IVentaRepository ventas,CancellationToken ct)
    {
        await ventas.ReintentarAsync(Operador,folio,ct);
        return Accepted();
    }
    [Microsoft.AspNetCore.Http.EndpointSummary("Aceptar, preparar, despachar, entregar o cancelar un pedido")]
    [ProducesResponseType(typeof(EstadoPedidoActualizado), 200)]
    [Microsoft.AspNetCore.Http.EndpointDescription("Solo pedidos de tu restaurante. Estados: Recibido, En preparación, Enviado, Listo para recoger, Entregado y Cancelado. El servidor valida la transición y Cancelado exige motivo. Entregado deja pendiente el registro de venta.")]
    [HttpPost("{folio}/estado")]
    public async Task<IActionResult> Cambiar(string folio,CambiarEstadoPedido datos,CancellationToken ct) =>
        Ok(await pedidos.CambiarAsync(Operador,folio,datos.Estado,datos.Motivo,ct));

    [HttpGet("{folio}/edicion")]
    [ProducesResponseType(typeof(EditorPedido), 200)]
    public async Task<IActionResult> ConsultarEdicion(string folio, [FromServices] EdicionPedidos edicion, CancellationToken ct) =>
        Ok(await edicion.ConsultarAsync(Operador, folio, ct));

    [HttpPost("{folio}/articulos")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> AgregarArticulo(string folio, AgregarArticuloPedido datos,
        [FromServices] EdicionPedidos edicion, CancellationToken ct)
    {
        await edicion.AgregarAsync(Operador, folio, datos, ct);
        return NoContent();
    }

    [HttpPost("{folio}/articulos/anular")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> AnularArticulo(string folio, AnularArticuloPedido datos,
        [FromServices] EdicionPedidos edicion, CancellationToken ct)
    {
        await edicion.AnularAsync(Operador, folio, datos, ct);
        return NoContent();
    }
}
