import test from 'node:test';
import assert from 'node:assert/strict';
import { buscarDirecciones, normalizarSugerencias, reverseGeocode, reverseGeocodeDetalle, geocodingWaitMs, GeocodingRateLimitError } from '../src/services/geocodingService.js';

const sample = { features: [{
  properties: { mapbox_id: 'abc', name: 'Av. Xalapa', place_formatted: 'Xalapa, Veracruz, México',
    full_address: 'Av. Xalapa 25, Xalapa, Veracruz, México', coordinates: { longitude: -96.93, latitude: 19.54 }, feature_type: 'address' },
  geometry: { coordinates: [-96.93, 19.54] }
}] };

test('suggestions map address and geographic pin', () => {
  assert.deepEqual(normalizarSugerencias(sample.features)[0], {
    id: 'abc', address: 'Av. Xalapa 25, Xalapa, Veracruz, México',
    label: 'Av. Xalapa', detail: 'Xalapa, Veracruz, México',
    checkoutAddress: { calle: '', numero: '' }, pin: { lng: -96.93, lat: 19.54 }
  });
});

test('ignores malformed or non-geographic features', () => {
  assert.deepEqual(normalizarSugerencias([{ properties: { name: 'nowhere' } }]), []);
});

test('forward search builds v6 request with autocomplete, mx and permanent storage', async () => {
  let url, options;
  const results = await buscarDirecciones('Av. Xalapa', {
    token: 'pk.fake', proximity: { lng: -96.9, lat: 19.5 },
    fetchImpl: async (input, opts) => { url = new URL(input); options = opts; return { ok: true, json: async () => sample }; }
  });
  assert.equal(url.pathname, '/search/geocode/v6/forward');
  assert.equal(url.searchParams.get('q'), 'Av. Xalapa');
  assert.equal(url.searchParams.get('autocomplete'), 'true');
  assert.equal(url.searchParams.get('permanent'), 'true');
  assert.equal(url.searchParams.get('proximity'), '-96.9,19.5');
  assert.equal(url.searchParams.get('country'), 'mx');
  assert.equal(options.cache, 'no-store');
  assert.equal(results.length, 1);
});

test('short text triggers no request', async () => {
  assert.deepEqual(await buscarDirecciones('ab', { fetchImpl: () => { throw new Error('called'); } }), []);
});

test('reverse request works and uses permanent lookup', async () => {
  let url;
  const address = await reverseGeocode(-96.93, 19.54, undefined, {
    token: 'pk.fake', fetchImpl: async input => { url = new URL(input); return { ok: true, json: async () => sample }; }
  });
  assert.equal(address, 'Av. Xalapa 25, Xalapa, Veracruz, México');
  assert.equal(url.searchParams.get('permanent'), 'true');
});

test('reverse detail carries street and number only from Mapbox fields', async () => {
  const feature = { features: [{
    properties: { full_address: 'Calle Reforma 25, Xalapa', feature_type: 'address',
      street: 'Reforma', address_number: '25' }
  }] };
  const result = await reverseGeocodeDetalle(-96.93, 19.54, undefined, {
    token: 'pk.fake', fetchImpl: async () => ({ ok: true, json: async () => feature })
  });
  assert.deepEqual(result, { address: 'Calle Reforma 25, Xalapa', checkoutAddress: { calle: 'Reforma', numero: '25' } });
});

test('401 is not misreported as rate limit', async () => {
  await assert.rejects(buscarDirecciones('Reforma 25', {
    token: 'pk.fake', fetchImpl: async () => ({ status: 401, ok: false })
  }), /token y los permisos/);
});

test('rate limiting surfaces a readable error', async () => {
  await assert.rejects(buscarDirecciones('Av. Xalapa', {
    token: 'pk.fake',
    fetchImpl: async () => ({ status: 429, ok: false, headers: { get: () => '12' } })
  }), error => error instanceof GeocodingRateLimitError && error.status === 429);
  assert.ok(geocodingWaitMs() > 10_000);
  let calls = 0;
  await assert.rejects(reverseGeocode(-96.93, 19.54, undefined, {
    token: 'pk.fake', fetchImpl: async () => { calls++; throw new Error('Unexpected fetch'); }
  }), error => error instanceof GeocodingRateLimitError);
  assert.equal(calls, 0, 'During cooldown no additional HTTP request is sent');
});
