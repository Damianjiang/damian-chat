/**
 * plugins/index.ts
 *
 * Automatically included in `./src/main.ts`
 */

// Types
import type { App } from 'vue'
// 开源滑块人机验证组件（@miitvip/captcha，MIT）
import MiCaptcha from '@miitvip/captcha'
// Plugins
import { createPinia } from 'pinia'

import router from '@/router'
import vuetify from './vuetify'

export function registerPlugins (app: App) {
  app.use(vuetify)
  app.use(MiCaptcha)
  app.use(createPinia())
  app.use(router)
}
