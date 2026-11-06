import { useEffect, useState } from 'react';
import { paymentCatalog } from '../config/defaults.js';
import { useStore } from '../state/StoreContext.jsx';
import { isOpen, validateConfig } from '../utils/restaurant.js';
import { loyverseAdapter } from '../services/loyverseAdapter.js';

export default function AdminConfig() {
  const { state, dispatch } = useStore();
  const [draft, setDraft] = useState(() => structuredClone(state.config));
  const [now, setNow] = useState(() => new Date());
  const [testResult, setTestResult] = useState('Sin pruebas realizadas en esta sesión.');
  const [printing, setPrinting] = useState(false);
  const open = isOpen(state.config, now);

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 30000);
    return () => clearInterval(timer);
  }, []);

  const changeShipping = (key, value) => setDraft(prev => ({ ...prev, shipping: { ...prev.shipping, [key]: value } }));
  const changePayment = key => setDraft(prev => ({ ...prev, paymentMethods: { ...prev.paymentMethods, [key]: !prev.paymentMethods[key] } }));
  const save = event => {
    event.preventDefault();
    const error = validateConfig(draft);
    if (error) return dispatch({ type: 'NOTIFY', message: error, error: true });
    dispatch({ type: 'SET_CONFIG', config: draft }); dispatch({ type: 'NOTIFY', message: 'Configuración guardada correctamente' });
  };
  const togglePrinter = () => {
    const next = { ...draft, printer: { connected: !draft.printer.connected } };
    setDraft(next);
    setTestResult(next.printer.connected ? 'Conexión simulada activada.' : 'Impresora desconectada (demo).');
  };
  const testPrinter = async () => {
    setPrinting(true); setTestResult('Procesando ticket de prueba…');
    try {
      const result = await loyverseAdapter.testPrint(draft.printer.connected);
      setTestResult(`✓ Impresión simulada exitosa · ${result.folio}`);
    } catch (error) { setTestResult(error.message); }
    finally { setPrinting(false); }
  };

  return <main className="container page settings-page"><div className="settings-heading"><div><span className="eyebrow orange">ADMINISTRACIÓN / AJUSTES</span><h1>Configuración del restaurante</h1><p>Administra horarios, entrega, cobros y caja.</p></div><span className={`badge ${open ? 'success' : 'error'}`}>{open ? '● Abierto ahora' : '● Cerrado ahora'}</span></div>
    <form className="settings-grid" onSubmit={save}>
      <section className="surface settings-card"><div className="settings-card-head"><span className="settings-symbol">◷</span><div><h2>Horarios de operación</h2><p>Según el reloj local del dispositivo, incluso turnos nocturnos.</p></div></div><div className="settings-two"><label className="settings-field">Apertura<input type="time" required value={draft.openingTime} onChange={e => setDraft({ ...draft, openingTime: e.target.value })}/></label><label className="settings-field">Cierre<input type="time" required value={draft.closingTime} onChange={e => setDraft({ ...draft, closingTime: e.target.value })}/></label></div><div className="settings-hint">El estado se recalcula automáticamente cada 30 segundos.</div></section>
      <section className="surface settings-card"><div className="settings-card-head"><span className="settings-symbol">🛵</span><div><h2>Costo de envío</h2><p>Tarifa base y reglas de distancia.</p></div></div><label className="settings-field">Tarifa base (MXN)<input type="number" min="0" max="10000" step="0.01" required value={draft.shipping.base} onChange={e => changeShipping('base', Number(e.target.value))}/></label><div className="settings-radios"><label><input type="radio" checked={draft.shipping.mode === 'flat'} onChange={() => changeShipping('mode', 'flat')}/> Tarifa fija</label><label><input type="radio" checked={draft.shipping.mode === 'distance'} onChange={() => changeShipping('mode', 'distance')}/> Por kilómetro</label></div>{draft.shipping.mode === 'distance' && <div className="settings-two"><label className="settings-field">Radio incluido (km)<input type="number" min="0" max="10000" step="0.1" value={draft.shipping.radiusKm} onChange={e => changeShipping('radiusKm', Number(e.target.value))}/></label><label className="settings-field">Extra por km (MXN)<input type="number" min="0" max="10000" step="0.01" value={draft.shipping.perKm} onChange={e => changeShipping('perKm', Number(e.target.value))}/></label></div>}<div className="settings-hint">El cálculo por km necesita un proveedor geográfico; actualmente se aplica la tarifa base.</div></section>
      <section className="surface settings-card"><div className="settings-card-head"><span className="settings-symbol">💳</span><div><h2>Métodos de pago</h2><p>Mapeo conceptual con el catálogo de Loyverse.</p></div></div><div className="settings-payment-list">{paymentCatalog.map(method => <label className="settings-payment" key={method.id}><span><b>{method.icon} {method.label}</b><small>Tipo de referencia: {method.loyverseType}</small></span><input type="checkbox" checked={draft.paymentMethods[method.id]} onChange={() => changePayment(method.id)}/></label>)}</div><div className="integration-note">◌ Loyverse no conectado. Los IDs reales deberán obtenerse desde el backend.</div></section>
      <section className="surface settings-card"><div className="settings-card-head"><span className="settings-symbol">🖨</span><div><h2>Impresora de caja</h2><p>Adaptador de hardware simulado.</p></div></div><div className="printer-status"><div><strong>Impresora térmica de caja</strong><small>Conectividad simulada</small></div><span className={`badge ${draft.printer.connected ? 'success' : 'error'}`}>{draft.printer.connected ? '● Conectada (demo)' : '● Desconectada'}</span></div><div className="printer-actions"><button className="button outline" type="button" onClick={togglePrinter}>{draft.printer.connected ? 'Desconectar demo' : 'Simular conexión'}</button><button className="button primary" type="button" disabled={!draft.printer.connected || printing} onClick={testPrinter}>{printing ? 'Procesando…' : 'Imprimir ticket de prueba'}</button></div><div className="ticket-result" role="status">{testResult}</div><div className="integration-note">No se emiten comandos ESC/POS ni se descubre hardware físico.</div></section>
      <div className="settings-save"><span>Los cambios se guardan en este navegador.</span><button className="button primary" type="submit">Guardar configuración</button></div>
    </form>
  </main>;
}
