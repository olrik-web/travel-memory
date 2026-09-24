import { defineConfig } from 'oxlint';

export default defineConfig({
  env: {
    browser: true,
  },
  ignorePatterns: ['dist/**'],
  options: {
    typeAware: true,
  },
  plugins: ['react', 'typescript'],
  rules: {
    'react/jsx-key': 'error',
    'typescript/no-floating-promises': 'error',
  },
});
