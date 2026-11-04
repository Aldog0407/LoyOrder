namespace LoyOrder.Infrastructure.Loyverse;

/// <summary>
/// Entrega el token de acceso de Loyverse. En producción la implementación lee el token cifrado
/// de la configuración del restaurante (CU-08) y lo descifra solo en el servidor.
/// </summary>
public interface IProveedorTokenLoyverse
{
    Task<string> ObtenerTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Implementación temporal para desarrollo: toma el token de la configuración
/// (user-secrets o variable de entorno LOYVERSE_ACCESS_TOKEN). Nunca se guarda en el repositorio.
/// </summary>
public sealed class ProveedorTokenDesdeConfiguracion : IProveedorTokenLoyverse
{
    private readonly string? _token;

    public ProveedorTokenDesdeConfiguracion(string? token) => _token = token;

    public Task<string> ObtenerTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_token))
            throw new LoyverseNoConfiguradoException();
        return Task.FromResult(_token);
    }
}
