import { useEffect } from 'react';

export default function Modal({ children, onClose, label, className = '' }) {
  useEffect(() => {
    const onKey = event => { if (event.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);
  return <div className="backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose(); }}><section className={`modal ${className}`} role="dialog" aria-modal="true" aria-label={label}><button className="close-button modal-close" onClick={onClose} aria-label="Cerrar">×</button>{children}</section></div>;
}
