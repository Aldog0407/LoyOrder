using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;
namespace LoyOrder.Api;
public static class ApiHost
{
    public static WebApplication Crear(string[] args, Action<IServiceCollection>? configurar = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AgregarDocumentacion();
        builder.AgregarAcceso();
        builder.AgregarCorreoAutenticacion();
        builder.Services.AddControllers().AddApplicationPart(typeof(CategoriasController).Assembly);
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<Errores>();
        builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
        {
            var cadena = builder.Configuration.GetConnectionString("LoyOrder");
            if (string.IsNullOrWhiteSpace(cadena)) throw new DependenciaNoDisponibleException();
            try { return NpgsqlDataSource.Create(cadena); }
            catch (ArgumentException) { throw new DependenciaNoDisponibleException(); }
        });
        builder.Services.AddScoped<ICategoriaRepository, CategoriaRepository>();
        builder.Services.AddScoped<ConsultarCategorias>();
        builder.Services.AddScoped<IMenuRepository, MenuRepository>();
        builder.Services.AddScoped<ConsultarMenu>();
        builder.AgregarSincronizacion();
        builder.Services.AddScoped<IPedidoClienteRepository, PedidoClienteRepository>();
        builder.Services.AddScoped<PedidosCliente>();
        builder.Services.AddScoped<ICrearPedidoRepository, CrearPedidoRepository>();
        builder.Services.AddScoped<CrearPedido>();
        builder.Services.AddScoped<IHistorialPedidoRepository, HistorialPedidoRepository>();
        builder.Services.AddScoped<HistorialPedidos>();
        builder.Services.AddScoped<IOperacionPedidoRepository, OperacionPedidoRepository>();
        builder.Services.AddScoped<OperacionPedidos>();
        builder.Services.AddScoped<INotificacionPedidoRepository, NotificacionPedidoRepository>();
        builder.Services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById(
            builder.Configuration["Pedidos:ZonaHoraria"] ?? "America/Mexico_City"));
        builder.Services.AddScoped<IVentaRepository, VentaRepository>();
        builder.Services.AddScoped<IDestinoVenta, LoyOrder.Infrastructure.Loyverse.DestinoVentaLoyverse>();
        builder.Services.AddScoped<RegistrarVentas>();
        var ventasHabilitadas=builder.Configuration.GetValue<bool>("Ventas:Habilitado");
        if(ventasHabilitadas) builder.Services.AddHostedService<VentasPendientes>();
        configurar?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors("Frontend");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapearDocumentacion();
        app.MapGet("/health/live", () => Results.Ok(new { estado = "Activo" })).Produces<EstadoApi>(200);
        app.MapGet("/health/ready", async (ICategoriaRepository repo, HttpContext context, CancellationToken ct) =>
        {
            var desafios = context.RequestServices.GetService<IDesafioCorreoRepository>();
            var checkout = context.RequestServices.GetRequiredService<ICrearPedidoRepository>();
            return await repo.DisponibleAsync(ct) && await checkout.DisponibleAsync(ct) && (desafios is null || await desafios.DisponibleAsync(ct))
                && (!ventasHabilitadas || await context.RequestServices.GetRequiredService<IVentaRepository>().DisponibleAsync(ct))
                ? Results.Ok(new { estado = "Disponible" })
                : Results.Problem(statusCode: 503, title: "Base de datos no disponible o esquema no preparado.");
        }).Produces<EstadoApi>(200).ProducesProblem(503);
        return app;
    }
}
