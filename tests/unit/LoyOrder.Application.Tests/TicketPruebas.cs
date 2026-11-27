using LoyOrder.Application;

// Pruebas del ticket de venta (7.2.11, CU-19) con el diseño 5.4: formato de 48 columnas y servicio con dobles.
sealed class TicketRepoFalso : ITicketRepository
{
    public DatosTicket? Pedido { get; set; } = DatosTicketPrueba.Domicilio();
    public ImpresoraDestino? Caja { get; set; } = new(3, "Caja", "192.168.123.100");
    public (long, string)? Admin { get; set; } = (1, "Alitas Don Carlos");
    public List<(long Operador, string Folio)> Consultas { get; } = [];
    public Task<DatosTicket?> ObtenerAsync(long idOperador, string folio, CancellationToken ct)
    {
        Consultas.Add((idOperador, folio));
        return Task.FromResult(Pedido?.Folio == folio ? Pedido : null);
    }
    public Task<(long IdRestaurante, string Nombre)?> RestauranteDeAdministradorAsync(long id, CancellationToken ct) => Task.FromResult(Admin);
    public Task<ImpresoraDestino?> ImpresoraCajaAsync(long idRestaurante, CancellationToken ct) => Task.FromResult(Caja);
}

sealed class ImpresoraFalsa : IImpresoraTermica
{
    public bool Caida { get; set; }
    public List<(ImpresoraDestino Destino, DocumentoImpresion Doc)> Enviados { get; } = [];
    public bool Prueba { get; private set; }
    public Task ImprimirAsync(ImpresoraDestino destino, DocumentoImpresion documento, CancellationToken ct)
    {
        if (Caida) throw new ImpresoraNoDisponibleException(destino.Nombre);
        Enviados.Add((destino, documento));
        return Task.CompletedTask;
    }
    public Task ImprimirPruebaAsync(ImpresoraDestino destino, DocumentoImpresion encabezado, CancellationToken ct)
    {
        Prueba = true;
        return ImprimirAsync(destino, encabezado, ct);
    }
}

static class DatosTicketPrueba
{
    public static readonly TimeZoneInfo Mexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    // 28/10/2026 19:58 en Ciudad de México (UTC-6)
    public static readonly DateTimeOffset Fecha = new(2026, 10, 29, 1, 58, 0, TimeSpan.Zero);

    public static DatosTicket Domicilio() => new(1, "Alitas Don Carlos", "LY-00000127", "En preparación", TiposEntrega.Domicilio,
        "María Pérez", "2281234567", "Av. Juárez", "12", "Centro", "Portón verde", "Efectivo", 280m, 30m, 310m,
        [new("Alitas", "10 piezas", 1, 165m, 165m, null), new("Papas a la francesa", null, 1, 55m, 55m, "Extra queso"),
         new("Refresco de cola", "Cola", 2, 30m, 60m, null)]);

    public static DatosTicket Recoger() => Domicilio() with
    {
        TipoEntrega = TiposEntrega.Recoleccion, CostoEnvio = 0m, Total = 280m, Calle = null, Numero = null, Colonia = null, Referencias = null,
    };

    public static ConfiguracionTicket Config(string? direccion = "Calle 5 de Mayo 10, Emiliano Zapata", string? tel = "2281112233") =>
        new(direccion, tel, Mexico);

    public static (TicketRepoFalso Repo, ImpresoraFalsa Impresora, ServicioTicket Servicio) Crear()
    {
        var repo = new TicketRepoFalso();
        var impresora = new ImpresoraFalsa();
        return (repo, impresora, new ServicioTicket(repo, impresora, Config(), new RelojFalso(Fecha)));
    }

    public static string[] Lineas(DocumentoImpresion d) => d.ComoTexto().TrimEnd('\n').Split('\n');
}

static partial class Pruebas
{
    public static Task Prueba_TicketNoPasaDe48ColumnasNiDe24EnDoble()
    {
        var largo = DatosTicketPrueba.Domicilio() with
        {
            Restaurante = "Restaurante de alitas y hamburguesas del centro",
            Articulos = [new("Hamburguesa doble con tocino, queso cheddar y aros de cebolla", "Grande", 3, 165m, 495m,
                "Sin cebolla\nAderezo chipotle aparte por favor, el cliente es alérgico al ajo")],
        };
        var doc = FormatoTicket.Crear(largo, DatosTicketPrueba.Config(), DatosTicketPrueba.Fecha, false);
        Datos.Cierto(doc.Lineas.All(l => l.Texto.Length <= (l.Doble ? 24 : 48)), "ancho máximo por línea");
        Datos.Cierto(doc.Lineas.Count(l => l.Doble) >= 3, "nombre largo del restaurante partido a doble tamaño");
        Datos.Cierto(doc.Lineas.Any(l => l.Texto.EndsWith("$495.00")), "importe alineado a la derecha");
        return Task.CompletedTask;
    }

    public static Task Prueba_TicketADomicilioComoElDiseno54()
    {
        var doc = FormatoTicket.Crear(DatosTicketPrueba.Domicilio(), DatosTicketPrueba.Config(), DatosTicketPrueba.Fecha, false);
        var texto = doc.ComoTexto();
        foreach (var esperado in new[] { "PEDIDO EN LÍNEA", "Folio: LY-00000127", "28/10/2026 19:58", "1 x Alitas 10 piezas", "$165.00",
                     "    > Extra queso", "2 x Refresco de cola", "    2 x $30.00", "Costo de envío", "TOTAL $310.00", "EFECTIVO",
                     "Se paga al recibir el pedido.", "ENTREGA A DOMICILIO", "Cliente: María Pérez", "Tel.: 2281234567",
                     "Av. Juárez 12, Col. Centro", "Ref.: Portón verde", "Comprobante de pedido. No es factura.", "¡Gracias por tu pedido!",
                     "Calle 5 de Mayo 10, Emiliano Zapata", "Tel. 2281112233" })
            Datos.Cierto(texto.Contains(esperado), $"falta \"{esperado}\"");
        Datos.Cierto(!texto.Contains("REIMPRESIÓN"), "el original no lleva la marca");
        Datos.Cierto(!texto.Contains("Cola Cola"), "la variante no se repite si ya está en el nombre");
        Datos.Cierto(doc.Lineas.Single(l => l.Texto.Contains("ENTREGA A DOMICILIO")) is { Invertido: true, Texto.Length: 48 }, "banda a todo el ancho");
        Datos.Cierto(doc.Lineas.Single(l => l.Texto.StartsWith("TOTAL")) is { Doble: true, Alineacion: Alineacion.Derecha }, "total a doble tamaño");
        return Task.CompletedTask;
    }

    public static Task Prueba_TicketParaRecogerReimpresoSinEnvio()
    {
        var doc = FormatoTicket.Crear(DatosTicketPrueba.Recoger(), DatosTicketPrueba.Config(null, null), DatosTicketPrueba.Fecha, true);
        var texto = doc.ComoTexto();
        Datos.Cierto(texto.Contains("*** REIMPRESIÓN ***") && texto.Contains("RECOGER EN EL LOCAL"), "marca y banda de recoger");
        Datos.Cierto(!texto.Contains("Costo de envío") && !texto.Contains("Tel. "), "sin envío ni teléfono del restaurante");
        Datos.Cierto(doc.Lineas.Any(l => l.Doble && l.Texto == "María Pérez"), "nombre del cliente a doble tamaño");
        Datos.Cierto(texto.Contains("TOTAL $280.00"), "total");
        return Task.CompletedTask;
    }

    public static Task Prueba_TicketLimpiaCaracteresDeControl()
    {
        var malicioso = DatosTicketPrueba.Domicilio() with { Cliente = "Ana\u001Bp\u0000\u001D Ruiz" };
        var doc = FormatoTicket.Crear(malicioso, DatosTicketPrueba.Config(), DatosTicketPrueba.Fecha, false);
        Datos.Cierto(doc.Lineas.All(l => !l.Texto.Any(char.IsControl)), "sin caracteres de control en las líneas");
        return Task.CompletedTask;
    }

    public static async Task Prueba_ServicioImprimeEnLaCajaDelRestauranteDelOperador()
    {
        var (repo, impresora, s) = DatosTicketPrueba.Crear();
        var r = await s.ImprimirAsync(7, " LY-00000127 ", true, default);
        Datos.Igual((7L, "LY-00000127"), repo.Consultas.Single(), "operador y folio sin espacios");
        Datos.Igual("Caja", r.Impresora, "impresora");
        Datos.Cierto(r.Reimpresion && impresora.Enviados.Single().Doc.ComoTexto().Contains("REIMPRESIÓN"), "reimpresión");
    }

    public static async Task Prueba_ServicioRechazaCanceladoAjenoYSinImpresora()
    {
        var (repo, _, s) = DatosTicketPrueba.Crear();
        await Datos.Lanza<PedidoNoEncontradoException>(() => s.ImprimirAsync(7, "LY-OTRO", false, default));
        await Datos.Lanza<SolicitudInvalidaException>(() => s.ImprimirAsync(7, new string('x', 31), false, default));
        repo.Caja = null;
        await Datos.Lanza<ImpresoraNoConfiguradaException>(() => s.ImprimirAsync(7, "LY-00000127", false, default));
        repo.Pedido = DatosTicketPrueba.Domicilio() with { Estado = "Cancelado" };
        await Datos.Lanza<TicketNoPermitidoException>(() => s.VistaPreviaAsync(7, "LY-00000127", false, default));
    }

    public static async Task Prueba_DespachoNoFallaSiLaImpresoraNoResponde()
    {
        var (repo, impresora, s) = DatosTicketPrueba.Crear();
        Datos.Cierto(ServicioTicket.EsDespacho("Enviado") && ServicioTicket.EsDespacho("Listo para recoger") && !ServicioTicket.EsDespacho("Entregado"), "estados de despacho");
        var ok = await s.ImprimirEnDespachoAsync(7, "LY-00000127", default);
        Datos.Cierto(ok is { Impreso: true, Impresora: "Caja", Aviso: null }, "impreso al despachar");
        impresora.Caida = true;
        var caida = await s.ImprimirEnDespachoAsync(7, "LY-00000127", default);
        Datos.Cierto(caida is { Impreso: false } && caida.Aviso!.Contains("Reimprimir ticket"), "aviso con impresora caída");
        repo.Caja = null;
        var sinCaja = await s.ImprimirEnDespachoAsync(7, "LY-00000127", default);
        Datos.Cierto(sinCaja is { Impreso: false } && sinCaja.Aviso!.Contains("impresora de caja"), "aviso sin impresora configurada");
    }

    public static async Task Prueba_HojaDePruebaUsaElRestauranteDelAdministrador()
    {
        var (repo, impresora, s) = DatosTicketPrueba.Crear();
        await s.ImprimirPruebaAsync(1, default);
        Datos.Cierto(impresora.Prueba && impresora.Enviados.Single().Doc.ComoTexto().Contains("192.168.123.100"), "hoja de prueba con la IP");
        repo.Admin = null;
        await Datos.Lanza<RestauranteNoConfiguradoException>(() => s.ImprimirPruebaAsync(99, default));
    }
}
