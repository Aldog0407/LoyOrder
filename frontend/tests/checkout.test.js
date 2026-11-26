import test from 'node:test';
import assert from 'node:assert/strict';
import { defaultConfig } from '../src/config/defaults.js';
import { checkoutTotals, createPlaceOrderAction, placeOrderState, validateCheckout, firmaCheckout } from '../src/utils/checkout.js';

const state = (changes = {}) => ({ config: structuredClone(defaultConfig), cart: [{ dishId: 1, variantId: 11, name: 'Burger de la casa', variant: 'Clásica', image: null, quantity: 2, price: 149 }], orders: [{ id: 'LY-1042' }], delivery: 'local', payment: 'cash', address: 'Calle de prueba 123', pin: { lng: -96.9271, lat: 19.5438 }, drawerOpen: true, ...changes });

test('local: folio único, envío cero y persistencia compatible', () => {
  const before = state();
  const action = createPlaceOrderAction();
  const after = placeOrderState(before, { ...action, firmaValidada: firmaCheckout(before) });
  assert.equal(after.orders[0].id, action.folio);
  assert.equal(after.orders[0].folio, action.folio);
  assert.equal(after.activeOrderId, action.folio);
  assert.equal(after.orders[0].total, 298);
  assert.equal(after.orders[0].shipping, 0);
  assert.equal(after.orders[0].address, null);
  assert.equal(after.orders[0].payment, 'cash');
  assert.equal(after.orders[0].delivery, 'local');
  assert.equal(after.cart.length, 0);
  assert.equal(before.cart.length, 1);
  assert.equal(after.drawerOpen, false);
  assert.equal(JSON.parse(JSON.stringify(after)).orders[1].id, 'LY-1042');
});
test('domicilio: registra dirección, método y desglose', () => {
  const before = state({ delivery: 'domicilio', payment: 'card' });
  assert.deepEqual(checkoutTotals(before), { subtotal: 298, shipping: 35, total: 333 });
  const order = placeOrderState(before, createPlaceOrderAction(firmaCheckout(before))).orders[0];
  assert.equal(order.address, before.address);
  assert.deepEqual(order.pin, before.pin);
  assert.equal(order.payment, 'card');
  assert.equal(order.loyverseType, 'CARD');
  assert.equal(order.total, 333);
});
test('rechaza carrito vacío, dirección incompleta y pago desactivado', () => {
  for (const input of [state({ cart: [] }), state({ delivery: 'domicilio', address: '' }), state({ payment: 'digital' })]) {
    assert.ok(validateCheckout(input));
    const after = placeOrderState(input, createPlaceOrderAction(firmaCheckout(input)));
    assert.equal(after.orders.length, 1);
    assert.equal(after.cart, input.cart);
    assert.equal(after.toast.error, true);
  }
});
test('reducer puro, acciones repetidas no duplican pedidos', () => {
  const before = state();
  const action = createPlaceOrderAction();
  const after = placeOrderState(before, { ...action, firmaValidada: firmaCheckout(before) });
  assert.deepEqual(after, placeOrderState(before, { ...action, firmaValidada: firmaCheckout(before) }));
  assert.equal(placeOrderState(after, { ...action, firmaValidada: firmaCheckout(before) }), after);
  assert.notEqual(createPlaceOrderAction().folio, action.folio);
});
test('rechaza variantes inválidas y folio ausente', () => {
  // 7.2.4: una línea sin variante del catálogo (formato del menú de demostración) se rechaza.
  assert.ok(validateCheckout(state({ cart: [{ dishId: 1, variant: 'No existe', quantity: 1, price: 149 }] })));
  assert.ok(validateCheckout(state({ cart: [{ dishId: 1, variantId: 11, name: 'Burger', variant: 'Clásica', quantity: 1, price: 0 }] })));
  assert.equal(placeOrderState(state(), { type: 'PLACE_ORDER' }).orders.length, 1);
});
