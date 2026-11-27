using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoyOrder.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArticuloSolicitado(long IdVariante, int Cantidad, decimal PrecioEsperado);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DireccionSolicitada(string Calle, string Numero, string? Referencias, decimal? Latitud, decimal? Longitud);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SolicitudPedido(Guid IdOperacion, string TipoEntrega, long IdMetodoPago,
    decimal TotalEsperado, IReadOnlyList<ArticuloSolicitado> Articulos, DireccionSolicitada? Direccion);
public sealed record MetodoPagoPedido(long Id, string Nombre);
public sealed record ConfiguracionPedido(long IdRestaurante, string Nombre, string? IdTiendaLoyverse,
    TimeOnly Apertura, TimeOnly Cierre, decimal CostoEnvio, IReadOnlyList<MetodoPagoPedido> MetodosPago);
public sealed record OpcionesPedido(long IdRestaurante, string Nombre, string Apertura, string Cierre,
    decimal CostoEnvio, bool Abierto, IReadOnlyList<MetodoPagoPedido> MetodosPago);
public sealed record VariantePedido(long Id, string? IdLoyverse, string Nombre, decimal Precio, bool ControlaInventario);
public sealed record PedidoCreado(long IdPedido, string Folio, DateTime FechaCreacion, string Estado,
    decimal Subtotal, decimal CostoEnvio, decimal Total);
public sealed class CheckoutCambioException(string mensaje) : Exception(mensaje);

public interface ICrearPedidoRepository
{
    Task<bool> DisponibleAsync(CancellationToken ct);
    Task<ConfiguracionPedido> ConfiguracionAsync(long? idRestaurante, CancellationToken ct);
    Task<IReadOnlyList<VariantePedido>> VariantesAsync(long idRestaurante, long[] ids, CancellationToken ct);
    Task<PedidoCreado?> RecuperarAsync(long idUsuario, Guid operacion, byte[] hash, CancellationToken ct);
    Task<PedidoCreado> GuardarAsync(long idUsuario, SolicitudPedido solicitud, byte[] hash,
        ConfiguracionPedido configuracion, IReadOnlyList<VariantePedido> variantes,
        DateTimeOffset ahora, TimeZoneInfo zona, CancellationToken ct);
}

public sealed class CrearPedido(ICrearPedidoRepository pedidos, IOrigenCatalogo origen, TimeProvider reloj, TimeZoneInfo zona)
{
    public async Task<OpcionesPedido> OpcionesAsync(long? idRestaurante, CancellationToken ct)
    {
        var c = await pedidos.ConfiguracionAsync(idRestaurante, ct);
        return new(c.IdRestaurante, c.Nombre, c.Apertura.ToString("HH:mm"), c.Cierre.ToString("HH:mm"),
            c.CostoEnvio, EstaAbierto(c, reloj.GetUtcNow(), zona), c.MetodosPago);
    }

    public async Task<PedidoCreado> EjecutarAsync(long idUsuario, long? idRestaurante, SolicitudPedido solicitud, CancellationToken ct)
    {
        var normalizada = Normalizar(solicitud);
        var hash = Huella(normalizada);
        var anterior = await pedidos.RecuperarAsync(idUsuario, normalizada.IdOperacion, hash, ct);
        if (anterior is not null) return anterior;
        var configuracion = await pedidos.ConfiguracionAsync(idRestaurante, ct);
        ValidarConfiguracion(configuracion, normalizada, reloj.GetUtcNow(), zona);
        var variantes = await pedidos.VariantesAsync(configuracion.IdRestaurante,
            normalizada.Articulos.Select(a => a.IdVariante).ToArray(), ct);
        ValidarPrecios(normalizada, variantes, configuracion.CostoEnvio);
        var controladas = variantes.Where(v => v.ControlaInventario).ToList();
        if (controladas.Count > 0)
        {
            var existencias = await ConsultarExistenciasAsync(origen, configuracion.IdTiendaLoyverse, controladas, ct);
            foreach (var variante in controladas)
            {
                var cantidad = normalizada.Articulos.Single(a => a.IdVariante == variante.Id).Cantidad;
                if (!existencias.TryGetValue(variante.IdLoyverse!, out var disponible) || disponible < cantidad)
                    throw new CheckoutCambioException($"No hay existencias suficientes de {variante.Nombre}. Revisa tu carrito.");
            }
        }
        return await pedidos.GuardarAsync(idUsuario, normalizada, hash, configuracion, variantes, reloj.GetUtcNow(), zona, ct);
    }

    public static async Task<IReadOnlyDictionary<string, decimal>> ConsultarExistenciasAsync(IOrigenCatalogo origen,
        string? tienda, IReadOnlyList<VariantePedido> variantes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tienda) || variantes.Any(v => string.IsNullOrWhiteSpace(v.IdLoyverse)))
            throw new CheckoutCambioException("Falta vincular el inventario de los artículos con Loyverse.");
        return (await origen.ExistenciasAsync(tienda, ct)).ToDictionary(e => e.VarianteId, e => e.Existencias);
    }

    public static SolicitudPedido Normalizar(SolicitudPedido s)
    {
        if (s.IdOperacion == Guid.Empty || s.IdMetodoPago <= 0 || s.TipoEntrega is not ("Domicilio" or "Recolección") ||
            s.TotalEsperado <= 0 || s.TotalEsperado > 9_999_999_999.99m || decimal.Round(s.TotalEsperado, 2) != s.TotalEsperado ||
            s.Articulos is null || s.Articulos.Count is < 1 or > 100 ||
            s.Articulos.Any(a => a is null || a.IdVariante <= 0 || a.Cantidad is < 1 or > 20 || a.PrecioEsperado <= 0 || a.PrecioEsperado > 9_999_999_999.99m ||
                decimal.Round(a.PrecioEsperado, 2) != a.PrecioEsperado) ||
            s.Articulos.Select(a => a.IdVariante).Distinct().Count() != s.Articulos.Count)
            throw new SolicitudInvalidaException("Revisa la operación, modalidad, método de pago, artículos y cantidades (máximo 20 por variante).");
        DireccionSolicitada? direccion = null;
        if (s.TipoEntrega == "Domicilio")
        {
            var d = s.Direccion;
            if (d is null || string.IsNullOrWhiteSpace(d.Calle) || d.Calle.Length > 150 ||
                string.IsNullOrWhiteSpace(d.Numero) || d.Numero.Length > 20 || d.Referencias?.Length > 1000 ||
                d.Latitud is null or < -90 or > 90 || d.Longitud is null or < -180 or > 180)
                throw new SolicitudInvalidaException("Completa la calle, número y ubicación de entrega.");
            direccion = d with { Calle = d.Calle.Trim(), Numero = d.Numero.Trim(), Referencias = string.IsNullOrWhiteSpace(d.Referencias) ? null : d.Referencias.Trim(), Latitud = decimal.Round(d.Latitud.Value, 7), Longitud = decimal.Round(d.Longitud.Value, 7) };
        }
        else if (s.Direccion is not null)
            throw new SolicitudInvalidaException("La recolección en local no requiere dirección.");
        return s with { Articulos = s.Articulos.OrderBy(a => a.IdVariante).ToArray(), Direccion = direccion };
    }

    public static byte[] Huella(SolicitudPedido s) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        s.TipoEntrega, s.IdMetodoPago, Total = s.TotalEsperado.ToString("0.00", CultureInfo.InvariantCulture),
        Articulos = s.Articulos.Select(a => new { a.IdVariante, a.Cantidad, Precio = a.PrecioEsperado.ToString("0.00", CultureInfo.InvariantCulture) }),
        Direccion = s.Direccion is { } d ? new {
            d.Calle, d.Numero, d.Referencias,
            Latitud = d.Latitud!.Value.ToString("0.0000000", CultureInfo.InvariantCulture),
            Longitud = d.Longitud!.Value.ToString("0.0000000", CultureInfo.InvariantCulture)
        } : null
    }));

    public static bool EstaAbierto(ConfiguracionPedido c, DateTimeOffset ahora, TimeZoneInfo zona)
    {
        var hora = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona).DateTime);
        return c.Apertura < c.Cierre ? hora >= c.Apertura && hora < c.Cierre
            : c.Apertura > c.Cierre && (hora >= c.Apertura || hora < c.Cierre);
    }

    public static void ValidarConfiguracion(ConfiguracionPedido c, SolicitudPedido s, DateTimeOffset ahora, TimeZoneInfo zona)
    {
        if (!EstaAbierto(c, ahora, zona)) throw new CheckoutCambioException("El restaurante está cerrado en este momento.");
        if (!c.MetodosPago.Any(p => p.Id == s.IdMetodoPago)) throw new CheckoutCambioException("El método de pago ya no está disponible.");
    }

    public static decimal ValidarPrecios(SolicitudPedido s, IReadOnlyList<VariantePedido> variantes, decimal envio)
    {
        if (variantes.Count != s.Articulos.Count) throw new CheckoutCambioException("Hay artículos que ya no están disponibles.");
        decimal subtotal = 0;
        foreach (var a in s.Articulos)
        {
            var v = variantes.SingleOrDefault(v => v.Id == a.IdVariante);
            if (v is null || v.Precio != a.PrecioEsperado || v.Precio <= 0)
                throw new CheckoutCambioException("El precio o la disponibilidad cambió. Actualiza tu carrito y vuelve a confirmar.");
            subtotal += v.Precio * a.Cantidad;
        }
        if (subtotal + (s.TipoEntrega == "Domicilio" ? envio : 0) != s.TotalEsperado)
            throw new CheckoutCambioException("El total o costo de envío cambió. Revisa el total antes de confirmar.");
        return subtotal;
    }
}
