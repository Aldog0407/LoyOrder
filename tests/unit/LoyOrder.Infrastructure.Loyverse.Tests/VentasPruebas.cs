using System.Net;
using System.Text.Json;
using LoyOrder.Application;
using LoyOrder.Infrastructure.Loyverse;
using Microsoft.Extensions.Configuration;

static partial class Pruebas
{
    const string TiendaVenta="00000000-0000-0000-0000-000000000001";
    const string PagoVenta="00000000-0000-0000-0000-000000000002";
    const string VarianteVenta="00000000-0000-0000-0000-000000000003";
    static DatosVenta DatosVentaPrueba()=>new(1,"LY-123",TiendaVenta,PagoVenta,"Recolección",100,0,DateTime.UtcNow,[new(VarianteVenta,1,100,null)],Guid.Parse("00000000-0000-0000-0000-000000000004"));
    static string ReciboVenta(decimal total=100)=>JsonSerializer.Serialize(new { receipt_number="1-200",order="LY-123",source="LoyOrder",store_id=TiendaVenta,
        note="Recolección · LoyOrder-op:00000000000000000000000000000004",receipt_type="SALE",total_money=total,line_items=new[] {new {variant_id=VarianteVenta,quantity=1,price=100.00m}},payments=new[] {new {payment_type_id=PagoVenta}} });
    static void PrepararVenta(HandlerFalso h) {
        h.Responder(HttpStatusCode.OK,"{\"receipts\":[]}");
        h.Responder(HttpStatusCode.OK,JsonSerializer.Serialize(new {stores=new[] {new {id=TiendaVenta}}}));
        h.Responder(HttpStatusCode.OK,JsonSerializer.Serialize(new {payment_types=new[] {new {id=PagoVenta}}}));
        h.Responder(HttpStatusCode.OK,JsonSerializer.Serialize(new {items=new[] {new {id="i",variants=new[] {new {variant_id=VarianteVenta,stores=new[] {new {store_id=TiendaVenta,available_for_sale=true}}}}}}}));
    }
    public static async Task Prueba_VentaConfirmaImportesHistoricosYUnPago() {
        var(c,h,_)=Fabrica.Crear();PrepararVenta(h);h.Responder(HttpStatusCode.OK,ReciboVenta());
        var r=await new DestinoVentaLoyverse(c,new ConfigurationBuilder().Build()).RegistrarAsync(DatosVentaPrueba(),false,default);
        Afirmar.Igual("Confirmado",r.Estado);Afirmar.Igual("1-200",r.Recibo);
        var post=h.Peticiones.Single(p=>p.Metodo==HttpMethod.Post);using var json=JsonDocument.Parse(post.Cuerpo!);
        Afirmar.Igual(100m,json.RootElement.GetProperty("line_items")[0].GetProperty("price").GetDecimal());
        Afirmar.Igual(1,json.RootElement.GetProperty("payments").GetArrayLength());
    }
    public static async Task Prueba_VentaInciertaNoRepiteElPost() {
        var(c,h,_)=Fabrica.Crear();PrepararVenta(h);h.FallarRed();
        var r=await new DestinoVentaLoyverse(c,new ConfigurationBuilder().Build()).RegistrarAsync(DatosVentaPrueba(),false,default);
        Afirmar.Igual("Por verificar",r.Estado);Afirmar.Igual(1,h.Peticiones.Count(p=>p.Metodo==HttpMethod.Post));
        var(c2,h2,_r)=Fabrica.Crear();h2.Responder(HttpStatusCode.OK,"{\"receipts\":[]}");
        var r2=await new DestinoVentaLoyverse(c2,new ConfigurationBuilder().Build()).RegistrarAsync(DatosVentaPrueba(),true,default);
        Afirmar.Igual("Por verificar",r2.Estado);Afirmar.Igual(0,h2.Peticiones.Count(p=>p.Metodo==HttpMethod.Post));
    }
    public static async Task Prueba_VentaReconciliadaNoCreaOtroRecibo() {
        var(c,h,_)=Fabrica.Crear();h.Responder(HttpStatusCode.OK,"{\"receipts\":["+ReciboVenta()+"]}");
        var r=await new DestinoVentaLoyverse(c,new ConfigurationBuilder().Build()).RegistrarAsync(DatosVentaPrueba(),true,default);
        Afirmar.Igual("Confirmado",r.Estado);Afirmar.Igual(1,h.Peticiones.Count);
        Afirmar.Verdadero(h.Peticiones[0].Url.Contains("order=LY-123")&&h.Peticiones[0].Url.Contains("source=LoyOrder"),"Filtros exactos");
    }
    public static async Task Prueba_VentaNoAsociaUnReciboDeImporteDistinto() {
        var(c,h,_)=Fabrica.Crear();h.Responder(HttpStatusCode.OK,"{\"receipts\":["+ReciboVenta(101)+"]}");
        var r=await new DestinoVentaLoyverse(c,new ConfigurationBuilder().Build()).RegistrarAsync(DatosVentaPrueba(),false,default);
        Afirmar.Igual("Por verificar",r.Estado);Afirmar.Igual(1,h.Peticiones.Count);
    }
    public static async Task Prueba_VentaRechazaDatosFicticiosYEnvioSinVinculo() {
        var(c,h,_)=Fabrica.Crear();var destino=new DestinoVentaLoyverse(c,new ConfigurationBuilder().Build());
        Afirmar.Igual("Fallido",(await destino.RegistrarAsync(DatosVentaPrueba() with { PagoId="PRUEBA-LOCAL-PAGO" },false,default)).Estado);
        Afirmar.Igual("Fallido",(await destino.RegistrarAsync(DatosVentaPrueba() with { Total=125,CostoEnvio=25 },false,default)).Estado);
        Afirmar.Igual("Por verificar",(await destino.RegistrarAsync(DatosVentaPrueba() with { PagoId="PRUEBA-LOCAL-PAGO" },true,default)).Estado);
        Afirmar.Igual(0,h.Peticiones.Count);
    }
    public static async Task Prueba_VentaRechazada400PuedeReintentarseYUnFolioAjenoNoSeAsocia() {
        var(c,h,_)=Fabrica.Crear();PrepararVenta(h);h.Responder(HttpStatusCode.BadRequest,"{\"errors\":[{\"code\":\"INVALID_VALUE\"}]}");
        Afirmar.Igual("Fallido",(await new DestinoVentaLoyverse(c,new ConfigurationBuilder().Build()).RegistrarAsync(DatosVentaPrueba(),false,default)).Estado);
        var(c2,h2,_r)=Fabrica.Crear();h2.Responder(HttpStatusCode.OK,"{\"receipts\":["+ReciboVenta().Replace("00000000000000000000000000000004","00000000000000000000000000000005")+"]}");
        Afirmar.Igual("Por verificar",(await new DestinoVentaLoyverse(c2,new ConfigurationBuilder().Build()).RegistrarAsync(DatosVentaPrueba(),false,default)).Estado);
        Afirmar.Igual(1,h2.Peticiones.Count);
    }
    public static async Task Prueba_VentaIncluyeElCostoDeEnvioComoLineaSeparada() {
        const string envio="00000000-0000-0000-0000-000000000006";
        var(c,h,_)=Fabrica.Crear();
        h.Responder(HttpStatusCode.OK,"{\"receipts\":[]}");
        h.Responder(HttpStatusCode.OK,JsonSerializer.Serialize(new {stores=new[] {new {id=TiendaVenta}}}));
        h.Responder(HttpStatusCode.OK,JsonSerializer.Serialize(new {payment_types=new[] {new {id=PagoVenta}}}));
        h.Responder(HttpStatusCode.OK,JsonSerializer.Serialize(new {items=new[] {new {id="i",variants=new[] {VarianteVenta,envio}.Select(v=>new {variant_id=v,stores=new[] {new {store_id=TiendaVenta,available_for_sale=true}}}).ToArray()}}}));
        var respuesta=JsonSerializer.Serialize(new {receipt_number="1-201",order="LY-123",source="LoyOrder",store_id=TiendaVenta,receipt_type="SALE",
            note="Domicilio · LoyOrder-op:00000000000000000000000000000004",total_money=125,line_items=new[] {new {variant_id=VarianteVenta,quantity=1,price=100},new {variant_id=envio,quantity=1,price=25}},payments=new[] {new {payment_type_id=PagoVenta}}});
        h.Responder(HttpStatusCode.OK,respuesta);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Ventas:IdVarianteEnvio"]=envio }).Build();
        var r=await new DestinoVentaLoyverse(c,config).RegistrarAsync(DatosVentaPrueba() with {TipoEntrega="Domicilio",CostoEnvio=25,Total=125},false,default);
        Afirmar.Igual("Confirmado",r.Estado);
        using var json=JsonDocument.Parse(h.Peticiones.Single(p=>p.Metodo==HttpMethod.Post).Cuerpo!);
        Afirmar.Igual(25m,json.RootElement.GetProperty("line_items")[1].GetProperty("price").GetDecimal());
    }
    public static async Task Prueba_VentaBuscaEnTodasLasPaginasYDetectaDuplicados() {
        var(c,h,_)=Fabrica.Crear();h.Responder(HttpStatusCode.OK,"{\"receipts\":[],\"cursor\":\"pagina2\"}");
        h.Responder(HttpStatusCode.OK,"{\"receipts\":["+ReciboVenta()+"]}");
        Afirmar.Igual("1-200",(await c.BuscarReciboPorFolioAsync(TiendaVenta,"LY-123"))?.Numero);
        Afirmar.Verdadero(h.Peticiones[1].Url.Contains("cursor=pagina2"),"Falta paginación");
        var(c2,h2,_r)=Fabrica.Crear();h2.Responder(HttpStatusCode.OK,"{\"receipts\":["+ReciboVenta()+","+ReciboVenta()+"]}");
        await Afirmar.LanzaAsync<LoyverseException>(()=>c2.BuscarReciboPorFolioAsync(TiendaVenta,"LY-123"));
    }
}
