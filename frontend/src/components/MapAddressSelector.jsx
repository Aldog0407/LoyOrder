import { useEffect, useId, useRef, useState } from 'react';
import mapboxgl from 'mapbox-gl';
import 'mapbox-gl/dist/mapbox-gl.css';
import { isValidPin, normalizeAddress } from '../utils/address.js';
import { buscarDirecciones, reverseGeocodeDetalle, geocodingWaitMs } from '../services/geocodingService.js';
import Modal from './Modal.jsx';

const TOKEN = import.meta.env.VITE_MAPBOX_ACCESS_TOKEN?.trim();
const CENTER = [-96.9271, 19.5438]; // Viewport only, not a selected address.
const SEARCH_DELAY = 700;
const REVERSE_DELAY = 250;

/** Draft stays local until Confirm. No Mapbox request runs on manual address-field changes. */
export default function MapAddressSelector({ initialAddress, initialPin, initialCheckoutAddress, onSave, onClose }) {
  const containerRef = useRef(null);
  const mapRef = useRef(null);
  const markerRef = useRef(null);
  const searchAbortRef = useRef(null);
  const reverseAbortRef = useRef(null);
  const reverseTimerRef = useRef(null);
  const initialPinRef = useRef(isValidPin(initialPin) ? { ...initialPin } : null);
  const aliveRef = useRef(false);
  const selectionVersionRef = useRef(0);
  const listId = useId();
  const searchId = useId();
  const addressId = useId();
  const streetId = useId();
  const numberId = useId();

  const [pin, setPin] = useState(initialPinRef.current);
  const [address, setAddress] = useState(initialPinRef.current ? initialAddress ?? '' : '');
  const [details, setDetails] = useState({
    calle: initialCheckoutAddress?.calle ?? '',
    numero: initialCheckoutAddress?.numero ?? ''
  });
  const [searchText, setSearchText] = useState('');
  const [suggestions, setSuggestions] = useState([]);
  const [searchBusy, setSearchBusy] = useState(false);
  const [reverseBusy, setReverseBusy] = useState(false);
  const [searchError, setSearchError] = useState('');
  const [reverseError, setReverseError] = useState('');
  const [mapError, setMapError] = useState('');
  const [ready, setReady] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);

  // Install the map once per mounted modal. Programmatic marker moves do not trigger reverse requests.
  useEffect(() => {
    aliveRef.current = true;
    if (!TOKEN?.startsWith('pk.')) {
      setMapError('Configura VITE_MAPBOX_ACCESS_TOKEN con un token público.');
      return () => { aliveRef.current = false; };
    }
    let observer;
    try {
      mapboxgl.accessToken = TOKEN;
      const saved = initialPinRef.current;
      const map = new mapboxgl.Map({
        container: containerRef.current,
        style: 'mapbox://styles/mapbox/streets-v12',
        center: saved ? [saved.lng, saved.lat] : CENTER,
        zoom: 15,
        renderWorldCopies: false
      });
      mapRef.current = map;
      const marker = new mapboxgl.Marker({ color: '#E8590C', draggable: true });
      markerRef.current = marker;
      map.addControl(new mapboxgl.NavigationControl({ showCompass: false }), 'top-right');
      if (saved) marker.setLngLat([saved.lng, saved.lat]).addTo(map);

      function choose(lngLat) {
        const { lng, lat } = lngLat.wrap();
        if (!isValidPin({ lng, lat })) return;
        const version = ++selectionVersionRef.current;
        clearTimeout(reverseTimerRef.current);
        searchAbortRef.current?.abort();
        reverseAbortRef.current?.abort();
        setSearchText('');
        setSuggestions([]);
        setSearchBusy(false);
        setSearchError('');
        setActiveIndex(-1);
        marker.setLngLat([lng, lat]).addTo(map);
        setPin({ lng, lat });
        setAddress('');
        setDetails({ calle: '', numero: '' });
        setReverseError('');
        setReverseBusy(true);

        // A real 429 must not trigger a retry storm. Manual entry remains available.
        if (geocodingWaitMs() > 0) {
          setReverseBusy(false);
          setReverseError('Mapbox está limitando consultas temporalmente. Introduce la dirección, calle y número manualmente.');
          return;
        }
        reverseTimerRef.current = setTimeout(async () => {
          const controller = new AbortController();
          reverseAbortRef.current = controller;
          try {
            const result = await reverseGeocodeDetalle(lng, lat, controller.signal);
            if (aliveRef.current && selectionVersionRef.current === version && !controller.signal.aborted) {
              setAddress(result.address);
              setDetails(result.checkoutAddress);
            }
          } catch (error) {
            if (aliveRef.current && selectionVersionRef.current === version && !controller.signal.aborted) {
              setReverseError(error.message);
            }
          } finally {
            if (aliveRef.current && selectionVersionRef.current === version) {
              setReverseBusy(false);
              reverseAbortRef.current = null;
            }
          }
        }, REVERSE_DELAY);
      }

      map.on('click', event => {
        // Do not reverse-geocode a click on the marker itself.
        if (event.originalEvent?.target?.closest?.('.mapboxgl-marker')) return;
        choose(event.lngLat);
      });
      marker.on('dragend', () => choose(marker.getLngLat()));
      map.on('load', () => { if (aliveRef.current) setReady(true); });
      map.on('error', () => { if (aliveRef.current) setMapError('Mapbox no pudo cargar un recurso del mapa. Revisa la conexión o el token.'); });
      if (typeof ResizeObserver !== 'undefined') {
        observer = new ResizeObserver(() => map.resize());
        observer.observe(containerRef.current);
      }
    } catch {
      setMapError('No fue posible iniciar el mapa. Comprueba WebGL y el token.');
    }
    return () => {
      aliveRef.current = false;
      ++selectionVersionRef.current;
      clearTimeout(reverseTimerRef.current);
      searchAbortRef.current?.abort();
      reverseAbortRef.current?.abort();
      observer?.disconnect();
      markerRef.current?.remove();
      mapRef.current?.remove();
      markerRef.current = null;
      mapRef.current = null;
    };
  }, []);

  // Only the dedicated search input requests forward geocoding.
  useEffect(() => {
    searchAbortRef.current?.abort();
    const query = searchText.trim();
    if (query.length < 4) return;
    const controller = new AbortController();
    searchAbortRef.current = controller;
    const timer = setTimeout(async () => {
      if (geocodingWaitMs() > 0) {
        setSearchError('Mapbox limitó las consultas. Puedes completar la dirección manualmente mientras se restablece el servicio.');
        setSearchBusy(false);
        return;
      }
      setSearchBusy(true);
      setSearchError('');
      try {
        const result = await buscarDirecciones(query, {
          signal: controller.signal,
          proximity: initialPinRef.current ?? { lng: CENTER[0], lat: CENTER[1] }
        });
        if (aliveRef.current && !controller.signal.aborted && searchAbortRef.current === controller) {
          setSuggestions(result);
          setActiveIndex(-1);
        }
      } catch (error) {
        if (aliveRef.current && !controller.signal.aborted && searchAbortRef.current === controller) {
          setSuggestions([]);
          setSearchError(error.message);
        }
      } finally {
        if (aliveRef.current && !controller.signal.aborted && searchAbortRef.current === controller) setSearchBusy(false);
      }
    }, SEARCH_DELAY);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [searchText]);

  function editSearch(value) {
    searchAbortRef.current?.abort();
    setSearchText(value);
    setSuggestions([]);
    setSearchBusy(false);
    setSearchError('');
    setActiveIndex(-1);
  }

  function chooseSuggestion(item) {
    searchAbortRef.current?.abort();
    reverseAbortRef.current?.abort();
    clearTimeout(reverseTimerRef.current);
    ++selectionVersionRef.current;
    setSearchText('');
    setSuggestions([]);
    setSearchBusy(false);
    setReverseBusy(false);
    setSearchError('');
    setReverseError('');
    setActiveIndex(-1);
    setPin(item.pin);
    setAddress(item.address);
    setDetails(item.checkoutAddress);
    markerRef.current?.setLngLat([item.pin.lng, item.pin.lat]).addTo(mapRef.current);
    mapRef.current?.flyTo({ center: [item.pin.lng, item.pin.lat], zoom: 17, essential: true });
    // No reverse request: the selected suggestion already carries coordinates and address.
  }

  function editManual(field, value) {
    // A pending reverse response must not overwrite manual corrections.
    ++selectionVersionRef.current;
    reverseAbortRef.current?.abort();
    clearTimeout(reverseTimerRef.current);
    setReverseBusy(false);
    setReverseError('');
    if (field === 'address') setAddress(value);
    else setDetails(prev => ({ ...prev, [field]: value }));
    // Deliberately DO NOT change searchText here.
  }

  function onSearchKeyDown(event) {
    if (!suggestions.length) return;
    if (event.key === 'ArrowDown') { event.preventDefault(); setActiveIndex(i => (i + 1) % suggestions.length); }
    if (event.key === 'ArrowUp') { event.preventDefault(); setActiveIndex(i => i <= 0 ? suggestions.length - 1 : i - 1); }
    if (event.key === 'Enter' && activeIndex >= 0) { event.preventDefault(); chooseSuggestion(suggestions[activeIndex]); }
    if (event.key === 'Escape') { event.stopPropagation(); editSearch(''); }
  }

  const selection = normalizeAddress(address, pin);
  const validDetails = details.calle.trim().length > 0 && details.numero.trim().length > 0 && details.calle.length <= 150 && details.numero.length <= 20;
  const canSave = ready && !reverseBusy && !!selection && validDetails;
  const showSuggestions = searchText.trim().length >= 4;

  return <Modal className="map-modal mapbox-address-modal" label="Seleccionar dirección de entrega" onClose={onClose}>
    <div className="modal-title">
      <span className="eyebrow orange">DOMICILIO</span>
      <h2>Elige tu ubicación</h2>
      <p>Busca una dirección, selecciona una sugerencia o coloca el marcador. Completa los datos aquí antes de guardar.</p>
    </div>
    <div className="address-search" role="search">
      <label className="field-label" htmlFor={searchId}>Buscar calle, colonia o ciudad</label>
      <div className="geocoding-field" aria-busy={searchBusy}>
        <input id={searchId} className="address-input" autoComplete="off" type="search" role="combobox"
          aria-autocomplete="list" aria-expanded={showSuggestions && suggestions.length > 0}
          aria-controls={listId} aria-activedescendant={activeIndex >= 0 && suggestions[activeIndex] ? `${listId}-${activeIndex}` : undefined}
          value={searchText} placeholder="Buscar en Mapbox (4+ caracteres)" maxLength={160}
          onChange={event => editSearch(event.target.value)} onKeyDown={onSearchKeyDown}/>
        {searchBusy && <span className="geocoding-spinner" aria-hidden="true"/>}
      </div>
      {showSuggestions && (searchBusy || searchError || suggestions.length > 0 || !geocodingWaitMs()) &&
        <div className="address-suggestions" id={listId} role="listbox" aria-label="Direcciones sugeridas">
          {searchBusy && <p className="address-suggestions-message" role="status">Buscando…</p>}
          {!searchBusy && searchError && <p className="address-suggestions-message checkout-error" role="alert">{searchError}</p>}
          {!searchBusy && !searchError && suggestions.length === 0 && <p className="address-suggestions-message" role="status">No hay sugerencias. Puedes ubicar el pin y escribir la dirección manualmente.</p>}
          {!searchBusy && suggestions.map((item, index) => <button
            key={item.id} id={`${listId}-${index}`} role="option" aria-selected={index === activeIndex}
            className={`address-suggestion ${index === activeIndex ? 'active' : ''}`} type="button"
            onMouseDown={event => event.preventDefault()} onClick={() => chooseSuggestion(item)}>
            <span className="address-suggestion-title">{item.label}</span>
            <span className="address-suggestion-detail">{item.detail || item.address}</span>
          </button>)}
        </div>}
    </div>
    <div ref={containerRef} className="mapbox-address-map" aria-label="Mapa interactivo de entrega"/>
    {!ready && !mapError && <p role="status">Cargando mapa…</p>}
    {mapError && <p role="alert" className="checkout-error">{mapError}</p>}
    {pin && <p className="fine-print">Latitud: {pin.lat.toFixed(6)} · Longitud: {pin.lng.toFixed(6)}</p>}
    {reverseBusy && <p className="geocoding-status" role="status">Buscando dirección del marcador…</p>}
    {reverseError && <p className="fine-print" role="status">{reverseError}</p>}
    <div className="map-address-fields">
      <label htmlFor={addressId}>Dirección y referencias
        <input id={addressId} className="address-input" value={address} maxLength={160}
          placeholder="Colonia, ciudad, referencias de entrega" onChange={event => editManual('address', event.target.value)}/>
      </label>
      <div className="map-address-fields-row">
        <label htmlFor={streetId}>Calle
          <input id={streetId} value={details.calle} maxLength={150}
            placeholder="Nombre de la calle" onChange={event => editManual('calle', event.target.value)}/>
        </label>
        <label htmlFor={numberId}>Número exterior o S/N
          <input id={numberId} value={details.numero} maxLength={20}
            placeholder="Ej. 25 o S/N" onChange={event => editManual('numero', event.target.value)}/>
        </label>
      </div>
    </div>
    <p className="fine-print">Verifica que el marcador corresponda al domicilio. Si Mapbox no responde, puedes completar el texto manualmente.</p>
    <button type="button" className="button primary full" disabled={!canSave} onClick={() => {
      if (canSave) onSave({ ...selection, checkoutAddress: { calle: details.calle.trim(), numero: details.numero.trim() } });
    }}>Confirmar ubicación</button>
  </Modal>;
}
