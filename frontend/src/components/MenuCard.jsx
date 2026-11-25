import { money } from '../utils/formato.js';
import ImagenPlatillo from './ImagenPlatillo.jsx';

export default function MenuCard({ dish, onSelect }) {
  const titulo = `platillo-${dish.id}`;
  return <article className={`dish-card ${dish.agotado ? 'sold-out' : ''}`} aria-labelledby={titulo}>
    <div className="dish-art"><ImagenPlatillo src={dish.imagenUrl}/>{dish.agotado && <span className="stock-label">Agotado</span>}</div>
    <div className="dish-content"><div className="dish-meta"><span>{dish.categoria}</span></div>
      <h3 id={titulo}>{dish.nombre}</h3>{dish.descripcion && <p>{dish.descripcion}</p>}
      <div className="dish-footer"><strong>{dish.precioVariable && <small>Desde </small>}{money(dish.precioDesde)}</strong>
        <button disabled={dish.agotado} aria-label={dish.agotado ? `${dish.nombre}: no disponible` : `Agregar ${dish.nombre}`} onClick={() => onSelect(dish)}>{dish.agotado ? 'No disponible' : '+ Agregar'}</button></div>
    </div>
  </article>;
}
