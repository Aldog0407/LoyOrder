import { useCallback, useEffect, useMemo, useState } from 'react';
import { actual, iniciar, cerrar, cuandoVenza } from '../auth.js';
import { AuthContext } from './AuthContext.js';
import { useStore } from './StoreContext.jsx';

export default function AuthProvider({ children }) {
  const [usuario, setUsuario] = useState(actual);
  const { dispatch } = useStore();
  useEffect(() => {
    cuandoVenza(() => {
      dispatch({ type: 'CLEAR_SESSION_DATA' });
      setUsuario(null);
      dispatch({ type: 'NOTIFY', message: 'La sesión venció. Inicia sesión nuevamente.', error: true });
    });
    return () => cuandoVenza(() => {});
  }, [dispatch]);
  const iniciarSesion = useCallback(async datos => {
    const identidad = await iniciar(datos);
    dispatch({ type: 'CLEAR_SESSION_DATA' });
    setUsuario(identidad);
    return identidad;
  }, [dispatch]);
  const cerrarSesion = useCallback(async () => {
    await cerrar();
    dispatch({ type: 'CLEAR_SESSION_DATA' });
    setUsuario(null);
  }, [dispatch]);
  const value = useMemo(() => ({ usuario, iniciarSesion, cerrarSesion }), [usuario, iniciarSesion, cerrarSesion]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
