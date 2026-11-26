using LoyOrder.Application;

sealed class CheckoutFalso : ICrearPedidoRepository
{
    public Task<bool> DisponibleAsync(CancellationToken ct) => Task.FromResult(true);
    public ConfiguracionPedido Configuracion { get; set; } = new(1,"Prueba","t1",new(9,0),new(22,0),25,[new(3,"Efectivo")]);
    public IReadOnlyList<VariantePedido> Variantes { get; set; } = [new(11,"v11","Producto",100,true)];
    public int Guardados { get; private set; }
    private readonly Dictionary<(long, Guid), (byte[] Hash, PedidoCreado Pedido)> pedidos = [];
    public Task<ConfiguracionPedido> ConfiguracionAsync(long? id, CancellationToken ct) => Task.FromResult(Configuracion);
    public Task<IReadOnlyList<VariantePedido>> VariantesAsync(long id, long[] ids, CancellationToken ct) => Task.FromResult(Variantes);
    public Task<PedidoCreado?> RecuperarAsync(long usuario, Guid operacion, byte[] hash, CancellationToken ct)
    {
        if (!pedidos.TryGetValue((usuario,operacion),out var anterior)) return Task.FromResult<PedidoCreado?>(null);
        if (!anterior.Hash.SequenceEqual(hash)) throw new CheckoutCambioException("Operación diferente");
        return Task.FromResult<PedidoCreado?>(anterior.Pedido);
    }
    public Task<PedidoCreado> GuardarAsync(long usuario, SolicitudPedido solicitud, byte[] hash, ConfiguracionPedido c,
        IReadOnlyList<VariantePedido> v, DateTimeOffset ahora, TimeZoneInfo zona, CancellationToken ct)
    {
        var pedido = new PedidoCreado(++Guardados,$"LY-{Guardados:D8}",ahora.UtcDateTime,"Recibido",200,0,200);
        pedidos.Add((usuario,solicitud.IdOperacion),(hash,pedido));
        return Task.FromResult(pedido);
    }
}

sealed class StockCheckout : IOrigenCatalogo
{
    public IReadOnlyList<ExistenciaOrigen> Stock { get; set; } = [new("v11",20)];
    public int Consultas { get; private set; }
    public bool Fallar { get; set; }
    public Task<IReadOnlyList<ExistenciaOrigen>> ExistenciasAsync(string tienda, CancellationToken ct)
    {
        Consultas++;
        if (Fallar) throw new OrigenNoDisponibleException();
        return Task.FromResult(Stock);
    }
    public Task<IReadOnlyList<TiendaOrigen>> TiendasAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TiendaOrigen>>([]);
    public Task<IReadOnlyList<CategoriaOrigen>> CategoriasAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<CategoriaOrigen>>([]);
    public Task<IReadOnlyList<ArticuloOrigen>> ArticulosAsync(string tienda, DateTimeOffset? desde, CancellationToken ct) => Task.FromResult<IReadOnlyList<ArticuloOrigen>>([]);
}

static partial class Pruebas
{
    private static SolicitudPedido PedidoCheckout() => new(Guid.NewGuid(),"Recolección",3,200,[new(11,2,100)],null);
    private static CrearPedido ServicioCheckout(CheckoutFalso repo, StockCheckout stock) =>
        new(repo,stock,new RelojFalso(new(2026,11,25,18,0,0,TimeSpan.Zero)),TimeZoneInfo.Utc);

    public static async Task Prueba_CheckoutConsultaStockYReintentaSinDuplicar()
    {
        var repo = new CheckoutFalso(); var stock = new StockCheckout(); var servicio = ServicioCheckout(repo,stock);
        var solicitud = PedidoCheckout();
        var primero = await servicio.EjecutarAsync(1,null,solicitud,default);
        stock.Fallar = true;
        var repetido = await servicio.EjecutarAsync(1,null,solicitud,default);
        Datos.Igual(primero.Folio,repetido.Folio,"folio al reintentar");
        Datos.Igual(1,repo.Guardados,"un solo pedido");
        Datos.Igual(1,stock.Consultas,"reintento recupera antes de volver a consultar stock");
        await Datos.Lanza<CheckoutCambioException>(() => servicio.EjecutarAsync(1,null,solicitud with { TotalEsperado=100, Articulos=[new(11,1,100)] },default));
    }

    public static async Task Prueba_CheckoutRechazaStockAusenteInsuficienteYFallaRemota()
    {
        foreach (var stock in new StockCheckout[] { new() { Stock=[] }, new() { Stock=[new("v11",1)] } })
        {
            var repo = new CheckoutFalso();
            await Datos.Lanza<CheckoutCambioException>(() => ServicioCheckout(repo,stock).EjecutarAsync(1,null,PedidoCheckout(),default));
            Datos.Igual(0,repo.Guardados,"sin stock no se guarda");
        }
        var r = new CheckoutFalso();
        await Datos.Lanza<OrigenNoDisponibleException>(() => ServicioCheckout(r,new() { Fallar=true }).EjecutarAsync(1,null,PedidoCheckout(),default));
        Datos.Igual(0,r.Guardados,"falla remota no crea pedido");
    }

    public static async Task Prueba_CheckoutRechazaCambiosDePrecioPagoYTotal()
    {
        foreach (var solicitud in new[] { PedidoCheckout() with { TotalEsperado=1 }, PedidoCheckout() with { IdMetodoPago=99 },
            PedidoCheckout() with { TotalEsperado=202,Articulos=[new(11,2,101)] } })
        {
            var repo = new CheckoutFalso();
            await Datos.Lanza<CheckoutCambioException>(() => ServicioCheckout(repo,new()).EjecutarAsync(1,null,solicitud,default));
            Datos.Igual(0,repo.Guardados,"sin confirmar cambios no se guarda");
        }
    }

    public static async Task Prueba_CheckoutValidaCantidadesDireccionYOperacion()
    {
        foreach (var solicitud in new[] { PedidoCheckout() with { IdOperacion=Guid.Empty },
            PedidoCheckout() with { Articulos=[new(11,21,100)] },
            PedidoCheckout() with { Articulos=[new(11,1,100),new(11,1,100)] },
            PedidoCheckout() with { TipoEntrega="Domicilio" },
            PedidoCheckout() with { TipoEntrega="Domicilio", Direccion=new("Calle","1",null,null,null) } })
            await Datos.Lanza<SolicitudInvalidaException>(() => ServicioCheckout(new(),new()).EjecutarAsync(1,null,solicitud,default));
    }

    public static Task Prueba_CheckoutReintentoNormalizaDecimalesDeDireccion()
    {
        var primera = PedidoCheckout() with { TipoEntrega="Domicilio", TotalEsperado=225,
            Direccion=new(" Calle "," 1 ",null,19.5m,-96m) };
        var otra = primera with { TotalEsperado=225.00m, Articulos=[new(11,2,100.00m)],
            Direccion=new("Calle","1",null,19.5000000m,-96.0000000m) };
        Datos.Cierto(CrearPedido.Huella(CrearPedido.Normalizar(primera)).SequenceEqual(
            CrearPedido.Huella(CrearPedido.Normalizar(otra))),"misma solicitud con distinta escala decimal");
        return Task.CompletedTask;
    }

    public static Task Prueba_CheckoutHorarioNocturnoYZonaHoraria()
    {
        var c = new CheckoutFalso().Configuracion with { Apertura=new(22,0),Cierre=new(6,0) };
        Datos.Cierto(CrearPedido.EstaAbierto(c,new(2026,11,26,1,0,0,TimeSpan.Zero),TimeZoneInfo.Utc),"abierto tras medianoche");
        Datos.Cierto(!CrearPedido.EstaAbierto(c,new(2026,11,26,12,0,0,TimeSpan.Zero),TimeZoneInfo.Utc),"cerrado al mediodía");
        var zona = TimeZoneInfo.CreateCustomTimeZone("Prueba-6",TimeSpan.FromHours(-6),"Prueba","Prueba");
        Datos.Cierto(!CrearPedido.EstaAbierto(c,new(2026,11,26,1,0,0,TimeSpan.Zero),zona),"usa zona del restaurante");
        return Task.CompletedTask;
    }
}
