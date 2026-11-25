import test from 'node:test';
import assert from 'node:assert/strict';
import { isValidPin, normalizeAddress } from '../src/utils/address.js';
import { validateCheckout } from '../src/utils/checkout.js';
import { defaultConfig } from '../src/config/defaults.js';
const pin = { lng: -96.9271, lat: 19.5438 };
test('valid coordinates and normalized text persist through JSON', () => {
  const location = normalizeAddress('  Calle de prueba 123  ', pin);
  assert.deepEqual(JSON.parse(JSON.stringify(location)), { address: 'Calle de prueba 123', pin });
});
test('rejects legacy, missing and out-of-range coordinates', () => {
  for (const input of [null, {x:50,y:50}, {lng:181,lat:0}, {lng:0,lat:90}, {lng:'1',lat:0}]) assert.equal(isValidPin(input), false);
  assert.equal(normalizeAddress('short', pin), null);
});
test('delivery requires geographic pin, pickup does not', () => {
  const state = { config: defaultConfig, cart: [{dishId:1,variantId:11,name:'Burger de la casa',variant:'Clásica',image:null,quantity:1,price:149}], payment:'cash', address:'Calle de prueba 123', pin:null };
  assert.ok(validateCheckout({...state,delivery:'domicilio'}));
  assert.equal(validateCheckout({...state,delivery:'domicilio',pin}), null);
  assert.equal(validateCheckout({...state,delivery:'local'}), null);
});
