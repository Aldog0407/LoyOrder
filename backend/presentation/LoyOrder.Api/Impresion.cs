using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using LoyOrder.Application;
using LoyOrder.Domain;
using LoyOrder.Infrastructure.Impresion;
using LoyOrder.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace LoyOrder.Api;

// Actividad 7.2.11 · CU-19 Imprimir ticket de venta (ESC/POS por red, TCP 9100). Roles según la matriz 4.2:
//   ticket y reimpresión (CU-19) solo OPERADOR; hoja de prueba de la impresora (CU-09) solo ADMINISTRADOR.
// Configuración: Impresion:Puerto (9100), Impresion:PaginaCodigos (PC858), Impresion:TiempoEsperaSegundos (5),
//   Ticket:Direccion y Ticket:Telefono (opcionales). La zona horaria es la de los pedidos (Pedidos:ZonaHoraria).
public static class ConfigurarImpresion
{
    public static void AgregarImpresion(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        builder.Services.AddImpresion(cfg);
        builder.Services.AddScoped<ITicketRepository, TicketRepository>();
        builder.Services.AddSingleton(sp => new ConfiguracionTicket(cfg["Ticket:Direccion"], cfg["Ticket:Telefono"],
            sp.GetRequiredService<TimeZoneInfo>()));
        builder.Services.AddScoped<ServicioTicket>();
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SolicitudTicket(bool Reimpresion);
public sealed record LineaVista(string Texto, string Alineacion, bool Negrita, bool Doble, bool Invertido);
public sealed record VistaTicket(string Texto, IReadOnlyList<LineaVista> Lineas);

[ProducesResponseType(typeof(ErrorApi), 400)]
[ProducesResponseType(typeof(ErrorApi), 401)]
[ProducesResponseType(typeof(ErrorApi), 403)]
[ProducesResponseType(typeof(ErrorApi), 404)]
[ProducesResponseType(typeof(ErrorApi), 409)]
[ProducesResponseType(typeof(ErrorApi), 503)]
[ApiController, Route("api/v1"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TicketController(ServicioTicket tickets) : ControllerBase
{
    private long Usuario => long.Parse(User.FindFirstValue("sub")!, CultureInfo.InvariantCulture);

    [Microsoft.AspNetCore.Http.EndpointSummary("Vista previa del ticket de venta en 48 columnas")]
    [ProducesResponseType(typeof(VistaTicket), 200)]
    [HttpGet("operacion/pedidos/{folio}/ticket"), Authorize(Roles = Roles.Operador)]
    public async Task<IActionResult> VistaPrevia([StringLength(30)] string folio, [FromQuery] bool reimpresion, CancellationToken ct)
    {
        var documento = await tickets.VistaPreviaAsync(Usuario, folio, reimpresion, ct);
        return Ok(new VistaTicket(documento.ComoTexto(), documento.Lineas
            .Select(l => new LineaVista(l.Texto, l.Alineacion.ToString().ToLowerInvariant(), l.Negrita, l.Doble, l.Invertido)).ToList()));
    }

    [Microsoft.AspNetCore.Http.EndpointSummary("Imprimir o reimprimir el ticket de venta en la impresora de caja")]
    [Microsoft.AspNetCore.Http.EndpointDescription("Solo pedidos de tu restaurante y no cancelados. Con reimpresion=true el ticket lleva la marca *** REIMPRESIÓN ***. 503 si la impresora no responde.")]
    [ProducesResponseType(typeof(ResultadoImpresion), 200)]
    [HttpPost("operacion/pedidos/{folio}/ticket"), Authorize(Roles = Roles.Operador)]
    public async Task<IActionResult> Imprimir([StringLength(30)] string folio,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SolicitudTicket? datos, CancellationToken ct) =>
        Ok(await tickets.ImprimirAsync(Usuario, folio, datos?.Reimpresion ?? false, ct));

    [Microsoft.AspNetCore.Http.EndpointSummary("Imprimir la hoja de prueba de la impresora de caja")]
    [Microsoft.AspNetCore.Http.EndpointDescription("Imprime la misma frase con acentos en PC850, PC858 y WPC1252 para elegir Impresion:PaginaCodigos.")]
    [ProducesResponseType(typeof(ResultadoImpresion), 200)]
    [HttpPost("impresion/caja/prueba"), Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Prueba(CancellationToken ct) => Ok(await tickets.ImprimirPruebaAsync(Usuario, ct));
}
