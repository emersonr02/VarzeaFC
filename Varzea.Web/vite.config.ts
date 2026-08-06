import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Varzea.Api roda em http://localhost:52525 (ver Varzea.Api/Properties/launchSettings.json).
// Proxy só existe em dev — em produção o React é servido estático pelo próprio ASP.NET
// (decisão travada do HANDOFF §2), então não há CORS pra resolver de verdade.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/careers': 'http://localhost:52525',
      '/rankings': 'http://localhost:52525',
      '/challenge': 'http://localhost:52525',
      '/meta': 'http://localhost:52525',
    },
  },
})
