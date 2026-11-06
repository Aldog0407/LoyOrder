import { useState } from 'react';
import { useStore } from '../state/StoreContext.jsx';
import Modal from './Modal.jsx';

/** Mapa esquemático ajustable; sustituir por un proveedor de mapas en integración. */
export default function LocationPicker({ onClose }) {
  const { state, dispatch } = useStore();
  const [pin, setPin] = useState(state.pin);
  const [address, setAddress] = useState(state.address);
  const movePin = event => {
    const bounds = event.currentTarget.getBoundingClientRect();
    setPin({ x: Math.max(5, Math.min(95, Math.round((event.clientX - bounds.left) / bounds.width * 100))), y: Math.max(8, Math.min(92, Math.round((event.clientY - bounds.top) / bounds.height * 100))) });
  };
  const save = () => {
    if (address.trim().length < 8) return dispatch({ type: 'NOTIFY', message: 'Ingresa una dirección más completa.', error: true });
    dispatch({ type: 'SET_ADDRESS', address: address.trim(), pin }); onClose();
  };
  return <Modal className="map-modal" label="Seleccionar ubicación" onClose={onClose}><div className="modal-title"><div><span className="eyebrow orange">DOMICILIO</span><h2>Elige tu ubicación</h2><p>Arrastra o toca el mapa para ubicar el pin.</p></div></div><div className="demo-map" onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); movePin(event); }} onPointerMove={event => { if (event.currentTarget.hasPointerCapture(event.pointerId)) movePin(event); }}><div className="map-road road-a"/><div className="map-road road-b"/><div className="map-road road-c"/><div className="map-park">PARQUE CENTRAL 🌳</div><div className="map-pin" style={{ left: `${pin.x}%`, top: `${pin.y}%` }}>📍</div></div><label className="field-label" htmlFor="address">Dirección y referencias</label><input className="address-input" id="address" value={address} maxLength={160} onChange={event => setAddress(event.target.value)}/><p className="fine-print">Mapa esquemático sin geocodificación ni distancias reales.</p><button className="button primary full" onClick={save}>Confirmar ubicación</button></Modal>;
}
