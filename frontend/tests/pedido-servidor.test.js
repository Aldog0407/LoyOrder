import test from 'node:test';
import assert from 'node:assert/strict';
import { prepararPedido, guardarPedidoConfirmado, adaptarHistorialPedido } from '../src/utils/pedido-servidor.js';
import { defaultConfig } from '../src/config/defaults.js';

const estado = () => ({ config: structuredClone(defaultConfig), cart: [{dishId:1,variantId:11,name:'Prueba',variant:'Normal',quantity:2,price:100}],
  delivery:'local', payment:'3', address:'Calle ejemplo 1',pin:{lat:19.54,lng:-96.92}, orders:[],
  checkoutOptions:{abierto:true,costoEnvio:25,metodosPago:[{id:3,nombre:'Efectivo'}]} });
const pedido = {folio:'LY-00000001',fechaCreacion:'2026-11-25T18:00:00Z',subtotal:200,costoEnvio:0,total:200};

test('el checkout usa los IDs y costos del servidor y crea una operación distinta por confirmación', () => {
  const state = estado();
  const primero = prepararPedido(state); const segundo = prepararPedido(state);
  assert.notEqual(primero.solicitud.idOperacion, segundo.solicitud.idOperacion);
  assert.deepEqual(primero.solicitud.articulos,[{idVariante:11,cantidad:2,precioEsperado:100}]);
  assert.equal(primero.solicitud.idMetodoPago,3);
  assert.equal(primero.solicitud.totalEsperado,200);
  const domicilio = prepararPedido({...state,delivery:'domicilio',checkoutAddress:{calle:'Ejemplo',numero:'1'}});
  assert.equal(domicilio.solicitud.totalEsperado,225);
  assert.equal(domicilio.solicitud.direccion.latitud,19.54);
});
test('un pedido confirmado no borra un carrito editado durante la petición ni duplica el historial', () => {
  const state = estado(); const pendiente = prepararPedido(state);
  const cambiado = {...state,cart:[{...state.cart[0],quantity:1}]};
  const resultado = guardarPedidoConfirmado(cambiado,{pendiente,pedido});
  assert.equal(resultado.cart[0].quantity,1);
  assert.equal(resultado.orders[0].persistido,true);
  assert.equal(guardarPedidoConfirmado(resultado,{pendiente,pedido}).orders.length,1);
  assert.equal(guardarPedidoConfirmado(state,{pendiente,pedido}).cart.length,0);
});
test('el historial del servidor conserva las variantes para volver a consultar el catálogo al repetir', () => {
  const h = adaptarHistorialPedido({...pedido,estado:'Entregado',calificacion:5,
    articulos:[{idProducto:1,idVariante:11,nombreProducto:'Nombre al comprar',variante:'Normal',cantidad:2,precioUnitario:100}]});
  assert.equal(h.items[0].variantId,11);
  assert.equal(h.items[0].name,'Nombre al comprar');
  assert.equal(h.status,3); assert.equal(h.rated,true);
});
