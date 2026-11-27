namespace LoyOrder.Application;

// Actividad 7.2.11 · CU-19 Imprimir ticket de venta (req. 16). Diseño 5.4: papel de 80 mm, 48 columnas.
// El ticket se imprime en la impresora de caja al despachar (Enviado / Listo para recoger, 7.2.23) y el operador lo
// puede reimprimir con la marca "*** REIMPRESIÓN ***". Matriz 4.2: CU-19 solo Operador; la hoja de prueba de la
// impresora es configuración de impresoras (CU-09), solo Administrador.

// ------------------------------------------------------------------ datos

public sealed record ArticuloTicket(string Nombre, string? Variante, int Cantidad, decimal PrecioUnitario, decimal Importe,
    string? Indicaciones);

public sealed record DatosTicket(long IdRestaurante, string Restaurante, string Folio, string Estado, string TipoEntrega,
    string Cliente, string? TelefonoCliente, string? Calle, string? Numero, string? Colonia, string? Referencias,
    string MetodoPago, decimal Subtotal, decimal CostoEnvio, decimal Total, IReadOnlyList<ArticuloTicket> Articulos);

/// <param name="Direccion">IP de la impresora en la red local (tabla impresora.direccion_ip).</param>
public sealed record ImpresoraDestino(long IdImpresora, string Nombre, string Direccion);

/// <param name="DireccionRestaurante">La tabla restaurante aún no tiene dirección ni teléfono (CU-08): se toman de la configuración.</param>
/// <param name="Zona">La misma zona horaria de los pedidos (Pedidos:ZonaHoraria).</param>
public sealed record ConfiguracionTicket(string? DireccionRestaurante, string? TelefonoRestaurante, TimeZoneInfo Zona);

public sealed record ResultadoImpresion(string Folio, string Impresora, DateTimeOffset Fecha, bool Reimpresion);

/// <summary>Resultado de imprimir el ticket al despachar. El cambio de estado ya quedó guardado aunque no se imprima.</summary>
public sealed record TicketDespacho(bool Impreso, string? Impresora, string? Aviso);

public static class TiposEntrega
{
    public const string Domicilio = "Domicilio";
    public const string Recoleccion = "Recolección";
}

// ------------------------------------------------------------------ puertos

public interface ITicketRepository
{
    /// <summary>Pedido del restaurante del operador (empleado activo con rol OPERADOR); null si no existe o es de otro restaurante.</summary>
    Task<DatosTicket?> ObtenerAsync(long idOperador, string folio, CancellationToken ct);
    /// <summary>Restaurante del administrador activo; null si el usuario no es administrador.</summary>
    Task<(long IdRestaurante, string Nombre)?> RestauranteDeAdministradorAsync(long idAdministrador, CancellationToken ct);
    /// <summary>Primera impresora activa de tipo 'Caja' del restaurante.</summary>
    Task<ImpresoraDestino?> ImpresoraCajaAsync(long idRestaurante, CancellationToken ct);
}

/// <summary>Impresora térmica ESC/POS. La implementa Infrastructure.Impresion (TCP 9100).</summary>
public interface IImpresoraTermica
{
    Task ImprimirAsync(ImpresoraDestino destino, DocumentoImpresion documento, CancellationToken ct);
    /// <summary>Imprime el encabezado y una muestra de acentos en cada página de códigos, para elegir la correcta.</summary>
    Task ImprimirPruebaAsync(ImpresoraDestino destino, DocumentoImpresion encabezado, CancellationToken ct);
}

public sealed class TicketNoPermitidoException(string mensaje) : Exception(mensaje);
public sealed class ImpresoraNoConfiguradaException(string mensaje) : Exception(mensaje);
/// <summary>La impresora no respondió (apagada, sin red o IP equivocada). La API responde 503.</summary>
public sealed class ImpresoraNoDisponibleException(string impresora) : Exception($"No se pudo conectar con la impresora {impresora}.")
{
    public string Impresora { get; } = impresora;
}

// ------------------------------------------------------------------ formato (diseño 5.4, PNG 5 y 6)

public static class FormatoTicket
{
    public static DocumentoImpresion Crear(DatosTicket t, ConfiguracionTicket config, DateTimeOffset fecha, bool reimpresion)
    {
        var l = new List<LineaImpresion>();
        void Agregar(IEnumerable<string> textos, Alineacion a = Alineacion.Izquierda, bool negrita = false, bool doble = false)
        {
            foreach (var texto in textos) l.Add(new(texto, a, negrita, doble));
        }
        void Separador(char c) => l.Add(new(Papel.Linea(c)));
        void Banda(string texto) => l.Add(new(Papel.Banda(texto), Alineacion.Izquierda, true, false, true));

        // Encabezado del restaurante
        Agregar(Papel.Partir(t.Restaurante, Papel.AnchoDoble), Alineacion.Centro, true, true);
        if (!string.IsNullOrWhiteSpace(config.DireccionRestaurante)) Agregar(Papel.Partir(config.DireccionRestaurante, Papel.Ancho), Alineacion.Centro);
        if (!string.IsNullOrWhiteSpace(config.TelefonoRestaurante)) Agregar(Papel.Partir("Tel. " + config.TelefonoRestaurante, Papel.Ancho), Alineacion.Centro);
        if (reimpresion) Banda("*** REIMPRESIÓN ***");
        Separador('=');

        // Pedido
        Agregar(["PEDIDO EN LÍNEA"], Alineacion.Centro, true, true);
        var local = TimeZoneInfo.ConvertTime(fecha, config.Zona);
        Agregar(Papel.DosColumnas("Folio: " + Papel.Limpiar(t.Folio), local.ToString("dd/MM/yyyy HH:mm")), negrita: true);
        Separador('-');
        Agregar(Papel.DosColumnas("Cant. Artículo", "Importe"), negrita: true);
        Separador('-');
        foreach (var a in t.Articulos)
        {
            Agregar(Papel.DosColumnas($"{a.Cantidad} x {NombreArticulo(a)}", Papel.Dinero(a.Importe)), negrita: true);
            foreach (var indicacion in (a.Indicaciones ?? "").Split('\n').Select(Papel.Limpiar).Where(i => i.Length > 0))
                foreach (var parte in Papel.Partir("> " + indicacion, Papel.Ancho - 4, 2)) l.Add(new("    " + parte, Negrita: true));
            if (a.Cantidad > 1) l.Add(new($"    {a.Cantidad} x {Papel.Dinero(a.PrecioUnitario)}"));
        }
        Separador('-');

        // Totales y pago
        Agregar(Papel.DosColumnas("Subtotal", Papel.Dinero(t.Subtotal)));
        if (t.CostoEnvio > 0) Agregar(Papel.DosColumnas("Costo de envío", Papel.Dinero(t.CostoEnvio)));
        l.Add(new("TOTAL " + Papel.Dinero(t.Total), Alineacion.Derecha, true, true));
        Agregar(Papel.DosColumnas("Método de pago:", Papel.Limpiar(t.MetodoPago).ToUpperInvariant()), negrita: true);
        l.Add(new("Se paga al recibir el pedido."));
        Separador('=');

        // Entrega
        if (t.TipoEntrega == TiposEntrega.Domicilio)
        {
            Banda("ENTREGA A DOMICILIO");
            Agregar(Papel.Partir("Cliente: " + t.Cliente, Papel.Ancho, 4), negrita: true);
            if (!string.IsNullOrWhiteSpace(t.TelefonoCliente)) Agregar(Papel.Partir("Tel.: " + t.TelefonoCliente, Papel.Ancho), negrita: true);
            var calle = string.Join(' ', new[] { t.Calle, t.Numero }.Select(Papel.Limpiar).Where(x => x.Length > 0));
            var colonia = Papel.Limpiar(t.Colonia);
            if (calle.Length > 0 || colonia.Length > 0)
                Agregar(Papel.Partir(colonia.Length > 0 ? $"{calle}, Col. {colonia}".TrimStart(',', ' ') : calle, Papel.Ancho, 4));
            if (!string.IsNullOrWhiteSpace(t.Referencias)) Agregar(Papel.Partir("Ref.: " + t.Referencias, Papel.Ancho, 6));
        }
        else
        {
            Banda("RECOGER EN EL LOCAL");
            l.Add(new("Cliente:", Negrita: true));
            Agregar(Papel.Partir(t.Cliente, Papel.AnchoDoble), negrita: true, doble: true);
        }
        Separador('=');

        // Pie
        l.Add(new("Comprobante de pedido. No es factura.", Alineacion.Centro));
        l.Add(new("¡Gracias por tu pedido!", Alineacion.Centro, true));
        return new DocumentoImpresion(l);
    }

    /// <summary>"Alitas" + "10 piezas" = "Alitas 10 piezas"; si el nombre guardado ya trae la variante, no se repite.</summary>
    public static string NombreArticulo(ArticuloTicket a)
    {
        var nombre = Papel.Limpiar(a.Nombre);
        var variante = Papel.Limpiar(a.Variante);
        return variante.Length == 0 || nombre.Contains(variante, StringComparison.OrdinalIgnoreCase) ? nombre : $"{nombre} {variante}";
    }
}

// ------------------------------------------------------------------ caso de uso

public sealed class ServicioTicket(ITicketRepository repositorio, IImpresoraTermica impresora, ConfiguracionTicket config,
    TimeProvider reloj)
{
    /// <summary>Estados en los que se despacha el pedido y sale el ticket original.</summary>
    public static bool EsDespacho(string estado) => estado is "Enviado" or "Listo para recoger";

    public async Task<DocumentoImpresion> VistaPreviaAsync(long idOperador, string folio, bool reimpresion, CancellationToken ct) =>
        FormatoTicket.Crear(await PedidoAsync(idOperador, folio, ct), config, reloj.GetUtcNow(), reimpresion);

    /// <summary>Imprime el ticket en la impresora de caja. Lanza si no hay impresora o no responde.</summary>
    public async Task<ResultadoImpresion> ImprimirAsync(long idOperador, string folio, bool reimpresion, CancellationToken ct)
    {
        var pedido = await PedidoAsync(idOperador, folio, ct);
        var destino = await repositorio.ImpresoraCajaAsync(pedido.IdRestaurante, ct)
            ?? throw new ImpresoraNoConfiguradaException("No hay una impresora de caja activa configurada.");
        var fecha = reloj.GetUtcNow();
        await impresora.ImprimirAsync(destino, FormatoTicket.Crear(pedido, config, fecha, reimpresion), ct);
        return new ResultadoImpresion(pedido.Folio, destino.Nombre, fecha, reimpresion);
    }

    /// <summary>
    /// Ticket original al despachar (CU-19 paso 1). No lanza por la impresora: el estado del pedido ya se guardó,
    /// así que una falla solo regresa un aviso para que el operador use "Reimprimir ticket" (CU-19 FA 2.a).
    /// </summary>
    public async Task<TicketDespacho> ImprimirEnDespachoAsync(long idOperador, string folio, CancellationToken ct)
    {
        try
        {
            var r = await ImprimirAsync(idOperador, folio, false, ct);
            return new TicketDespacho(true, r.Impresora, null);
        }
        catch (ImpresoraNoDisponibleException e)
        {
            return new TicketDespacho(false, e.Impresora, $"El pedido se despachó, pero la impresora {e.Impresora} no respondió. Revísala y usa \"Reimprimir ticket\".");
        }
        catch (Exception e) when (e is ImpresoraNoConfiguradaException or DependenciaNoDisponibleException)
        {
            return new TicketDespacho(false, null, "El pedido se despachó, pero no se pudo imprimir el ticket: " +
                (e is ImpresoraNoConfiguradaException ? e.Message : "la base de datos no respondió."));
        }
    }

    /// <summary>Hoja de prueba de la impresora de caja del restaurante del administrador (CU-09).</summary>
    public async Task<ResultadoImpresion> ImprimirPruebaAsync(long idAdministrador, CancellationToken ct)
    {
        var restaurante = await repositorio.RestauranteDeAdministradorAsync(idAdministrador, ct)
            ?? throw new RestauranteNoConfiguradoException("Tu cuenta no está asignada a un restaurante como administrador.");
        var destino = await repositorio.ImpresoraCajaAsync(restaurante.IdRestaurante, ct)
            ?? throw new ImpresoraNoConfiguradaException("No hay una impresora de caja activa configurada.");
        var fecha = reloj.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(fecha, config.Zona);
        var encabezado = new DocumentoImpresion([
            .. Papel.Partir(restaurante.Nombre, Papel.AnchoDoble).Select(x => new LineaImpresion(x, Alineacion.Centro, true, true)),
            new("HOJA DE PRUEBA", Alineacion.Centro, true),
            new(Papel.Linea('=')),
            .. Papel.DosColumnas("Impresora:", Papel.Limpiar(destino.Nombre)).Select(x => new LineaImpresion(x, Negrita: true)),
            .. Papel.DosColumnas("Dirección:", Papel.Limpiar(destino.Direccion)).Select(x => new LineaImpresion(x)),
            .. Papel.DosColumnas("Fecha:", local.ToString("dd/MM/yyyy HH:mm")).Select(x => new LineaImpresion(x)),
            new(Papel.Linea('-')),
            new("Texto normal"),
            new("Texto en negrita", Negrita: true),
            new("Doble", Alineacion.Centro, true, true),
            new(Papel.Banda("Banda invertida"), Negrita: true, Invertido: true),
            .. Papel.DosColumnas("Izquierda", "Derecha").Select(x => new LineaImpresion(x)),
            new(Papel.Linea('-')),
            new("Elige la página de códigos que muestre bien:"),
        ]);
        await impresora.ImprimirPruebaAsync(destino, encabezado, ct);
        return new ResultadoImpresion("PRUEBA", destino.Nombre, fecha, false);
    }

    private async Task<DatosTicket> PedidoAsync(long idOperador, string folio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folio) || folio.Length > 30)
            throw new SolicitudInvalidaException("Escribe un folio de hasta 30 caracteres.");
        var pedido = await repositorio.ObtenerAsync(idOperador, folio.Trim(), ct) ?? throw new PedidoNoEncontradoException();
        if (pedido.Estado == "Cancelado")
            throw new TicketNoPermitidoException("No se imprime el ticket de un pedido cancelado.");
        return pedido;
    }
}
