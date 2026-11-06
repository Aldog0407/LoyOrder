import { useState } from 'react';
import { useNavigate } from 'react-router';
import { getDish, money } from '../data/menu.js';
import { useStore } from '../state/StoreContext.jsx';
import Modal from '../components/Modal.jsx';
import RatingModal from '../components/RatingModal.jsx';

export default function HistoryView() {
  const { state, dispatch } = useStore();
  const [reorder, setReorder] = useState(null);
  const [rate, setRate] = useState(null);
  const navigate = useNavigate();
  return <main className="container page"><div className="page-heading"><span className="eyebrow orange">TUS FAVORITOS SIEMPRE CERCA</span><h1>Historial de pedidos</h1><p>Una buena comida merece repetirse.</p></div><div className="history-list">{state.orders.map(order => <article className="surface history-card" key={order.id}><div className="history-icon">🛍️</div><div className="history-main"><div className="history-title"><h3>Pedido {order.id}</h3><span className={`badge ${order.status === 3 ? 'success' : 'warning'}`}>{order.status === 3 ? 'Entregado' : 'En proceso'}</span></div><p>{order.date} · {order.items.map(item => `${item.quantity}× ${getDish(item.dishId)?.name}`).join(', ')}</p><strong>{money(order.total)}</strong></div><div className="history-actions"><button className="button outline" onClick={() => setReorder(order)}>↻ Reordenar</button>{order.status === 3 && !order.rated && <button className="button subtle" onClick={() => setRate(order.id)}>★ Calificar</button>}<button className="text-button" onClick={() => { dispatch({ type: 'VIEW_ORDER', id: order.id }); navigate('/seguimiento'); }}>Ver detalle →</button></div></article>)}</div>{reorder && <Modal label="Reordenar" className="confirm-modal" onClose={() => setReorder(null)}><div className="rate-face">🔄</div><h2>¿Volver a pedirlo?</h2><p>Se agregarán los productos todavía disponibles al carrito.</p><button className="button primary full" onClick={() => { dispatch({ type: 'REORDER', items: reorder.items }); setReorder(null); }}>Sí, reordenar</button><button className="button subtle full" onClick={() => setReorder(null)}>Cancelar</button></Modal>}{rate && <RatingModal orderId={rate} onClose={() => setRate(null)}/>}</main>;
}
