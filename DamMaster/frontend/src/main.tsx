import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { AuthProvider } from './auth/AuthContext';
import { ToastProvider } from './components/ui';
import './styles.css';

// offline shell for the field app (production build only)
if ('serviceWorker' in navigator && import.meta.env.PROD) window.addEventListener('load', () => { void navigator.serviceWorker.register('/sw.js'); });

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <ToastProvider><App /></ToastProvider>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);
