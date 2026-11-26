import { useId, useState } from 'react';

/** Control reutilizable: la contraseña solo cambia de visibilidad, no de valor. */
export default function PasswordInput({ label, name, autoComplete, minLength, maxLength = 128, describedBy }) {
  const id = useId();
  const [visible, setVisible] = useState(false);
  return <div className="password-field">
    <label htmlFor={id}>{label}</label>
    <div className="password-control">
      <input
        id={id}
        name={name}
        type={visible ? 'text' : 'password'}
        required
        minLength={minLength}
        maxLength={maxLength}
        autoComplete={autoComplete}
        aria-describedby={describedBy}
      />
      <button
        type="button"
        className="password-toggle"
        aria-pressed={visible}
        aria-label={`${visible ? 'Ocultar' : 'Mostrar'} ${label.toLowerCase()}`}
        onClick={() => setVisible(value => !value)}
      >{visible ? 'Ocultar' : 'Mostrar'}</button>
    </div>
  </div>;
}
