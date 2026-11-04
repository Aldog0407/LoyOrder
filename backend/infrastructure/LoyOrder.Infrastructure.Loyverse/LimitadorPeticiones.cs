namespace LoyOrder.Infrastructure.Loyverse;

/// <summary>Reloj inyectable para poder probar esperas sin dormir el proceso.</summary>
public interface IReloj
{
    DateTimeOffset Ahora { get; }
    Task EsperarAsync(TimeSpan tiempo, CancellationToken ct);
}

public sealed class RelojSistema : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
    public Task EsperarAsync(TimeSpan tiempo, CancellationToken ct) =>
        tiempo <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(tiempo, ct);
}

/// <summary>
/// Límite de uso con ventana deslizante: guarda la hora de las últimas peticiones y, si ya hay
/// <c>max</c> dentro de la ventana, espera a que salga la más antigua. Loyverse no manda encabezados
/// de límite (prueba P20), así que LoyOrder lleva su propia cuenta. Se registra como singleton para que
/// la sincronización y el registro de ventas compartan el mismo contador.
/// </summary>
public sealed class LimitadorPeticiones
{
    private readonly int _max;
    private readonly TimeSpan _ventana;
    private readonly IReloj _reloj;
    private readonly Queue<DateTimeOffset> _marcas = new();
    private readonly SemaphoreSlim _candado = new(1, 1);

    public LimitadorPeticiones(int max, TimeSpan ventana, IReloj reloj)
    {
        if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
        _max = max;
        _ventana = ventana;
        _reloj = reloj;
    }

    /// <summary>Peticiones registradas dentro de la ventana actual.</summary>
    public int EnVentana
    {
        get { lock (_marcas) { Purgar(_reloj.Ahora); return _marcas.Count; } }
    }

    /// <summary>Espera, si hace falta, hasta que se pueda hacer una petición más, y la registra.</summary>
    public async Task EsperarTurnoAsync(CancellationToken ct = default)
    {
        await _candado.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (true)
            {
                TimeSpan espera;
                lock (_marcas)
                {
                    var ahora = _reloj.Ahora;
                    Purgar(ahora);
                    if (_marcas.Count < _max)
                    {
                        _marcas.Enqueue(ahora);
                        return;
                    }
                    espera = _marcas.Peek() + _ventana - ahora;
                }
                await _reloj.EsperarAsync(espera, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _candado.Release();
        }
    }

    private void Purgar(DateTimeOffset ahora)
    {
        while (_marcas.Count > 0 && _marcas.Peek() + _ventana <= ahora)
            _marcas.Dequeue();
    }
}
