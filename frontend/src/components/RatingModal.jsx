import { useState } from 'react';
import { useStore } from '../state/StoreContext.jsx';
import Modal from './Modal.jsx';

export default function RatingModal({ orderId, onClose }) {
  const [rating, setRating] = useState(0);
  const { dispatch } = useStore();
  return <Modal label="Calificar pedido" className="rate-modal" onClose={onClose}><div className="rate-face">💛</div><h2>¿Qué tal estuvo todo?</h2><p>Tu opinión nos ayuda a mejorar.</p><div className="stars" role="group" aria-label="Calificación de 1 a 5 estrellas">{[1, 2, 3, 4, 5].map(value => <button key={value} aria-label={`${value} estrellas`} aria-pressed={rating === value} className={value <= rating ? 'selected' : ''} onClick={() => setRating(value)}>★</button>)}</div><small>{rating ? `${rating} de 5 estrellas` : 'Selecciona una calificación'}</small><button disabled={!rating} className="button primary full" onClick={() => { dispatch({ type: 'RATE_ORDER', id: orderId, rating }); dispatch({ type: 'NOTIFY', message: '¡Gracias por tu calificación!' }); onClose(); }}>Enviar calificación</button></Modal>;
}
