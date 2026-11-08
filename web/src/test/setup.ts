import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';

afterEach(() => {
  cleanup();
  sessionStorage.clear();
  localStorage.clear();
});
