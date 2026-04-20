import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tsconfigPaths from 'vite-tsconfig-paths'

// Separate from vite.config.ts so tests don't go through the TanStack Start SSR pipeline.
export default defineConfig({
  plugins: [tsconfigPaths({ projects: ['./tsconfig.json'] }), react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['node_modules', 'dist', '.tanstack', 'build'],
  },
})
