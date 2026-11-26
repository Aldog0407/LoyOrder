import { useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { money } from '../utils/formato.js';
import { obtenerCatalogo } from '../services/catalogo.js';
import { revisarLineasConCatalogo } from '../utils/catalogoStock.js';
import { useStore } from '../state/StoreContext.jsx';
import Modal from '../components/Modal.jsx';
import RatingModal from '../components/RatingModal.jsx';

/** 7.2.14: repetir únicamente líneas reconstruidas desde el catálogo vigente. */
export default function HistoryView() {
  const { state, dispatch } = useStore();
  const [reorder, setReorder] = useState(null);
  const [rate, setRate] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const busy = useRef(false);
  const navigate = useNavigate();

  async function empezarRepeticion(order) {
    if (busy.current) return;
    busy.current = true;
    setLoading(true);
    setError('');
    try {
      const catalogo = await obtenerCatalogo();
      const revision = revisarLineasConCatalogo(order.items, catalogo, state.cart);
      setReorder({ order, revision });
    } catch (fallo) {
      setError(`No se pudo comprobar la disponibilidad: ${fallo.message}`);
    } finally {
      setLoading(false);
      busy.current = false;
    }
  }

  async function confirmarRepeticion() {
    if (busy.current || !reorder?.revision.disponibles.length) return;
    busy.current = true;
    setLoading(true);
    setError('');
    try {
      // Segunda consulta para no utilizar una vista previa caducada.
      const catalogo = await obtenerCatalogo();
      const actual = revisarLineasConCatalogo(reorder.order.items, catalogo, state.cart);
      if (JSON.stringify(actual) !== JSON.stringify(reorder.revision)) {
        setReorder({ ...reorder, revision: actual });
        setError('El catálogo cambió mientras confirmabas. Revisa los nuevos detalles y vuelve a confirmar.');
        return;
      }
      dispatch({ type: 'REORDER', items: actual.disponibles });
      dispatch({ type: 'NOTIFY', message: `${actual.disponibles.length} variante(s) añadida(s) con precios actuales.` });
      setReorder(null);
    } catch (fallo) {
      setError(`No se pudo repetir el pedido: ${fallo.message}`);
    } finally {
      setLoading(false);
      busy.current = false;
    }
  }

  return <main className="container page">
    <div className="page-heading">
      <span className="eyebrow orange">TUS FAVORITOS SIEMPRE CERCA</span>
      <h1>Historial de pedidos</h1>
      <p>Una buena comida merece repetirse.</p>
    </div>
    {error && <p role="alert" className="checkout-error">{error}</p>}
    <div className="history-list">
      {state.orders.map(order => <article className="surface history-card" key={order.id}>
        <div className="history-icon">🛍️</div>
        <div className="history-main">
          <div className="history-title">
            <h3>Pedido {order.folio ?? order.id}</h3>
            <span className={`badge ${order.status === 3 ? 'success' : 'warning'}`}>
              {order.status === 3 ? 'Entregado' : 'En proceso'}
            </span>
          </div>
          <p>{order.date} · {order.items.map(item => `${item.quantity}× ${item.name ?? 'Producto anterior'}`).join(', ')}</p>
          <strong>{money(order.total)}</strong>
        </div>
        <div className="history-actions">
          <button className="button outline" disabled={loading} onClick={() => void empezarRepeticion(order)}>↻ Reordenar</button>
          {order.status === 3 && !order.rated && <button className="button subtle" onClick={() => setRate(order.id)}>★ Calificar</button>}
          <button className="text-button" onClick={() => { dispatch({ type: 'VIEW_ORDER', id: order.id }); navigate('/seguimiento'); }}>Ver detalle →</button>
        </div>
      </article>)}
    </div>
    {loading && <p role="status">Consultando el catálogo actualizado…</p>}
    {reorder && <Modal label="Reordenar" className="confirm-modal" onClose={() => { if (!loading) { setReorder(null); setError(''); } }}>
      <div className="rate-face">🔄</div>
      <h2>Repetir pedido</h2>
      <p>Los precios y la disponibilidad corresponden al catálogo consultado. No se procesará ningún pago.</p>
      {error && <p role="alert" className="checkout-error">{error}</p>}
      {reorder.revision.disponibles.length > 0 && <><h3>Disponibles</h3><ul>
        {reorder.revision.disponibles.map(item => <li key={`${item.dishId}:${item.variantId}`}>
          {item.quantity}× {item.name} — {item.variant || 'Única'} · {money(item.price)} por unidad
        </li>)}
      </ul></>}
      {reorder.revision.incidencias.length > 0 && <div className="checkout-error" role="alert">
        <strong>Cambios o productos no disponibles</strong><ul>
          {reorder.revision.incidencias.map((issue, index) => <li key={index}>{issue.mensaje}</li>)}
        </ul>
        <p>Los productos agotados se omitirán. Los cambios de precio se aplicarán solo si aceptas continuar.</p>
      </div>}
      <button className="button primary full" disabled={loading || !reorder.revision.disponibles.length} onClick={() => void confirmarRepeticion()}>
        {loading ? 'Verificando nuevamente…' : 'Agregar disponibles con precios actuales'}
      </button>
      <button className="button subtle full" disabled={loading} onClick={() => { setReorder(null); setError(''); }}>Cancelar</button>
    </Modal>}
    {rate && <RatingModal orderId={rate} onClose={() => setRate(null)}/>}
  </main>;
}
