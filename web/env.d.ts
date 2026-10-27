/// <reference types="vite/client" />

declare module '*.scss'
declare module '*.css'
declare module 'unfonts.css'

// 兜底：纯 JS 的第三方 .vue 组件（如 vendor/dragVerifyImgChip.vue）从 <script setup lang="ts">
// 导入时 vue-tsc 推不出类型（TS7016）。这里给出通用组件类型；能真实推断的 .vue 仍以各自文件为准。
declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>
  export default component
}

// 构建期注入：当前打包版本的唯一标识（vite define 全局），用于与服务端 /version.json 比对「是否有更新」。
declare const __BUILD_VERSION__: string
