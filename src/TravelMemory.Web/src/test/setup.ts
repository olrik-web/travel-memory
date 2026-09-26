import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// Without Vitest globals, Testing Library cannot register its automatic cleanup, so
// rendered components would pile up across the tests in a file.
afterEach(cleanup);
