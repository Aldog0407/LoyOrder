using System.Net;
using System.Net.Sockets;

namespace LoyOrder.Application;

public sealed record ImpresoraCocina(long IdImpresora, string Nombre, string DireccionIp, bool Activa);
public sealed record EstacionPreparacion(long IdEstacion, string Nombre, long IdImpresora, bool Activa);
public sealed record CategoriaEstacion(long IdCategoria, string Nombre, bool Activa);
public sealed record VarianteEstacion(long IdVariante, long IdCategoria, string Producto, string? Variante, bool Disponible);
public sealed record AsignacionEstacion(long IdAsignacion, long IdEstacion, long? IdCategoria, long? IdVariante);
public sealed record ConfiguracionEstaciones(IReadOnlyList<ImpresoraCocina> Impresoras, IReadOnlyList<EstacionPreparacion> Estaciones,
    IReadOnlyList<CategoriaEstacion> Categorias, IReadOnlyList<VarianteEstacion> Variantes, IReadOnlyList<AsignacionEstacion> Asignaciones);
public sealed record GuardarImpresora(string Nombre, string DireccionIp, bool Activa);
public sealed record GuardarEstacion(string Nombre, long IdImpresora, bool Activa);
public sealed record GuardarAsignacion(long? IdCategoria, long? IdVariante, bool Reasignar = false);
public sealed class EstacionNoEncontradaException : Exception;
public sealed class ConflictoEstacionException(string mensaje) : Exception(mensaje);
public sealed class AdministradorEstacionException : Exception;

public interface IEstacionRepository
{
    Task<ConfiguracionEstaciones> ConsultarAsync(long administrador, CancellationToken ct);
    Task<long> GuardarImpresoraAsync(long administrador, long? id, GuardarImpresora datos, CancellationToken ct);
    Task<long> GuardarEstacionAsync(long administrador, long? id, GuardarEstacion datos, CancellationToken ct);
    Task<long> AsignarAsync(long administrador, long estacion, GuardarAsignacion datos, CancellationToken ct);
    Task RetirarAsync(long administrador, long estacion, long asignacion, CancellationToken ct);
}

public sealed class Estaciones(IEstacionRepository repo)
{
    public Task<ConfiguracionEstaciones> ConsultarAsync(long admin, CancellationToken ct) => repo.ConsultarAsync(admin, ct);
    public Task<long> GuardarImpresoraAsync(long admin, long? id, GuardarImpresora datos, CancellationToken ct)
    {
        var nombre = Nombre(datos.Nombre);
        if (string.IsNullOrWhiteSpace(datos.DireccionIp) || datos.DireccionIp.Length > 45
            || (!datos.DireccionIp.Contains(':') && !IPv4Valida(datos.DireccionIp))
            || (datos.DireccionIp.Contains('.') && !IPv4Valida(datos.DireccionIp.Split(':')[^1]))
            || !IPAddress.TryParse(datos.DireccionIp, out var ip) || ip.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            || datos.DireccionIp.Contains('%') || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            throw new SolicitudInvalidaException("Indica una dirección IPv4 o IPv6 válida.");
        return repo.GuardarImpresoraAsync(admin, id, datos with { Nombre = nombre, DireccionIp = ip.ToString() }, ct);
    }
    public Task<long> GuardarEstacionAsync(long admin, long? id, GuardarEstacion datos, CancellationToken ct)
    {
        if (datos.IdImpresora <= 0) throw new SolicitudInvalidaException("Selecciona una impresora de cocina.");
        return repo.GuardarEstacionAsync(admin, id, datos with { Nombre = Nombre(datos.Nombre) }, ct);
    }
    public Task<long> AsignarAsync(long admin, long estacion, GuardarAsignacion datos, CancellationToken ct)
    {
        if ((datos.IdCategoria is null) == (datos.IdVariante is null) || datos.IdCategoria is <= 0 || datos.IdVariante is <= 0)
            throw new SolicitudInvalidaException("Selecciona una categoría o una variante, no ambas.");
        return repo.AsignarAsync(admin, estacion, datos, ct);
    }
    public Task RetirarAsync(long admin, long estacion, long asignacion, CancellationToken ct) => repo.RetirarAsync(admin, estacion, asignacion, ct);
    private static bool IPv4Valida(string valor)
    {
        var partes = valor.Split('.');
        return partes.Length == 4 && partes.All(p => p.Length is >= 1 and <= 3
            && (p.Length == 1 || p[0] != '0') && p.All(c => c is >= '0' and <= '9')
            && byte.TryParse(p, out _));
    }
    private static string Nombre(string? valor) => string.IsNullOrWhiteSpace(valor) || valor.Trim().Length > 100
        ? throw new SolicitudInvalidaException("Indica un nombre de hasta 100 caracteres.") : valor.Trim();
}
