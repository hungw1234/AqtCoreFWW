import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { validateEnv } from '@/core/env-validation';
import { App } from './app';
import '@/app/print-styles.css';
import '@/app/global-styles.css';

// Validate env vars before anything else
validateEnv();

// React 19 createRoot entry point
const rootElement = document.getElementById('root');
if (!rootElement) throw new Error('Root element #root not found');

createRoot(rootElement).render(
  <StrictMode>
    <App />
  </StrictMode>
);
