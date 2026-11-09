using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;
namespace LoyOrder.Api;
public static class ApiHost
{
    public static WebApplication Crear(string[] args, Action<IServiceCollection>? configurar = null)
    {
        var builder = WebApplication.CreateBuilder(args);
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
        configurar?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors("Frontend");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health/live", () => Results.Ok(new { estado = "Activo" }));
        app.MapGet("/health/ready", async (ICategoriaRepository repo, HttpContext context, CancellationToken ct) =>
        {
            var desafios = context.RequestServices.GetService<IDesafioCorreoRepository>();
            return await repo.DisponibleAsync(ct) && (desafios is null || await desafios.DisponibleAsync(ct))
                ? Results.Ok(new { estado = "Disponible" })
                : Results.Problem(statusCode: 503, title: "Base de datos no disponible o esquema no preparado.");
        });
        return app;
    }
}
