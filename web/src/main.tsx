import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { ApiClient } from './api/client';
import { loadConfig } from './api/config';
import { App, createQueryClient } from './App';
import './styles.css';

const config = await loadConfig();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <App client={new ApiClient(config.apiBaseUrl)} queryClient={createQueryClient()} />
    </BrowserRouter>
  </StrictMode>,
);
