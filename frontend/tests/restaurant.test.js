import { test } from 'node:test';
import assert from 'node:assert/strict';
import { defaultConfig } from '../src/config/defaults.js';
import { isOpen, shippingFee, validateConfig } from '../src/utils/restaurant.js';

test('horarios nocturnos cruzan medianoche correctamente', () => {
  const config = { ...defaultConfig, openingTime: '18:00', closingTime: '02:00' };
  assert.equal(isOpen(config, new Date(2026, 0, 1, 23, 0)), true);
  assert.equal(isOpen(config, new Date(2026, 0, 2, 1, 0)), true);
  assert.equal(isOpen(config, new Date(2026, 0, 2, 12, 0)), false);
});

test('cobro base sin datos de distancia y extra sobre radio', () => {
  const config = { ...defaultConfig, shipping: { base: 35, mode: 'distance', radiusKm: 5, perKm: 8 } };
  assert.equal(shippingFee(config, 'local'), 0);
  assert.equal(shippingFee(config, 'domicilio'), 35);
  assert.equal(shippingFee(config, 'domicilio', 7), 51);
});

test('se requiere un método de pago habilitado', () => {
  const config = { ...defaultConfig, paymentMethods: { cash: false, card: false, digital: false } };
  assert.match(validateConfig(config), /Habilita/);
});
