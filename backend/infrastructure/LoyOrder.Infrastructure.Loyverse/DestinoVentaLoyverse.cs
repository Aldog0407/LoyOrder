using LoyOrder.Application;
using LoyOrder.Infrastructure.Loyverse.Modelos;
using Microsoft.Extensions.Configuration;

namespace LoyOrder.Infrastructure.Loyverse;

public sealed class DestinoVentaLoyverse(ILoyverseClient cliente,IConfiguration configuracion) : IDestinoVenta
{
    public async Task<ResultadoVenta> RegistrarAsync(DatosVenta datos,bool soloVerificar,CancellationToken ct)
    {
        var envio=configuracion["Ventas:IdVarianteEnvio"];
        if(datos.Correlacion==Guid.Empty||string.IsNullOrWhiteSpace(datos.Folio)||datos.Folio.Length>20||!Guid.TryParse(datos.TiendaId,out _) || !Guid.TryParse(datos.PagoId,out _) || datos.Lineas.Count==0 ||
            datos.Lineas.Any(l=>!Guid.TryParse(l.VarianteId,out _) || l.Cantidad<=0 || l.Precio<=0) ||
            datos.CostoEnvio<0 || datos.Lineas.Sum(l=>l.Cantidad*l.Precio)+datos.CostoEnvio!=datos.Total ||
            (datos.CostoEnvio>0 && !Guid.TryParse(envio,out _)))
            return new(soloVerificar?"Por verificar":"Fallido",null,"Faltan vínculos reales de tienda, pago, artículos o variante de envío, o los importes no coinciden.");
        var lineas=datos.Lineas.Select(l=>new LineaRecibo { VarianteId=l.VarianteId,Cantidad=l.Cantidad,Precio=l.Precio,Nota=l.Nota,Impuestos=[] }).ToList();
        if(datos.CostoEnvio>0) lineas.Add(new() { VarianteId=envio!,Cantidad=1,Precio=datos.CostoEnvio,Nota="Envío de LoyOrder",Impuestos=[] });
        var enviando=false;
        try {
            var anterior=await cliente.BuscarReciboPorFolioAsync(datos.TiendaId,datos.Folio,ct:ct);
            if(anterior is not null) return Comprobar(anterior,datos,lineas);
            if(soloVerificar) return new("Por verificar",null,"Todavía no se encontró el recibo. No se reenvía automáticamente una venta incierta.");
            // Validación remota previa al primer POST: no enviar IDs de otras cuentas o pagos eliminados.
            var tiendas=await cliente.ObtenerTiendasAsync(ct);
            var pagos=await cliente.ObtenerTiposPagoAsync(ct);
            var articulos=await cliente.ObtenerArticulosAsync(ct:ct);
            if(!tiendas.Any(t=>t.Id==datos.TiendaId && t.EliminadaEn is null) || !pagos.Any(p=>p.Id==datos.PagoId && p.EliminadoEn is null) ||
                lineas.Any(l=>!articulos.Any(a=>a.EliminadoEn is null && a.Variantes.Any(v=>v.Id==l.VarianteId && v.EnTienda(datos.TiendaId)?.DisponibleParaVenta==true))))
                return new("Fallido",null,"La cuenta de Loyverse no contiene la tienda, pago o variantes disponibles del pedido.");
            enviando=true;
            var recibo=await cliente.CrearReciboAsync(new() { TiendaId=datos.TiendaId,Folio=datos.Folio,Origen="LoyOrder",
                Nota=$"{datos.TipoEntrega} · Pedido {datos.Folio} · LoyOrder-op:{datos.Correlacion:N}",Lineas=lineas,
                Pagos=[new() { TipoPagoId=datos.PagoId,PagadoEn=new DateTimeOffset(DateTime.SpecifyKind(datos.FechaEntrega,DateTimeKind.Utc)) }] },ct);
            return Comprobar(recibo,datos,lineas);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception e) when(e is LoyverseException or HttpRequestException or System.Text.Json.JsonException) {
            // Nunca guardar cuerpos de respuestas ni el token. Tras comenzar un POST la incertidumbre prevalece.
            var rechazo=e is LoyverseException le && le.Estado is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.TooManyRequests;
            var detalle=e.GetType().Name+(e is LoyverseException l ? $" HTTP {(int?)l.Estado}" : "");
            return new(soloVerificar||(enviando&&!rechazo)?"Por verificar":"Fallido",null,detalle);
        }
    }
    private static ResultadoVenta Comprobar(Recibo r,DatosVenta d,List<LineaRecibo> esperadas)
    {
        static IEnumerable<string> Firma(IEnumerable<LineaRecibo> ls)=>ls.GroupBy(l=>(l.VarianteId,l.Precio))
            .Select(g=>$"{g.Key.VarianteId}|{g.Key.Precio?.ToString("G29",System.Globalization.CultureInfo.InvariantCulture)}|{g.Sum(l=>l.Cantidad).ToString("G29",System.Globalization.CultureInfo.InvariantCulture)}").OrderBy(x=>x);
        if(string.IsNullOrWhiteSpace(r.Numero)||r.Numero.Length>50||r.Folio!=d.Folio||r.TiendaId!=d.TiendaId||r.Origen!="LoyOrder"||
            !(r.Nota??"").Contains($"LoyOrder-op:{d.Correlacion:N}",StringComparison.Ordinal)||r.Tipo!="SALE"||r.CanceladoEn is not null||r.Total!=d.Total||r.Pagos.Count!=1||r.Pagos[0].TipoPagoId!=d.PagoId||
            !Firma(r.Lineas).SequenceEqual(Firma(esperadas)))
            return new("Por verificar",null,"El recibo encontrado no coincide con el pedido. Revisar antes de reenviar.");
        return new("Confirmado",r.Numero,null);
    }
}
