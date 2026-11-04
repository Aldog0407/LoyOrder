using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LoyOrder.Infrastructure.Loyverse.Modelos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LoyOrder.Infrastructure.Loyverse;

/// <summary>Operaciones de LoyOrder sobre la API de Loyverse (CU-08, CU-10, CU-20).</summary>
public interface ILoyverseClient
{
    /// <summary>CU-08 paso 3: valida un token recién capturado consultando las tiendas con ese token (no con el guardado).</summary>
    Task<IReadOnlyList<Tienda>> ValidarTokenAsync(string token, CancellationToken ct = default);

    Task<IReadOnlyList<Tienda>> ObtenerTiendasAsync(CancellationToken ct = default);

    /// <summary>CU-08 paso 5: tipos de pago para relacionarlos con Efectivo y Tarjeta.</summary>
    Task<IReadOnlyList<TipoPago>> ObtenerTiposPagoAsync(CancellationToken ct = default);

    /// <summary>CU-10 paso 1.</summary>
    Task<IReadOnlyList<Categoria>> ObtenerCategoriasAsync(bool incluirEliminadas = false, CancellationToken ct = default);

    /// <summary>
    /// CU-10 paso 2: artículos modificados desde <paramref name="actualizadosDesde"/> (null = catálogo completo, FA 2.a),
    /// recorriendo todas las páginas. updated_at_min es inclusivo: guardar con upsert por Id.
    /// </summary>
    Task<IReadOnlyList<Articulo>> ObtenerArticulosAsync(DateTimeOffset? actualizadosDesde = null, bool incluirEliminados = false, CancellationToken ct = default);

    /// <summary>CU-10 paso 5: existencias por variante de una tienda.</summary>
    Task<IReadOnlyList<NivelInventario>> ObtenerExistenciasAsync(string tiendaId, CancellationToken ct = default);

    /// <summary>
    /// EV-21 paso 6 y EV-24 paso 2: existencias actuales de las variantes del carrito al confirmar un pedido.
    /// Consulta el inventario de la tienda (una petición por cada 250 variantes con inventario) y devuelve solo las
    /// variantes pedidas que llevan control de existencias. Una variante que no aparece en el resultado no lleva
    /// control de inventario en Loyverse y no se limita por existencias.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> ObtenerExistenciasDeVariantesAsync(string tiendaId, IEnumerable<string> varianteIds, CancellationToken ct = default);

    /// <summary>CU-20: registra la venta. Nunca se reintenta solo ante fallas de red, para no duplicarla (RN08).</summary>
    Task<Recibo> CrearReciboAsync(NuevoRecibo recibo, CancellationToken ct = default);

    /// <summary>RN08: antes de reintentar un registro con respuesta incierta, revisa si la venta ya existe.</summary>
    Task<Recibo?> BuscarReciboPorFolioAsync(string tiendaId, string folio, int revisarUltimos = 100, CancellationToken ct = default);
}

public sealed class LoyverseClient : ILoyverseClient
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly IProveedorTokenLoyverse _tokens;
    private readonly LimitadorPeticiones _limitador;
    private readonly IReloj _reloj;
    private readonly LoyverseOptions _op;
    private readonly ILogger<LoyverseClient> _log;

    public LoyverseClient(HttpClient http, IProveedorTokenLoyverse tokens, LimitadorPeticiones limitador,
                          IReloj reloj, IOptions<LoyverseOptions> opciones, ILogger<LoyverseClient>? log = null)
    {
        _http = http;
        _tokens = tokens;
        _limitador = limitador;
        _reloj = reloj;
        _op = opciones.Value;
        _log = log ?? NullLogger<LoyverseClient>.Instance;
        _http.BaseAddress ??= new Uri(_op.UrlBase);
    }

    // ------------------------------------------------------------------ operaciones públicas

    public Task<IReadOnlyList<Tienda>> ValidarTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("El token está vacío.", nameof(token));
        return ObtenerTodosAsync<PaginaTiendas, Tienda>("stores", new(), token, ct);
    }

    public Task<IReadOnlyList<Tienda>> ObtenerTiendasAsync(CancellationToken ct = default) =>
        ObtenerTodosAsync<PaginaTiendas, Tienda>("stores", new(), null, ct);

    public Task<IReadOnlyList<TipoPago>> ObtenerTiposPagoAsync(CancellationToken ct = default) =>
        ObtenerTodosAsync<PaginaTiposPago, TipoPago>("payment_types", new(), null, ct);

    public Task<IReadOnlyList<Categoria>> ObtenerCategoriasAsync(bool incluirEliminadas = false, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string>();
        if (incluirEliminadas) q["show_deleted"] = "true";
        return ObtenerTodosAsync<PaginaCategorias, Categoria>("categories", q, null, ct);
    }

    public Task<IReadOnlyList<Articulo>> ObtenerArticulosAsync(DateTimeOffset? actualizadosDesde = null, bool incluirEliminados = false, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string>();
        if (actualizadosDesde is { } desde) q["updated_at_min"] = FormatoFecha(desde);
        if (incluirEliminados) q["show_deleted"] = "true";
        return ObtenerTodosAsync<PaginaArticulos, Articulo>("items", q, null, ct);
    }

    public Task<IReadOnlyList<NivelInventario>> ObtenerExistenciasAsync(string tiendaId, CancellationToken ct = default) =>
        ObtenerTodosAsync<PaginaInventario, NivelInventario>("inventory", new() { ["store_ids"] = tiendaId }, null, ct);

    public async Task<IReadOnlyDictionary<string, decimal>> ObtenerExistenciasDeVariantesAsync(string tiendaId, IEnumerable<string> varianteIds, CancellationToken ct = default)
    {
        var pedidas = varianteIds.ToHashSet();
        if (pedidas.Count == 0) return new Dictionary<string, decimal>();
        var inventario = await ObtenerExistenciasAsync(tiendaId, ct).ConfigureAwait(false);
        return inventario.Where(n => pedidas.Contains(n.VarianteId) && (string.IsNullOrEmpty(n.TiendaId) || n.TiendaId == tiendaId))
                         .GroupBy(n => n.VarianteId)
                         .ToDictionary(g => g.Key, g => g.Sum(n => n.Existencias));
    }

    public async Task<Recibo> CrearReciboAsync(NuevoRecibo recibo, CancellationToken ct = default)
    {
        if (recibo.Pagos.Count != 1)
            throw new ArgumentException("Loyverse acepta exactamente un pago por venta (prueba P15).", nameof(recibo));
        if (recibo.Lineas.Count == 0)
            throw new ArgumentException("La venta no tiene artículos.", nameof(recibo));

        using var resp = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, "receipts")
        {
            Content = JsonContent.Create(recibo, options: Json)
        }, null, reintentarFallasDeRed: false, ct).ConfigureAwait(false);
        return (await resp.Content.ReadFromJsonAsync<Recibo>(Json, ct).ConfigureAwait(false))!;
    }

    public async Task<Recibo?> BuscarReciboPorFolioAsync(string tiendaId, string folio, int revisarUltimos = 100, CancellationToken ct = default)
    {
        var limite = Math.Clamp(revisarUltimos, 1, 250);
        var url = ConQuery("receipts", new() { ["store_id"] = tiendaId, ["limit"] = limite.ToString(CultureInfo.InvariantCulture) });
        using var resp = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, url), null, true, ct).ConfigureAwait(false);
        var pagina = await resp.Content.ReadFromJsonAsync<PaginaRecibos>(Json, ct).ConfigureAwait(false);
        return pagina?.Elementos.FirstOrDefault(r => r.Folio == folio);
    }

    // ------------------------------------------------------------------ paginación

    /// <summary>Recorre todas las páginas siguiendo el cursor hasta que la API ya no lo mande.</summary>
    internal async Task<IReadOnlyList<T>> ObtenerTodosAsync<TPagina, T>(string ruta, Dictionary<string, string> query,
        string? tokenExplicito, CancellationToken ct) where TPagina : IPagina<T>
    {
        var resultado = new List<T>();
        var vistos = new HashSet<string>();
        string? cursor = null;
        var paginas = 0;
        do
        {
            var q = new Dictionary<string, string>(query) { ["limit"] = _op.TamanoPagina.ToString(CultureInfo.InvariantCulture) };
            if (cursor is not null) q["cursor"] = cursor;
            var url = ConQuery(ruta, q);

            using var resp = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, url), tokenExplicito, true, ct).ConfigureAwait(false);
            var pagina = await resp.Content.ReadFromJsonAsync<TPagina>(Json, ct).ConfigureAwait(false)
                         ?? throw new LoyverseException($"Respuesta vacía de {ruta}.");
            resultado.AddRange(pagina.Elementos);

            cursor = string.IsNullOrEmpty(pagina.Cursor) ? null : pagina.Cursor;
            if (cursor is not null && !vistos.Add(cursor))
                throw new LoyverseException($"Loyverse repitió el cursor en {ruta}; se detiene para no ciclarse.");
            if (++paginas >= _op.MaxPaginas && cursor is not null)
                throw new LoyverseException($"Se alcanzó el tope de {_op.MaxPaginas} páginas en {ruta}.");
        } while (cursor is not null);

        _log.LogDebug("Loyverse {Ruta}: {Total} registros en {Paginas} página(s)", ruta, resultado.Count, paginas);
        return resultado;
    }

    // ------------------------------------------------------------------ envío con límite y reintentos

    /// <summary>
    /// Envía la petición respetando el límite de uso. Reintenta con espera creciente ante 429 (siempre, porque la
    /// API no la procesó) y, si <paramref name="reintentarFallasDeRed"/> es true, ante 5xx, tiempo agotado o red.
    /// </summary>
    internal async Task<HttpResponseMessage> EnviarAsync(Func<HttpRequestMessage> crear, string? tokenExplicito,
        bool reintentarFallasDeRed, CancellationToken ct)
    {
        var token = tokenExplicito ?? await _tokens.ObtenerTokenAsync(ct).ConfigureAwait(false);
        var intento = 0;
        while (true)
        {
            intento++;
            await _limitador.EsperarTurnoAsync(ct).ConfigureAwait(false);

            using var req = crear();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage? resp = null;
            Exception? falla = null;
            var cronometro = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_op.TiempoMaximoPeticion);
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                falla = e;
            }

            // El token nunca se escribe en el registro.
            _log.LogInformation("Loyverse {Metodo} {Ruta} -> {Estado} en {Ms} ms (intento {Intento})",
                req.Method, req.RequestUri?.ToString().Split('?')[0], resp is null ? "sin respuesta" : (int)resp.StatusCode,
                cronometro.ElapsedMilliseconds, intento);

            if (resp is not null && resp.IsSuccessStatusCode) return resp;

            var estado = resp?.StatusCode;
            var reintentable = estado == HttpStatusCode.TooManyRequests
                               || (reintentarFallasDeRed && (falla is not null || (int?)estado >= 500));

            if (!reintentable || intento > _op.MaxReintentos)
            {
                try { throw await CrearExcepcionAsync(resp, falla, intento, ct).ConfigureAwait(false); }
                finally { resp?.Dispose(); }
            }

            var espera = resp?.Headers.RetryAfter?.Delta
                         ?? TimeSpan.FromTicks(_op.EsperaBaseReintento.Ticks * (1L << (intento - 1)));
            resp?.Dispose();
            _log.LogWarning("Loyverse respondió {Estado}; reintento {Intento} de {Max} en {Segundos} s",
                estado?.ToString() ?? falla?.GetType().Name, intento, _op.MaxReintentos, espera.TotalSeconds);
            await _reloj.EsperarAsync(espera, ct).ConfigureAwait(false);
        }
    }

    private static async Task<LoyverseException> CrearExcepcionAsync(HttpResponseMessage? resp, Exception? falla, int intentos, CancellationToken ct)
    {
        if (resp is null)
            return new LoyverseNoDisponibleException($"Loyverse no respondió después de {intentos} intento(s).", null, falla);

        switch (resp.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return new LoyverseNoAutorizadoException();
            case HttpStatusCode.TooManyRequests:
                return new LoyverseLimiteExcedidoException(intentos);
            case >= HttpStatusCode.InternalServerError:
                return new LoyverseNoDisponibleException($"Loyverse respondió {(int)resp.StatusCode} después de {intentos} intento(s).", resp.StatusCode);
        }

        string? codigo = null, detalle = null;
        try
        {
            var error = await resp.Content.ReadFromJsonAsync<RespuestaError>(Json, ct).ConfigureAwait(false);
            var primero = error?.Errores?.FirstOrDefault();
            codigo = primero?.Codigo;
            detalle = primero?.Detalle;
        }
        catch (JsonException) { /* cuerpo que no es JSON */ }

        return new LoyverseException($"Loyverse respondió {(int)resp.StatusCode} {codigo}: {detalle}".TrimEnd(' ', ':'), resp.StatusCode, codigo);
    }

    // ------------------------------------------------------------------ utilidades

    /// <summary>Formato ISO 8601 en UTC con milisegundos, el mismo que devuelve updated_at.</summary>
    internal static string FormatoFecha(DateTimeOffset fecha) =>
        fecha.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static string ConQuery(string ruta, Dictionary<string, string> q) =>
        q.Count == 0 ? ruta : ruta + "?" + string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}
