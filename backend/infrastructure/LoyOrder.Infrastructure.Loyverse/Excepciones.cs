using System.Net;

namespace LoyOrder.Infrastructure.Loyverse;

/// <summary>Error devuelto por la API de Loyverse o al comunicarse con ella.</summary>
public class LoyverseException : Exception
{
    public HttpStatusCode? Estado { get; }

    /// <summary>Código de error de Loyverse (por ejemplo NOT_FOUND o INVALID_VALUE), si la respuesta lo trae.</summary>
    public string? Codigo { get; }

    public LoyverseException(string mensaje, HttpStatusCode? estado = null, string? codigo = null, Exception? interna = null)
        : base(mensaje, interna)
    {
        Estado = estado;
        Codigo = codigo;
    }
}

/// <summary>401: el token no existe, se revocó o es inválido (CU-08 EX-01, CU-10 EX-01). No se reintenta.</summary>
public sealed class LoyverseNoAutorizadoException : LoyverseException
{
    public LoyverseNoAutorizadoException()
        : base("Loyverse rechazó el token de acceso.", HttpStatusCode.Unauthorized, "UNAUTHORIZED") { }
}

/// <summary>429 que siguió después de todos los reintentos (CU-10 EX-02).</summary>
public sealed class LoyverseLimiteExcedidoException : LoyverseException
{
    public LoyverseLimiteExcedidoException(int intentos)
        : base($"Loyverse siguió respondiendo 429 después de {intentos} intentos.", HttpStatusCode.TooManyRequests) { }
}

/// <summary>Loyverse no respondió o respondió 5xx después de los reintentos (CU-10 EX-03, CU-08 EX-02).</summary>
public sealed class LoyverseNoDisponibleException : LoyverseException
{
    public LoyverseNoDisponibleException(string mensaje, HttpStatusCode? estado = null, Exception? interna = null)
        : base(mensaje, estado, null, interna) { }
}

/// <summary>Todavía no se ha capturado un token (CU-08 no se ha completado).</summary>
public sealed class LoyverseNoConfiguradoException : LoyverseException
{
    public LoyverseNoConfiguradoException()
        : base("No hay un token de Loyverse configurado.") { }
}
