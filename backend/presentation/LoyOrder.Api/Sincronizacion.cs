using LoyOrder.Application;
using LoyOrder.Domain;
using LoyOrder.Infrastructure.Loyverse;
using LoyOrder.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LoyOrder.Api;

// Actividad 7.2.3 · CU-10 Sincronizar catálogo e inventario con Loyverse (req. 5 y 6).
//
// Configuración (sección "Sincronizacion"; el token NUNCA va en appsettings ni en el repo):
//   Loyverse:Token o LOYVERSE_ACCESS_TOKEN   token de la cuenta de PRUEBA (user-secrets en desarrollo)
//   Sincronizacion:Automatica                true por defecto; false apaga la tarea programada
//   Sincronizacion:IdRestaurante             opcional; si no se indica, el primer restaurante registrado
//   Sincronizacion:EsperaInicialSegundos     espera antes de la primera corrida automática (15 por defecto)
public static class ConfigurarSincronizacion
{
    public static void AgregarSincronizacion(this WebApplicationBuilder builder)
    {
        builder.Services.AddLoyverse(builder.Configuration);
        builder.Services.AddTransient<IOrigenCatalogo, OrigenCatalogoLoyverse>();
        builder.Services.AddScoped<ICatalogoRepository, CatalogoRepository>();
        builder.Services.AddScoped<SincronizarCatalogo>();

        var token = builder.Configuration["Loyverse:Token"] ?? builder.Configuration["LOYVERSE_ACCESS_TOKEN"];
        var automatica = builder.Configuration.GetValue("Sincronizacion:Automatica", true);
        // Sin token no tiene caso programarla: se registraría una falla cada intervalo. La manual sí queda disponible.
        if (automatica && !string.IsNullOrWhiteSpace(token))
            builder.Services.AddHostedService<SincronizacionProgramada>();
    }

    internal static long? IdRestaurante(IConfiguration configuracion) =>
        configuracion.GetValue<long?>("Sincronizacion:IdRestaurante");
}

/// <summary>Tarea programada de CU-10: corre cada <c>restaurante.intervalo_sincronizacion</c> minutos (1 a 15; 2 por defecto).</summary>
public sealed class SincronizacionProgramada(IServiceScopeFactory scopes, IConfiguration configuracion,
    ILogger<SincronizacionProgramada> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        var espera = TimeSpan.FromSeconds(configuracion.GetValue("Sincronizacion:EsperaInicialSegundos", 15));
        while (!detener.IsCancellationRequested)
        {
            try { await Task.Delay(espera, detener); }
            catch (OperationCanceledException) { return; }

            var minutos = 2;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repositorio = scope.ServiceProvider.GetRequiredService<ICatalogoRepository>();
                var id = ConfigurarSincronizacion.IdRestaurante(configuracion);
                minutos = (await repositorio.RestauranteAsync(id, detener))?.IntervaloMinutos ?? minutos;
                var resumen = await scope.ServiceProvider.GetRequiredService<SincronizarCatalogo>().EjecutarAsync(id, detener);
                logger.LogInformation("Sincronización con Loyverse correcta: {Nuevos} productos nuevos, {Actualizados} actualizados, {Eliminados} eliminados, {Existencias} existencias.",
                    resumen.ProductosNuevos, resumen.ProductosActualizados, resumen.ProductosEliminados, resumen.ExistenciasActualizadas);
            }
            catch (OperationCanceledException) when (detener.IsCancellationRequested) { return; }
            catch (SincronizacionEnCursoException) { /* la inició el administrador; la siguiente vuelta lo intenta de nuevo */ }
            catch (Exception e)
            {
                // Solo el tipo de error: nunca el token ni respuestas de Loyverse o PostgreSQL.
                logger.LogWarning("La sincronización con Loyverse falló: {Tipo}", e.GetType().Name);
            }
            espera = TimeSpan.FromMinutes(Math.Clamp(minutos, 1, 15));
        }
    }
}

/// <summary>"Sincronizar ahora" y el historial del panel del administrador (CU-10).</summary>
[ProducesResponseType(typeof(ErrorApi),401)]
[ProducesResponseType(typeof(ErrorApi),403)]
[ProducesResponseType(typeof(ErrorApi),409)]
[ProducesResponseType(typeof(ErrorApi),502)]
[ProducesResponseType(typeof(ErrorApi),503)]
[ApiController, Route("api/v1/sincronizacion"), Authorize(Roles = Roles.Administrador)]
public sealed class SincronizacionController(SincronizarCatalogo sincronizar, ICatalogoRepository repositorio,
    IConfiguration configuracion) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ResumenSincronizacion>> Sincronizar(CancellationToken ct) =>
        Ok(await sincronizar.EjecutarAsync(ConfigurarSincronizacion.IdRestaurante(configuracion), ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RegistroSincronizacion>>> Historial(CancellationToken ct)
    {
        var restaurante = await repositorio.RestauranteAsync(ConfigurarSincronizacion.IdRestaurante(configuracion), ct)
            ?? throw new RestauranteNoConfiguradoException("No hay un restaurante registrado para sincronizar.");
        return Ok(await repositorio.HistorialAsync(restaurante.IdRestaurante, 20, ct));
    }
}
