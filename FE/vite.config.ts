// `vitest/config` re-exports vite's defineConfig widened with the `test` block below.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  test: {
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    // The Playwright suite lives under e2e/ and is run by its own runner.
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['e2e/**', 'node_modules/**'],
    // `default` keeps the human-readable summary on stdout; the JSON file is what
    // tools/spec-coverage reads the @spec: markers out of.
    reporters: [
      'default',
      ['json', { outputFile: '../docs/test-coverage/raw/vitest.json' }],
    ],
    coverage: {
      provider: 'v8',
      reportsDirectory: '../docs/test-coverage/frontend',
      reporter: ['text-summary', 'html', 'json-summary', 'lcov'],
      // Every frontend coverage exclusion is declared here, per
      // automated-testing/code-coverage-gate/exclusions-are-declared.
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        'src/main.tsx',            // bootstrap: mounts the app, nothing to assert
        'src/routes.tsx',          // route table: declaration, exercised by e2e
        'src/vite-env.d.ts',
        'src/types/**',            // type-only modules, no emitted code
        'src/test/**',             // the harness itself
        'src/**/*.{test,spec}.{ts,tsx}',
        'e2e/**',
      ],
      // Deliberately no `thresholds` here. The 80% gate is evaluated by tools/coverage-gate
      // instead: a vitest threshold fails the whole frontend run, which would hide real test
      // failures behind a coverage failure while only user-sessions has tests. See design D7.
    },
  },

  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/hubs': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
        ws: true,
      },
      '/files': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/register': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/login': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/refresh': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/forgotPassword': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/resetPassword': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
