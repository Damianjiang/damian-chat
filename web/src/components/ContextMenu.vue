<script setup lang="ts">
/**
 * ContextMenu —— 可复用的"落在指针位置"右键/长按菜单浮层。
 *
 * 定位：position: fixed，left = x, top = y（CSS 坐标 = clientX/clientY，就是指针位置）。
 * 防溢出：菜单渲染后（requestAnimationFrame）测量实际尺寸，若右/下边缘超出视口则自动向左/上平移；
 *         同时保证不贴出左上角（≥ 8px 边距）。
 * 关闭：点遮罩 / 右键 / ESC / 滚动 都关。
 *
 * ★ 关键设计（修旧 bug）：
 *   旧版用 adjLeft/adjTop = ref(0) 存 clamp 后的位置，Teleport + v-if 的 DOM 渲染
 *   比 nextTick 晚一拍时 menuRef 还是 null → adjLeft/adjTop 停在 0 → 菜单飞到左上角。
 *   新版 menuStyle 直接用 props.x/props.y（0 时就是 (0,0)，但那是初始值，不是"错"），
 *   菜单渲染后 requestAnimationFrame 再更新 adjLeft/adjTop（此时 DOM 一定存在）。
 */
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps<{
  modelValue: boolean
  x: number
  y: number
  width?: number
  maxHeight?: number
}>()

const emit = defineEmits<{
  'update:modelValue': [v: boolean]
  close: []
}>()

const menuRef = ref<HTMLElement | null>(null)
// adj 初始 = -1（"未计算"哨兵）：menuStyle 里 adj=-1 时回退用 props.x/props.y，
// 避免 ref(0) 导致菜单飞到左上角的旧 bug
const adjLeft = ref(-1)
const adjTop  = ref(-1)

/** 重新计算 clamp 位置（菜单已在 DOM 里，requestAnimationFrame 保证渲染完成） */
function reposition () {
  const el = menuRef.value
  if (!el) return
  const w = el.offsetWidth
  const h = el.offsetHeight
  const pad = 8
  let left = props.x
  let top = props.y
  // 右溢出 → 向左收
  if (left + w > window.innerWidth - pad) left = window.innerWidth - w - pad
  // 下溢出 → 向上收
  if (top + h > window.innerHeight - pad) top = window.innerHeight - h - pad
  // 不出左上角
  left = Math.max(pad, left)
  top = Math.max(pad, top)
  adjLeft.value = left
  adjTop.value = top
}

// 菜单打开 → 下一帧定位
watch(() => props.modelValue, async (open) => {
  if (!open) {
    // 关闭时重置 adj（避免下次打开闪现旧位置）
    adjLeft.value = -1
    adjTop.value = -1
    return
  }
  // 等待 DOM 渲染完成（Teleport + v-if 需要 nextTick）
  await nextTick()
  // 再等到浏览器实际绘制完（requestAnimationFrame）
  requestAnimationFrame(() => reposition())
}, { immediate: true })

// x/y 变化时（菜单打开期间指针移动）也重新定位
watch([() => props.x, () => props.y], () => {
  if (!props.modelValue) return
  requestAnimationFrame(() => reposition())
})

function close () {
  emit('update:modelValue', false)
  emit('close')
}

function onKeydown (e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}

function onScrollResize () { close() }

watch(() => props.modelValue, (open) => {
  if (open) {
    window.addEventListener('keydown', onKeydown)
    window.addEventListener('scroll', onScrollResize, true)
    window.addEventListener('resize', onScrollResize)
  } else {
    window.removeEventListener('keydown', onKeydown)
    window.removeEventListener('scroll', onScrollResize, true)
    window.removeEventListener('resize', onScrollResize)
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
  window.removeEventListener('scroll', onScrollResize, true)
  window.removeEventListener('resize', onScrollResize)
})

/**
 * 核心：adjLeft/adjTop = -1（"尚未计算"）→ 直接用 props.x/props.y（指针位置）。
 * 已计算 → 用 adj 值（经过视口内 clamp 后的安全位置）。
 * 这样即使 DOM 还没渲染出来（adj=-1），位置也已是正确的指针坐标，不会飞左上角。
 */
const menuStyle = computed(() => ({
  left: (adjLeft.value >= 0 ? adjLeft.value : props.x) + 'px',
  top:  (adjTop.value  >= 0 ? adjTop.value  : props.y) + 'px',
  ...(props.width ? { width: props.width + 'px' } : {}),
  maxHeight: (props.maxHeight ?? 480) + 'px',
}))
</script>

<template>
  <Teleport to="body">
    <template v-if="modelValue">
      <!-- 遮罩：透明但拦截点击，z-index 在菜单下方 -->
      <div class="ctx-backdrop" @click="close" @contextmenu.prevent="close"></div>
      <!-- 菜单本体：fixed 定位，落在指针处 -->
      <div
        ref="menuRef"
        class="ctx-menu"
        :style="menuStyle"
        @click.stop
        @contextmenu.prevent.stop
      >
        <slot />
      </div>
    </template>
  </Teleport>
</template>

<style scoped>
.ctx-backdrop {
  position: fixed;
  inset: 0;
  z-index: 2900;
  background: transparent;
}

.ctx-menu {
  position: fixed;
  z-index: 2901;
  background: rgb(var(--v-theme-surface, #fff));
  border-radius: 10px;
  box-shadow:
    0 4px 16px rgba(0, 0, 0, 0.14),
    0 1px 4px rgba(0, 0, 0, 0.08);
  overflow: hidden;
  animation: ctx-in 0.12s ease-out;
}

@keyframes ctx-in {
  from { opacity: 0; transform: scale(0.96); }
  to   { opacity: 1; transform: scale(1); }
}

.v-theme--dark .ctx-menu {
  background: rgb(var(--v-theme-surface, #242424));
  box-shadow:
    0 4px 16px rgba(0, 0, 0, 0.4),
    0 1px 4px rgba(0, 0, 0, 0.3);
}
</style>
