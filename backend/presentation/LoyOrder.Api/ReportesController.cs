using System.Globalization;
using System.Security.Claims;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

[ApiController, Route("api/v1/admin/reportes"), Authorize(Roles = Roles.Administrador)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ErrorApi), 400)]
[ProducesResponseType(typeof(ErrorApi), 401)]
[ProducesResponseType(typeof(ErrorApi), 403)]
[ProducesResponseType(typeof(ErrorApi), 503)]
public sealed class ReportesController(ConsultarReportes reportes) : ControllerBase
{
    private long Administrador => long.Parse(User.FindFirstValue("sub")!, CultureInfo.InvariantCulture);

    [HttpGet("{tipo}")]
    [ProducesResponseType(typeof(ReporteRestaurante), 200)]
    public async Task<IActionResult> Consultar(string tipo, [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct) =>
        Ok(await reportes.EjecutarAsync(Administrador, tipo, desde, hasta, ct));

    [HttpGet("{tipo}/descarga/{formato}")]
    public async Task<IActionResult> Descargar(string tipo, string formato, [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        if (formato is not ("pdf" or "xlsx"))
            throw new SolicitudInvalidaException("Selecciona PDF o Excel.");
        var reporte = await reportes.EjecutarAsync(Administrador, tipo, desde, hasta, ct);
        var nombre = FormattableString.Invariant($"loyorder-{tipo}-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.{formato}");
        return formato == "pdf"
            ? File(ExportarReportePdf.Generar(reporte), "application/pdf", nombre)
            : File(ExportarReporteExcel.Generar(reporte), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
    }
}
