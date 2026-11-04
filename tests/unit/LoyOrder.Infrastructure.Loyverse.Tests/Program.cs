using System.Net;
using System.Reflection;
using System.Text;
using LoyOrder.Infrastructure.Loyverse;
using LoyOrder.Infrastructure.Loyverse.Modelos;
using Microsoft.Extensions.Options;

// Pruebas del cliente de Loyverse sin red: un HttpMessageHandler falso devuelve respuestas preparadas
// y un reloj falso hace que las esperas no tarden.
var pruebas = typeof(Pruebas).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.Name.StartsWith("Prueba_")).OrderBy(m => m.MetadataToken).ToList();
var fallidas = 0;
foreach (var p in pruebas)
{
    try
    {
        await (Task)p.Invoke(null, null)!;
        Console.WriteLine($"  OK   {p.Name}");
    }
    catch (Exception e)
    {
        fallidas++;
        var real = e is TargetInvocationException t ? t.InnerException! : e;
        Console.WriteLine($"  FALLA {p.Name}: {real.Message}");
    }
}
Console.WriteLine($"\n{pruebas.Count - fallidas} de {pruebas.Count} pruebas aprobadas.");
return fallidas == 0 ? 0 : 1;

// ------------------------------------------------------------------ dobles de prueba

sealed class HandlerFalso : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _respuestas = new();
    public List<(HttpMethod Metodo, string Url, string? Auth, string? Cuerpo)> Peticiones { get; } = new();

    public HandlerFalso Responder(HttpStatusCode estado, string json = "{}", Action<HttpResponseMessage>? extra = null)
    {
        _respuestas.Enqueue(_ =>
        {
            var r = new HttpResponseMessage(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            extra?.Invoke(r);
            return r;
        });
        return this;
    }

    public HandlerFalso FallarRed()
    {
        _respuestas.Enqueue(_ => throw new HttpRequestException("Conexión rechazada (simulada)"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var cuerpo = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
        Peticiones.Add((req.Method, req.RequestUri!.ToString(), req.Headers.Authorization?.ToString(), cuerpo));
        if (_respuestas.Count == 0) throw new InvalidOperationException("No hay más respuestas preparadas.");
        return _respuestas.Dequeue()(req);
    }
}

sealed class RelojFalso : IReloj
{
    public DateTimeOffset Ahora { get; private set; } = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    public List<TimeSpan> Esperas { get; } = new();
    public Task EsperarAsync(TimeSpan tiempo, CancellationToken ct)
    {
        Esperas.Add(tiempo);
        if (tiempo > TimeSpan.Zero) Ahora += tiempo;
        return Task.CompletedTask;
    }
    public void Avanzar(TimeSpan t) => Ahora += t;
}

static class Fabrica
{
    public static (LoyverseClient Cliente, HandlerFalso Handler, RelojFalso Reloj) Crear(
        Action<LoyverseOptions>? configurar = null, string? token = "token-de-prueba")
    {
        var op = new LoyverseOptions { TamanoPagina = 2 };
        configurar?.Invoke(op);
        var handler = new HandlerFalso();
        var reloj = new RelojFalso();
        var http = new HttpClient(handler) { BaseAddress = new Uri(op.UrlBase) };
        var cliente = new LoyverseClient(http, new ProveedorTokenDesdeConfiguracion(token),
            new LimitadorPeticiones(op.MaxPeticionesPorVentana, op.VentanaLimite, reloj), reloj, Options.Create(op));
        return (cliente, handler, reloj);
    }
}

static class Afirmar
{
    public static void Igual<T>(T esperado, T real, string que = "")
    {
        if (!EqualityComparer<T>.Default.Equals(esperado, real))
            throw new Exception($"{que} se esperaba <{esperado}> y llegó <{real}>");
    }
    public static void Verdadero(bool cond, string que)
    {
        if (!cond) throw new Exception(que);
    }
    public static async Task<TEx> LanzaAsync<TEx>(Func<Task> accion) where TEx : Exception
    {
        try { await accion(); }
        catch (TEx e) { return e; }
        catch (Exception e) { throw new Exception($"Se esperaba {typeof(TEx).Name} y llegó {e.GetType().Name}: {e.Message}"); }
        throw new Exception($"Se esperaba {typeof(TEx).Name} y no se lanzó nada");
    }
}

// ------------------------------------------------------------------ pruebas

static class Pruebas
{
    public static async Task Prueba_EnviaTokenBearerYUrlCorrecta()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """{"stores":[{"id":"t1","name":"LoyOrder"}]}""");
        var tiendas = await c.ObtenerTiendasAsync();
        Afirmar.Igual(1, tiendas.Count, "Tiendas:");
        Afirmar.Igual("LoyOrder", tiendas[0].Nombre, "Nombre:");
        Afirmar.Igual("Bearer token-de-prueba", h.Peticiones[0].Auth, "Encabezado:");
        Afirmar.Igual("https://api.loyverse.com/v1.0/stores?limit=2", h.Peticiones[0].Url, "URL:");
    }

    public static async Task Prueba_ValidarTokenUsaElTokenCapturadoYNoElGuardado()
    {
        var (c, h, _) = Fabrica.Crear(token: null); // todavía no hay token guardado (primera configuración)
        h.Responder(HttpStatusCode.OK, """{"stores":[{"id":"t1","name":"LoyOrder"}]}""");
        var tiendas = await c.ValidarTokenAsync("token-nuevo");
        Afirmar.Igual(1, tiendas.Count, "Tiendas:");
        Afirmar.Igual("Bearer token-nuevo", h.Peticiones[0].Auth, "Encabezado:");
    }

    public static async Task Prueba_SinTokenConfiguradoLanzaNoConfigurado()
    {
        var (c, h, _) = Fabrica.Crear(token: null);
        await Afirmar.LanzaAsync<LoyverseNoConfiguradoException>(() => c.ObtenerCategoriasAsync());
        Afirmar.Igual(0, h.Peticiones.Count, "Peticiones:");
    }

    public static async Task Prueba_RecorreTodasLasPaginasConElCursor()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """{"items":[{"id":"a1","item_name":"Alitas 6","variants":[]},{"id":"a2","item_name":"Alitas 10","variants":[]}],"cursor":"c1"}""")
         .Responder(HttpStatusCode.OK, """{"items":[{"id":"a3","item_name":"Boneless","variants":[]},{"id":"a4","item_name":"Papas","variants":[]}],"cursor":"c2"}""")
         .Responder(HttpStatusCode.OK, """{"items":[{"id":"a5","item_name":"Pay","variants":[]}]}""");
        var articulos = await c.ObtenerArticulosAsync();
        Afirmar.Igual(5, articulos.Count, "Artículos:");
        Afirmar.Igual(3, h.Peticiones.Count, "Peticiones:");
        Afirmar.Verdadero(!h.Peticiones[0].Url.Contains("cursor="), "La primera página no debe llevar cursor");
        Afirmar.Verdadero(h.Peticiones[1].Url.Contains("cursor=c1"), "La segunda página debe llevar cursor=c1");
        Afirmar.Verdadero(h.Peticiones[2].Url.Contains("cursor=c2"), "La tercera página debe llevar cursor=c2");
    }

    public static async Task Prueba_SincronizacionIncrementalMandaUpdatedAtMinEnUtc()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """{"items":[]}""");
        await c.ObtenerArticulosAsync(new DateTimeOffset(2026, 10, 26, 12, 30, 5, 120, TimeSpan.FromHours(-6)));
        Afirmar.Verdadero(h.Peticiones[0].Url.Contains("updated_at_min=2026-10-26T18%3A30%3A05.120Z"),
            "updated_at_min debe ir en UTC con milisegundos: " + h.Peticiones[0].Url);
    }

    public static async Task Prueba_LeeVariantesPrecioDisponibilidadYExistencias()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """
            {"items":[{"id":"h1","item_name":"Hamburguesa hawaiana","category_id":"cat1","track_stock":false,
              "description":"<p>Con piña</p>","updated_at":"2026-10-26T18:00:00.000Z",
              "variants":[{"variant_id":"v1","sku":"HAM-HAW","stores":[{"store_id":"t1","price":115,"available_for_sale":false}]}]}]}
            """)
         .Responder(HttpStatusCode.OK, """{"inventory_levels":[{"variant_id":"v2","store_id":"t1","in_stock":-1}]}""");
        var a = (await c.ObtenerArticulosAsync()).Single();
        var enTienda = a.Variantes[0].EnTienda("t1")!;
        Afirmar.Igual(115m, enTienda.Precio, "Precio:");
        Afirmar.Igual(false, enTienda.DisponibleParaVenta, "Disponible:");
        Afirmar.Igual("cat1", a.CategoriaId, "Categoría:");
        var inv = (await c.ObtenerExistenciasAsync("t1")).Single();
        Afirmar.Igual(-1m, inv.Existencias, "Existencias (negativas como en la prueba P17):");
        Afirmar.Verdadero(h.Peticiones[1].Url.Contains("store_ids=t1"), "Debe filtrar por store_ids");
    }

    public static async Task Prueba_ExistenciasAlConfirmarSoloDevuelveLasVariantesDelCarrito()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """{"inventory_levels":[{"variant_id":"cola","store_id":"t1","in_stock":47},{"variant_id":"lim","store_id":"t1","in_stock":-1}],"cursor":"p2"}""")
         .Responder(HttpStatusCode.OK, """{"inventory_levels":[{"variant_id":"naranja","store_id":"t1","in_stock":24}]}""");
        var ex = await c.ObtenerExistenciasDeVariantesAsync("t1", new[] { "lim", "naranja", "hamb-clasica" });
        Afirmar.Igual(2, ex.Count, "Variantes con inventario:");
        Afirmar.Igual(-1m, ex["lim"], "Lima-limón:");
        Afirmar.Igual(24m, ex["naranja"], "Naranja (segunda página):");
        Afirmar.Verdadero(!ex.ContainsKey("hamb-clasica"), "La hamburguesa no lleva inventario y no debe aparecer");
        Afirmar.Verdadero(h.Peticiones[0].Url.Contains("store_ids=t1"), "Debe filtrar por la tienda");
    }

    public static async Task Prueba_ExistenciasAlConfirmarConCarritoVacioNoConsulta()
    {
        var (c, h, _) = Fabrica.Crear();
        var ex = await c.ObtenerExistenciasDeVariantesAsync("t1", Array.Empty<string>());
        Afirmar.Igual(0, ex.Count, "Resultado:");
        Afirmar.Igual(0, h.Peticiones.Count, "Peticiones:");
    }

    public static async Task Prueba_401NoSeReintentaYLanzaNoAutorizado()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.Unauthorized, """{"errors":[{"code":"UNAUTHORIZED"}]}""");
        await Afirmar.LanzaAsync<LoyverseNoAutorizadoException>(() => c.ObtenerTiendasAsync());
        Afirmar.Igual(1, h.Peticiones.Count, "Peticiones:");
    }

    public static async Task Prueba_429SeReintentaConEsperaCreciente()
    {
        var (c, h, r) = Fabrica.Crear();
        h.Responder(HttpStatusCode.TooManyRequests).Responder(HttpStatusCode.TooManyRequests)
         .Responder(HttpStatusCode.OK, """{"categories":[{"id":"c1","name":"Alitas"}]}""");
        var cats = await c.ObtenerCategoriasAsync();
        Afirmar.Igual(1, cats.Count, "Categorías:");
        Afirmar.Igual(3, h.Peticiones.Count, "Peticiones:");
        Afirmar.Igual(TimeSpan.FromSeconds(2), r.Esperas[0], "Primera espera:");
        Afirmar.Igual(TimeSpan.FromSeconds(4), r.Esperas[1], "Segunda espera:");
    }

    public static async Task Prueba_429RespetaRetryAfterSiLoMandan()
    {
        var (c, h, r) = Fabrica.Crear();
        h.Responder(HttpStatusCode.TooManyRequests, "{}", m => m.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30)))
         .Responder(HttpStatusCode.OK, """{"stores":[]}""");
        await c.ObtenerTiendasAsync();
        Afirmar.Igual(TimeSpan.FromSeconds(30), r.Esperas[0], "Espera:");
    }

    public static async Task Prueba_429PersistenteLanzaLimiteExcedidoTrasTresReintentos()
    {
        var (c, h, _) = Fabrica.Crear();
        for (var i = 0; i < 4; i++) h.Responder(HttpStatusCode.TooManyRequests);
        await Afirmar.LanzaAsync<LoyverseLimiteExcedidoException>(() => c.ObtenerTiendasAsync());
        Afirmar.Igual(4, h.Peticiones.Count, "Peticiones (1 + 3 reintentos):");
    }

    public static async Task Prueba_FallaDeRedEnConsultaSeReintenta()
    {
        var (c, h, _) = Fabrica.Crear();
        h.FallarRed().Responder(HttpStatusCode.ServiceUnavailable).Responder(HttpStatusCode.OK, """{"stores":[]}""");
        await c.ObtenerTiendasAsync();
        Afirmar.Igual(3, h.Peticiones.Count, "Peticiones:");
    }

    public static async Task Prueba_CrearReciboNoSeReintentaAnteFallaDeRed()
    {
        var (c, h, _) = Fabrica.Crear();
        h.FallarRed();
        await Afirmar.LanzaAsync<LoyverseNoDisponibleException>(() => c.CrearReciboAsync(ReciboDePrueba()));
        Afirmar.Igual(1, h.Peticiones.Count, "Peticiones (no debe reintentar para no duplicar la venta):");
    }

    public static async Task Prueba_CrearReciboMandaFolioOrigenNotaYUnSoloPago()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """{"receipt_number":"1-1001","order":"LO-0127","source":"LoyOrder","total_money":310}""");
        var r = await c.CrearReciboAsync(ReciboDePrueba());
        Afirmar.Igual("1-1001", r.Numero, "Número de recibo:");
        Afirmar.Igual(310m, r.Total, "Total:");
        var cuerpo = h.Peticiones[0].Cuerpo!;
        foreach (var campo in new[] { "\"store_id\":\"t1\"", "\"order\":\"LO-0127\"", "\"source\":\"LoyOrder\"", "\"line_note\":\"Salsa BBQ\"", "\"payment_type_id\":\"pago-efectivo\"" })
            Afirmar.Verdadero(cuerpo.Contains(campo), $"El cuerpo debe incluir {campo}: {cuerpo}");
        Afirmar.Verdadero(h.Peticiones[0].Url.EndsWith("/receipts"), "Debe ir a /receipts");
    }

    public static async Task Prueba_CrearReciboConDosPagosSeRechazaAntesDeEnviar()
    {
        var (c, h, _) = Fabrica.Crear();
        var recibo = ReciboDePrueba();
        recibo.Pagos.Add(new PagoRecibo { TipoPagoId = "pago-tarjeta", PagadoEn = DateTimeOffset.UtcNow });
        await Afirmar.LanzaAsync<ArgumentException>(() => c.CrearReciboAsync(recibo));
        Afirmar.Igual(0, h.Peticiones.Count, "Peticiones:");
    }

    public static async Task Prueba_Error400TraeCodigoDeLoyverse()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.BadRequest, """{"errors":[{"code":"INVALID_VALUE","details":"Only one payment can be applied to receipt"}]}""");
        var e = await Afirmar.LanzaAsync<LoyverseException>(() => c.CrearReciboAsync(ReciboDePrueba()));
        Afirmar.Igual("INVALID_VALUE", e.Codigo, "Código:");
        Afirmar.Igual(1, h.Peticiones.Count, "Peticiones (un 400 no se reintenta):");
    }

    public static async Task Prueba_BuscarReciboPorFolioEncuentraLaVentaExistente()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """{"receipts":[{"receipt_number":"1-1000","order":"LO-0126"},{"receipt_number":"1-1001","order":"LO-0127"}]}""");
        var r = await c.BuscarReciboPorFolioAsync("t1", "LO-0127");
        Afirmar.Igual("1-1001", r?.Numero, "Recibo encontrado:");
    }

    public static async Task Prueba_CursorRepetidoDetieneLaPaginacion()
    {
        var (c, h, _) = Fabrica.Crear();
        h.Responder(HttpStatusCode.OK, """{"categories":[],"cursor":"x"}""").Responder(HttpStatusCode.OK, """{"categories":[],"cursor":"x"}""");
        await Afirmar.LanzaAsync<LoyverseException>(() => c.ObtenerCategoriasAsync());
        Afirmar.Igual(2, h.Peticiones.Count, "Peticiones:");
    }

    public static async Task Prueba_LimitadorEsperaCuandoSeLlenaLaVentana()
    {
        var reloj = new RelojFalso();
        var limitador = new LimitadorPeticiones(3, TimeSpan.FromSeconds(10), reloj);
        for (var i = 0; i < 3; i++) { await limitador.EsperarTurnoAsync(); reloj.Avanzar(TimeSpan.FromSeconds(1)); }
        Afirmar.Igual(0, reloj.Esperas.Count, "No debe esperar con 3 de 3:");
        await limitador.EsperarTurnoAsync(); // la cuarta espera a que salga la primera (t=0 + 10 s)
        Afirmar.Igual(1, reloj.Esperas.Count, "Esperas:");
        Afirmar.Igual(TimeSpan.FromSeconds(7), reloj.Esperas[0], "Tiempo de espera:");
        Afirmar.Igual(3, limitador.EnVentana, "Peticiones en la ventana:");
    }

    public static async Task Prueba_ElLimitadorCuentaTambienLosReintentos()
    {
        var (c, h, _) = Fabrica.Crear(op => { op.MaxPeticionesPorVentana = 2; op.VentanaLimite = TimeSpan.FromSeconds(300); });
        h.Responder(HttpStatusCode.TooManyRequests).Responder(HttpStatusCode.OK, """{"stores":[]}""")
         .Responder(HttpStatusCode.OK, """{"stores":[]}""");
        await c.ObtenerTiendasAsync(); // 2 peticiones (429 + 200)
        await c.ObtenerTiendasAsync(); // la tercera debe esperar a que se libere la ventana
        Afirmar.Igual(3, h.Peticiones.Count, "Peticiones:");
    }

    private static NuevoRecibo ReciboDePrueba() => new()
    {
        TiendaId = "t1",
        Folio = "LO-0127",
        Nota = "Pedido en línea · Entrega a domicilio",
        Lineas = { new LineaRecibo { VarianteId = "v-alitas10", Cantidad = 1, Nota = "Salsa BBQ" } },
        Pagos = { new PagoRecibo { TipoPagoId = "pago-efectivo", PagadoEn = new DateTimeOffset(2026, 10, 29, 2, 0, 0, TimeSpan.Zero) } },
    };
}
