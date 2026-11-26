using LoyOrder.Application;

namespace LoyOrder.Api;

public sealed class NotificacionesPedidos(IServiceScopeFactory scopes,ILogger<NotificacionesPedidos> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        while (!detener.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<INotificacionPedidoRepository>();
                var correo = scope.ServiceProvider.GetRequiredService<IEnvioCorreo>();
                if (await repo.ProcesarUnaAsync(async (mensaje,ct) => {
                    using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    limite.CancelAfter(TimeSpan.FromSeconds(20));
                    await correo.EnviarAsync(mensaje,limite.Token);
                },detener)) continue;
            }
            catch (OperationCanceledException) when (detener.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning("No se pudo procesar un aviso de pedido ({Tipo}).",e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5),detener); }
            catch (OperationCanceledException) { return; }
        }
    }
}
