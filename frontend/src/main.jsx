import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { StoreProvider } from './state/StoreContext.jsx';
import AuthProvider from './state/AuthProvider.jsx';
import App from './App.jsx';
import './styles/styles.css';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <StoreProvider><AuthProvider><App /></AuthProvider></StoreProvider>
    </BrowserRouter>
  </React.StrictMode>,
);
