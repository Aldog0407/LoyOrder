import test from 'node:test';
import assert from 'node:assert/strict';
import { revisarLineasConCatalogo, verificarCheckoutConCatalogo } from '../src/utils/catalogoStock.js';
import { createPlaceOrderAction, firmaCheckout, placeOrderState } from '../src/utils/checkout.js';
import { defaultConfig } from '../src/config/defaults.js';

const producto = (agotado = false, precio = 85) => ({ id: 12, nombre: 'Pizza', imagenUrl: null,
  agotado, variantes: [{ id: 120, nombre: 'Chica', precio, agotado }] });
const catalogo = (p = producto()) => ({ categorias: [{ id: 1, nombre: 'Comida', productos: p ? [p] : [] }] });
const linea = () => ({ dishId: 12, variantId: 120, name: 'Pizza', variant: 'Chica', image: null, price: 85, quantity: 2 });
const state = () => ({ cart: [linea()], orders: [], delivery: 'local', payment: 'cash', config: structuredClone(defaultConfig), address: '', pin: null });

test('checkout válido con variante y precio idénticos', () => {
  const result = verificarCheckoutConCatalogo([linea()], catalogo());
  assert.equal(result.valido, true);
  assert.equal(result.disponibles[0].variantId, 120);
});
test('rechaza variante agotada o producto desaparecido', () => {
  for (const menu of [catalogo(producto(true)), catalogo(null)]) {
    const result = verificarCheckoutConCatalogo([linea()], menu);
    assert.equal(result.valido, false);
    assert.equal(result.disponibles.length, 0);
    assert.equal(result.incidencias[0].tipo, 'agotado');
  }
});
test('precio actualizado obliga a revisar checkout; reorden usa precio vigente', () => {
  const result = verificarCheckoutConCatalogo([linea()], catalogo(producto(false, 105)));
  assert.equal(result.valido, false);
  assert.equal(result.disponibles[0].price, 105);
  assert.ok(result.incidencias.some(i => i.tipo === 'precio'));
});
test('pedido antiguo sin variantId se descarta con mensaje', () => {
  const result = revisarLineasConCatalogo([{ dishId: 12, name: 'Pizza', price: 85, quantity: 2 }], catalogo());
  assert.equal(result.disponibles.length, 0);
  assert.equal(result.incidencias[0].tipo, 'formato');
});
test('repetir respeta cantidad máxima sumada al carrito existente', () => {
  const result = revisarLineasConCatalogo([linea()], catalogo(), [{ ...linea(), quantity: 19 }]);
  assert.equal(result.disponibles.length, 0);
  assert.equal(result.incidencias[0].tipo, 'limite');
});
test('PLACE_ORDER requiere comprobación vigente de la misma versión del carrito', () => {
  const before = state();
  const bad = placeOrderState(before, createPlaceOrderAction());
  assert.equal(bad.orders.length, 0);
  const action = createPlaceOrderAction(firmaCheckout(before));
  const changed = { ...before, cart: [{ ...linea(), quantity: 3 }] };
  assert.equal(placeOrderState(changed, action).orders.length, 0);
  assert.equal(placeOrderState(before, action).orders.length, 1);
});
