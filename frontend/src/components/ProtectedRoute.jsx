import { Navigate } from 'react-router';
import { useAuth } from '../state/AuthContext.js';
import { destinoProtegido, rutaInicial } from '../utils/auth-routing.js';

export default function ProtectedRoute({ roles, children }) {
  const { usuario } = useAuth();
  const destino = destinoProtegido(usuario, roles);
  return destino ? <Navigate replace to={destino}/> : children;
}
export function PublicOnly({ children }) {
  const { usuario, destinoAcceso } = useAuth();
  return usuario ? <Navigate replace to={destinoAcceso ?? rutaInicial(usuario)}/> : children;
}
