import { useState } from 'react';

/** Imagen de Loyverse; si no hay o no carga, un marcador genérico. Es decorativa: el nombre va junto en texto. */
export default function ImagenPlatillo({ src }) {
  const [fallo, setFallo] = useState(false);
  if (!src || fallo) return <span className="dish-placeholder" aria-hidden="true">🍽️</span>;
  return <img className="dish-photo" src={src} alt="" loading="lazy" decoding="async" onError={() => setFallo(true)}/>;
}
