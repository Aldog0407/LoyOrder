namespace LoyOrder.Infrastructure.Loyverse;

/// <summary>
/// Configuración del cliente de Loyverse. Se lee de la sección "Loyverse" de appsettings.json.
/// El token NO va aquí: lo entrega <see cref="IProveedorTokenLoyverse"/> (en producción sale cifrado de la base de datos, CU-08 RN-01).
/// </summary>
public sealed class LoyverseOptions
{
    public const string Seccion = "Loyverse";

    /// <summary>URL base de la API. Debe terminar en "/".</summary>
    public string UrlBase { get; set; } = "https://api.loyverse.com/v1.0/";

    /// <summary>Registros por página (la API permite hasta 250).</summary>
    public int TamanoPagina { get; set; } = 250;

    /// <summary>
    /// Máximo de peticiones dentro de <see cref="VentanaLimite"/>. Loyverse permite 300 cada 300 s por cuenta
    /// (CU-10 RN-02); se deja un margen de 10 por si alguien más usa la cuenta (Postman, pruebas).
    /// </summary>
    public int MaxPeticionesPorVentana { get; set; } = 290;

    public TimeSpan VentanaLimite { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>Reintentos ante 429, errores 5xx o fallas de red (CU-10 EX-02: hasta 3).</summary>
    public int MaxReintentos { get; set; } = 3;

    /// <summary>Espera base del primer reintento; se duplica en cada intento (2 s, 4 s, 8 s).</summary>
    public TimeSpan EsperaBaseReintento { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan TiempoMaximoPeticion { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tope de páginas por consulta, para no quedar en un ciclo infinito si la API repitiera el cursor.</summary>
    public int MaxPaginas { get; set; } = 1000;
}
