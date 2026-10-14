import { cpSync, createReadStream, existsSync, statSync } from 'node:fs'
import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { webFilezOutput } from './scripts/paths.mjs'

// The custom element's runtime and static assets, as published by scripts/publish-webfilez.mjs.
const BLAZOR_FOLDERS = ['_framework', '_content']

const CONTENT_TYPES = {
  '.js': 'text/javascript',
  '.mjs': 'text/javascript',
  '.wasm': 'application/wasm',
  '.json': 'application/json',
  '.css': 'text/css',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
}

/** Serves the published WebFilez assets during development and copies them into the build. */
function webFilez() {
  return {
    name: 'webfilez',

    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const url = decodeURIComponent((req.url ?? '').split('?')[0])
        if (!BLAZOR_FOLDERS.some(folder => url.startsWith(`/${folder}/`))) {
          return next()
        }

        const file = path.join(webFilezOutput, url)
        if (!file.startsWith(webFilezOutput + path.sep) || !existsSync(file) || !statSync(file).isFile()) {
          return next()
        }

        res.setHeader('Content-Type', CONTENT_TYPES[path.extname(file)] ?? 'application/octet-stream')
        res.setHeader('Cache-Control', 'no-cache')
        createReadStream(file).pipe(res)
      })
    },

    writeBundle(options) {
      for (const folder of BLAZOR_FOLDERS) {
        cpSync(path.join(webFilezOutput, folder), path.join(options.dir, folder), { recursive: true })
      }
    },
  }
}

export default defineConfig({
  plugins: [react(), webFilez()],
  server: {
    port: 5173,
    strictPort: true,
  },
})
