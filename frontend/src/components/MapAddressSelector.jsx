import { useEffect, useId, useRef, useState } from 'react';
import mapboxgl from 'mapbox-gl';
import 'mapbox-gl/dist/mapbox-gl.css';
import { isValidPin, normalizeAddress } from '../utils/address.js';
import Modal from './Modal.jsx';
import { reverseGeocode } from '../services/geocodingService.js';

const TOKEN = import.meta.env.VITE_MAPBOX_ACCESS_TOKEN?.trim();
const CENTER = [-96.9271, 19.5438]; // Xalapa: initial viewport, not a saved address.

/** Draft stays local until the user explicitly confirms. */
export default function MapAddressSelector({ initialAddress, initialPin, onSave, onClose }) {
  const container = useRef(null);
  const requestRef = useRef(null);
  const initial = useRef(isValidPin(initialPin) ? { ...initialPin } : null);
  const [pin, setPin] = useState(initial.current);
  const [address, setAddress] = useState(initial.current ? initialAddress || '' : '');
  const [ready, setReady] = useState(false);
  const [loadingAddress, setLoadingAddress] = useState(false);
  const [mapError, setMapError] = useState('');
  const [geocodingError, setGeocodingError] = useState('');
  const fieldId = useId();

  useEffect(() => {
    if (!TOKEN?.startsWith('pk.')) {
      setMapError('Configura el token público de Mapbox.');
      return;
    }

    let map;
    let marker;
    let observer;
    let disposed = false;

    /**
     * Consulta la dirección correspondiente al pin.
     * Cancela cualquier solicitud anterior.
     */
    async function resolveAddress(lng, lat) {
      requestRef.current?.abort();

      const controller = new AbortController();
      requestRef.current = controller;

      setLoadingAddress(true);
      setGeocodingError('');
      setAddress('');

      try {
        const result = await reverseGeocode(
          lng,
          lat,
          controller.signal
        );

        if (!disposed && !controller.signal.aborted) {
          setAddress(result);
        }

      } catch (error) {
        if (!disposed && !controller.signal.aborted) {
          setGeocodingError(
            `${error.message} Escribe tu dirección manualmente.`
          );
        }

      } finally {
        // Una solicitud antigua no debe modificar
        // el estado de una solicitud más reciente.
        if (
          !disposed &&
          requestRef.current === controller
        ) {
          setLoadingAddress(false);
          requestRef.current = null;
        }
      }
    }

    try {
      mapboxgl.accessToken = TOKEN;

      const saved = initial.current;

      map = new mapboxgl.Map({
        container: container.current,
        style: 'mapbox://styles/mapbox/streets-v12',
        center: saved
          ? [saved.lng, saved.lat]
          : CENTER,
        zoom: 15,
        renderWorldCopies: false
      });

      marker = new mapboxgl.Marker({
        color: '#E8590C',
        draggable: true
      });

      map.addControl(
        new mapboxgl.NavigationControl({
          showCompass: false
        }),
        'top-right'
      );

      /**
       * Actualiza las coordenadas y resuelve la dirección.
       */
      function choose(lngLat) {
        const { lng, lat } = lngLat.wrap();
        const next = { lng, lat };

        if (!isValidPin(next)) return;

        marker
          .setLngLat([lng, lat])
          .addTo(map);

        setPin(next);

        void resolveAddress(lng, lat);
      }

      // Restablecer la ubicación guardada sin consumir
      // otra solicitud de geocodificación.
      if (saved) {
        marker
          .setLngLat([saved.lng, saved.lat])
          .addTo(map);
      }

      // Selección mediante clic o toque.
      map.on('click', event => {
        choose(event.lngLat);
      });

      // Consultar únicamente cuando termine el arrastre.
      marker.on('dragend', () => {
        choose(marker.getLngLat());
      });

      map.on('load', () => {
        setReady(true);
        setMapError('');
      });

      map.on('error', () => {
        if (!disposed) {
          setMapError(
            'No se pudo cargar algún recurso del mapa.'
          );
        }
      });

      observer = new ResizeObserver(() => {
        map.resize();
      });

      observer.observe(container.current);

    } catch {
      setMapError(
        'No se pudo iniciar Mapbox. Comprueba WebGL y el token.'
      );
    }

    return () => {
      disposed = true;

      requestRef.current?.abort();
      observer?.disconnect();
      marker?.remove();
      map?.remove();
    };

  }, []);

  const selection = normalizeAddress(address, pin);
  const canConfirm = ready && !loadingAddress && Boolean(selection);

  return (
    <Modal
      className="map-modal mapbox-address-modal"
      label="Seleccionar ubicación"
      onClose={onClose}
    >
      <div className="modal-title">
        <span className="eyebrow orange">
          DOMICILIO
        </span>

        <h2>Elige tu ubicación</h2>

        <p>
          Selecciona un punto en el mapa o arrastra
          el marcador hasta tu domicilio.
        </p>
      </div>

      <div
        ref={container}
        className="mapbox-address-map"
        aria-label="Mapa de ubicación de entrega"
      />

      {!ready && !mapError && (
        <p role="status">Cargando mapa…</p>
      )}

      {mapError && (
        <p className="checkout-error" role="alert">
          {mapError}
        </p>
      )}

      {pin && (
        <p className="fine-print">
          Latitud: {pin.lat.toFixed(6)}
          {' · '}
          Longitud: {pin.lng.toFixed(6)}
        </p>
      )}

      <label
        className="field-label"
        htmlFor={fieldId}
      >
        Dirección de entrega
      </label>

      <div
        className="geocoding-field"
        aria-busy={loadingAddress}
      >
        <input
          id={fieldId}
          className="address-input"
          value={address}
          maxLength={160}
          disabled={loadingAddress}
          placeholder={
            loadingAddress
              ? 'Buscando dirección…'
              : 'Calle, número, colonia y ciudad'
          }
          onChange={event => {
            setAddress(event.target.value);
          }}
        />

        {loadingAddress && (
          <span
            className="geocoding-spinner"
            aria-hidden="true"
          />
        )}
      </div>

      {loadingAddress && (
        <p className="geocoding-status" role="status">
          Obteniendo dirección desde Mapbox…
        </p>
      )}

      {geocodingError && (
        <p className="checkout-error" role="alert">
          {geocodingError}
        </p>
      )}

      {!loadingAddress && address && !geocodingError && (
        <p className="fine-print">
          Verifica la calle, número y colonia.
          Puedes corregir la dirección manualmente.
        </p>
      )}

      <button
        type="button"
        className="button primary full"
        disabled={!canConfirm}
        onClick={() => {
          if (canConfirm) onSave(selection);
        }}
      >
        {loadingAddress
          ? 'Buscando dirección…'
          : 'Confirmar ubicación'}
      </button>
    </Modal>
  );
}