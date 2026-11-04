/**
 * plugins/index.ts
 *
 * Automatically included in `./src/main.ts`
 */

// Types
import type { App } from 'vue'
// Plugins
import { createPinia } from 'pinia'

import router from '@/router'
import vuetify from './vuetify'

// 说明：登录滑块用的是本地 components/SliderCaptcha.vue（服务端出图 + 像素比对），
// @miitvip/captcha 从未被任何组件引用，属纯启动负担（约 73KB 下载+解析），已移除依赖。
export function registerPlugins (app: App) {
  app.use(vuetify)
  app.use(createPinia())
  app.use(router)
}
