using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LoyOrder.Infrastructure.Loyverse;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el cliente de Loyverse. Uso en Program.cs de LoyOrder.Api:
    /// <code>builder.Services.AddLoyverse(builder.Configuration);</code>
    /// Si no se registró antes un <see cref="IProveedorTokenLoyverse"/>, usa el token de configuración: Loyverse:Token
    /// (user-secrets) o la variable de entorno LOYVERSE_ACCESS_TOKEN de .env.example. Solo para desarrollo.
    /// </summary>
    public static IServiceCollection AddLoyverse(this IServiceCollection services, IConfiguration configuracion)
    {
        var seccion = configuracion.GetSection(LoyverseOptions.Seccion);
        services.Configure<LoyverseOptions>(seccion);

        services.TryAddSingleton<IReloj, RelojSistema>();
        services.TryAddSingleton(sp =>
        {
            var op = sp.GetRequiredService<IOptions<LoyverseOptions>>().Value;
            return new LimitadorPeticiones(op.MaxPeticionesPorVentana, op.VentanaLimite, sp.GetRequiredService<IReloj>());
        });
        services.TryAddSingleton<IProveedorTokenLoyverse>(_ => new ProveedorTokenDesdeConfiguracion(seccion["Token"] ?? configuracion["LOYVERSE_ACCESS_TOKEN"]));

        services.AddHttpClient<ILoyverseClient, LoyverseClient>((sp, http) =>
        {
            var op = sp.GetRequiredService<IOptions<LoyverseOptions>>().Value;
            http.BaseAddress = new Uri(op.UrlBase);
            // El tiempo máximo por intento lo controla LoyverseClient; este es solo un tope general.
            http.Timeout = TimeSpan.FromMinutes(2);
        });
        return services;
    }
}
