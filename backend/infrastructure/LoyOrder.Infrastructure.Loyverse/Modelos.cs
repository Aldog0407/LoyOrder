using System.Text.Json.Serialization;

namespace LoyOrder.Infrastructure.Loyverse.Modelos;

// Objetos tal como los devuelve la API de Loyverse v1.0. Los nombres JSON de los campos marcados con (P##)
// se comprobaron en las pruebas de la actividad 2.5; los demás salen de la documentación oficial y conviene
// verificarlos con Postman antes de usarlos en producción.

public sealed class Tienda
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";                 // P04
    [JsonPropertyName("name")] public string Nombre { get; set; } = "";           // P04
    [JsonPropertyName("deleted_at")] public DateTimeOffset? EliminadaEn { get; set; }
}

public sealed class Categoria
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";                 // P05
    [JsonPropertyName("name")] public string Nombre { get; set; } = "";           // P05
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("deleted_at")] public DateTimeOffset? EliminadaEn { get; set; } // P05
}

public sealed class Articulo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";                 // P06
    [JsonPropertyName("item_name")] public string Nombre { get; set; } = "";      // P06
    /// <summary>Puede venir con HTML si se editó en el Back Office (P06): limpiar antes de mostrar.</summary>
    [JsonPropertyName("description")] public string? Descripcion { get; set; }    // P06
    [JsonPropertyName("category_id")] public string? CategoriaId { get; set; }
    [JsonPropertyName("track_stock")] public bool ControlaInventario { get; set; }
    [JsonPropertyName("image_url")] public string? UrlImagen { get; set; }        // P06
    [JsonPropertyName("option1_name")] public string? NombreOpcion1 { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset ActualizadoEn { get; set; } // P06
    [JsonPropertyName("deleted_at")] public DateTimeOffset? EliminadoEn { get; set; }
    [JsonPropertyName("variants")] public List<Variante> Variantes { get; set; } = new(); // P06
}

public sealed class Variante
{
    [JsonPropertyName("variant_id")] public string Id { get; set; } = "";         // P06
    [JsonPropertyName("item_id")] public string? ArticuloId { get; set; }
    [JsonPropertyName("sku")] public string? Sku { get; set; }                   // P06
    [JsonPropertyName("option1_value")] public string? ValorOpcion1 { get; set; }
    [JsonPropertyName("default_price")] public decimal? PrecioBase { get; set; }
    [JsonPropertyName("stores")] public List<VarianteEnTienda> Tiendas { get; set; } = new(); // P06

    /// <summary>Datos de la variante en una tienda; null si la variante no está en esa tienda.</summary>
    public VarianteEnTienda? EnTienda(string tiendaId) => Tiendas.FirstOrDefault(t => t.TiendaId == tiendaId);
}

public sealed class VarianteEnTienda
{
    [JsonPropertyName("store_id")] public string TiendaId { get; set; } = "";     // P06
    [JsonPropertyName("price")] public decimal? Precio { get; set; }              // P06
    [JsonPropertyName("available_for_sale")] public bool DisponibleParaVenta { get; set; } // P06
}

public sealed class NivelInventario
{
    [JsonPropertyName("variant_id")] public string VarianteId { get; set; } = ""; // P10
    [JsonPropertyName("store_id")] public string TiendaId { get; set; } = "";
    [JsonPropertyName("in_stock")] public decimal Existencias { get; set; }       // P10 (puede ser negativo: P17)
    [JsonPropertyName("updated_at")] public DateTimeOffset? ActualizadoEn { get; set; }
}

public sealed class TipoPago
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";                 // P11
    [JsonPropertyName("name")] public string Nombre { get; set; } = "";           // P11
    /// <summary>CASH, NONINTEGRATEDCARD, etc. (P11).</summary>
    [JsonPropertyName("type")] public string Tipo { get; set; } = "";
    [JsonPropertyName("deleted_at")] public DateTimeOffset? EliminadoEn { get; set; } // P11
}

// ---------------------------------------------------------------- registro de venta (CU-20)

/// <summary>Cuerpo de POST /receipts. Loyverse acepta un solo pago por venta (P15).</summary>
public sealed class NuevoRecibo
{
    [JsonPropertyName("store_id")] public string TiendaId { get; set; } = "";     // P12
    /// <summary>Folio de LoyOrder; sirve para buscar la venta y no duplicarla (RN08).</summary>
    [JsonPropertyName("order")] public string Folio { get; set; } = "";           // P12
    [JsonPropertyName("source")] public string Origen { get; set; } = "LoyOrder"; // P12
    /// <summary>Aquí va el tipo de entrega: dining_option se ignora (P12).</summary>
    [JsonPropertyName("note")] public string? Nota { get; set; }                  // P12
    [JsonPropertyName("line_items")] public List<LineaRecibo> Lineas { get; set; } = new(); // P12
    [JsonPropertyName("payments")] public List<PagoRecibo> Pagos { get; set; } = new();     // P12
}

public sealed class LineaRecibo
{
    [JsonPropertyName("variant_id")] public string VarianteId { get; set; } = ""; // P12
    [JsonPropertyName("quantity")] public decimal Cantidad { get; set; }          // P12
    [JsonPropertyName("line_note")] public string? Nota { get; set; }             // P12
}

public sealed class PagoRecibo
{
    [JsonPropertyName("payment_type_id")] public string TipoPagoId { get; set; } = ""; // P12
    [JsonPropertyName("paid_at")] public DateTimeOffset PagadoEn { get; set; }    // P12
}

public sealed class Recibo
{
    [JsonPropertyName("receipt_number")] public string Numero { get; set; } = ""; // P12
    [JsonPropertyName("order")] public string? Folio { get; set; }                // P12
    [JsonPropertyName("source")] public string? Origen { get; set; }              // P12
    [JsonPropertyName("total_money")] public decimal Total { get; set; }          // P12
    [JsonPropertyName("note")] public string? Nota { get; set; }
}

// ---------------------------------------------------------------- envoltorios internos de respuesta

internal interface IPagina<T>
{
    string? Cursor { get; }
    List<T> Elementos { get; }
}

internal sealed class PaginaTiendas : IPagina<Tienda>
{
    [JsonPropertyName("stores")] public List<Tienda> Elementos { get; set; } = new();
    [JsonPropertyName("cursor")] public string? Cursor { get; set; }
}

internal sealed class PaginaCategorias : IPagina<Categoria>
{
    [JsonPropertyName("categories")] public List<Categoria> Elementos { get; set; } = new();
    [JsonPropertyName("cursor")] public string? Cursor { get; set; }
}

internal sealed class PaginaArticulos : IPagina<Articulo>
{
    [JsonPropertyName("items")] public List<Articulo> Elementos { get; set; } = new();
    [JsonPropertyName("cursor")] public string? Cursor { get; set; }
}

internal sealed class PaginaInventario : IPagina<NivelInventario>
{
    [JsonPropertyName("inventory_levels")] public List<NivelInventario> Elementos { get; set; } = new();
    [JsonPropertyName("cursor")] public string? Cursor { get; set; }
}

internal sealed class PaginaTiposPago : IPagina<TipoPago>
{
    [JsonPropertyName("payment_types")] public List<TipoPago> Elementos { get; set; } = new();
    [JsonPropertyName("cursor")] public string? Cursor { get; set; }
}

internal sealed class PaginaRecibos : IPagina<Recibo>
{
    [JsonPropertyName("receipts")] public List<Recibo> Elementos { get; set; } = new();
    [JsonPropertyName("cursor")] public string? Cursor { get; set; }
}

internal sealed class RespuestaError
{
    [JsonPropertyName("errors")] public List<DetalleError>? Errores { get; set; }
}

internal sealed class DetalleError
{
    [JsonPropertyName("code")] public string? Codigo { get; set; }
    [JsonPropertyName("details")] public string? Detalle { get; set; }
    [JsonPropertyName("field")] public string? Campo { get; set; }
}
