import type { HttpHandler } from 'msw'

// Default handlers applied to every test. Per-test overrides: `server.use(http.get(...))`.
// Add entries here as endpoints get wired up, e.g.:
//   http.get('http://localhost:5258/menu', () => HttpResponse.json({ categories: [] })),
export const handlers: Array<HttpHandler> = []
