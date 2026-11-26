using LoyOrder.Application;
namespace LoyOrder.Api;
public sealed class VentasPendientes(IServiceScopeFactory scopes,ILogger<VentasPendientes> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested) {
            try {
                await using var scope=scopes.CreateAsyncScope();
                if(await scope.ServiceProvider.GetRequiredService<RegistrarVentas>().ProcesarUnaAsync(ct)) continue;
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { return; }
            catch(Exception e) { logger.LogWarning("No se pudo procesar una venta ({Tipo}).",e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5),ct); } catch(OperationCanceledException) { return; }
        }
    }
}
