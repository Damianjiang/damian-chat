<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">个性化设置</v-toolbar-title>
    </v-app-bar>

    <div class="wx-personalize-bg flex-grow-1 overflow-y-auto wx-scroll pa-2">
      <!-- 外观：主题配色 / 深色模式 / 字体大小（从设置根目录移入） -->
      <v-list bg-color="surface" class="wx-settings-group">
        <v-subheader>外观</v-subheader>

        <v-list-item>
          <v-list-item-title>主题配色</v-list-item-title>
          <template #append>
            <div class="dm-theme-swatches">
              <button
                v-for="p in theme.presets"
                :key="p.id"
                class="dm-swatch"
                :class="{ 'dm-swatch-active': p.id === theme.themeId }"
                :style="{ background: p.primary }"
                :title="p.name"
                type="button"
                @click="theme.setTheme(p.id)"
              />
            </div>
          </template>
        </v-list-item>

        <v-list-item>
          <v-list-item-title>深色模式</v-list-item-title>
          <template #append>
            <v-btn-toggle
              :model-value="theme.mode"
              class="wx-mode-toggle"
              density="compact"
              mandatory
              @update:model-value="(v: any) => theme.setMode(v)"
            >
              <v-btn value="light">亮</v-btn>
              <v-btn value="dark">暗</v-btn>
              <v-btn value="system">跟随</v-btn>
            </v-btn-toggle>
          </template>
        </v-list-item>

        <v-list-item @click="openFontDialog">
          <v-list-item-title>字体大小</v-list-item-title>
          <template #append>
            <div class="d-flex align-center gap-1" @click.stop>
              <span class="text-body-small text-medium-emphasis wx-font-cur">{{ fontLabels[fontLevel] }}</span>
              <v-btn icon="mdi-eye-outline" size="x-small" variant="text" title="字体预览" @click="openFontDialog" />
            </div>
          </template>
        </v-list-item>
      </v-list>

      <!-- 组件与特效 -->
      <v-list bg-color="surface" class="wx-settings-group mt-2">
        <v-subheader>组件与特效</v-subheader>

        <v-list-item @click="openAlphaDialog">
          <template #prepend><v-icon icon="mdi-opacity" /></template>
          <v-list-item-title>组件半透明度</v-list-item-title>
          <v-list-item-subtitle>调低让顶栏 / 卡片 / 面板变半透明，透出背景（默认 100 = 不透明）</v-list-item-subtitle>
          <template #append>
            <div class="d-flex align-center gap-1" @click.stop>
              <span class="text-body-small text-medium-emphasis wx-font-cur">{{ alpha }}%</span>
              <v-btn icon="mdi-tune-variant" size="x-small" variant="text" title="设置半透明度" @click="openAlphaDialog" />
            </div>
          </template>
        </v-list-item>

        <v-list-item>
          <template #prepend><v-icon icon="mdi-weather-windy" /></template>
          <v-list-item-title>动态效果</v-list-item-title>
          <v-list-item-subtitle>进场动画 / 消息浮现 / 呼吸 logo 等（关闭后全站停非必要动画）</v-list-item-subtitle>
          <template #append><v-switch v-model="motion" color="primary" density="compact" hide-details @update:model-value="toggleFx" /></template>
        </v-list-item>

        <v-list-item>
          <template #prepend><v-icon icon="mdi-cube-outline" /></template>
          <v-list-item-title>3D 倾斜</v-list-item-title>
          <v-list-item-subtitle>登录卡片随指针轻微倾斜（关闭则保留玻璃质感、不再倾斜）</v-list-item-subtitle>
          <template #append><v-switch v-model="tilt" color="primary" density="compact" hide-details @update:model-value="toggleFx" /></template>
        </v-list-item>

        <v-list-item>
          <template #prepend><v-icon icon="mdi-water" /></template>
          <v-list-item-title>登录页波纹</v-list-item-title>
          <v-list-item-subtitle>登录卡片后的蓝色动波纹背景（关闭显示静态纯色底）</v-list-item-subtitle>
          <template #append><v-switch v-model="ripple" color="primary" density="compact" hide-details @update:model-value="toggleFx" /></template>
        </v-list-item>
      </v-list>

      <div class="text-center text-body-small text-medium-emphasis py-6">
        以上均为本地偏好，实时生效，仅保存在这台设备上。
      </div>
    </div>

    <v-snackbar v-model="snack" color="success" :timeout="1800">{{ snackMsg }}</v-snackbar>

    <!-- 字体大小预览：选一个字号，右侧按该字号实时渲染样张，应用/还原 -->
    <v-dialog v-model="fontDialog" max-width="420">
      <v-card>
        <v-card-title class="text-body-large">字体大小</v-card-title>
        <v-card-text class="d-flex gap-4">
          <div class="wx-font-opt-col">
            <button
              v-for="(f, i) in fontLabels"
              :key="i"
              class="wx-font-chip2"
              :class="{ 'is-on': i === fontDraft }"
              :type="'button'"
              @click="previewFont(i)"
            >{{ f }}</button>
          </div>
          <div class="wx-font-preview">
            <div class="text-caption text-medium-emphasis mb-1">预览（{{ fontLabels[fontDraft] }}）</div>
            <div :style="{ fontSize: fontPreviewPx + 'px', lineHeight: 1.5 }">
              <p class="font-weight-medium mb-1">标题示例</p>
              <p>这是一段正文，用来预览字体大小在界面里的实际效果。</p>
              <p class="text-medium-emphasis">次要文字 / 说明文案也一并放大。</p>
            </div>
          </div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="fontDialog = false">取消</v-btn>
          <v-btn color="primary" @click="applyFontDraft">应用</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 组件半透明度：点击行弹出，窗内滑条实时调节（草稿 + 应用，与字体预览同一交互范式） -->
    <v-dialog v-model="alphaDialog" max-width="360">
      <v-card>
        <v-card-title class="text-body-large">组件半透明度</v-card-title>
        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-2">调低让顶栏 / 卡片 / 面板变半透明、透出背景；100 = 完全不透明。</div>
          <div class="wx-alpha-preview">
            <div class="text-caption text-medium-emphasis mb-1">预览（{{ alphaDraft }}% 不透明）</div>
            <div class="wx-alpha-preview-box">
              <div
                class="wx-alpha-preview-card"
                :style="{ background: `rgba(255,255,255,${(alphaDraft / 100).toFixed(2)})` }"
              >
                <span>顶栏</span>
                <span>卡片</span>
                <span>面板</span>
              </div>
            </div>
          </div>
          <div class="d-flex align-center gap-2 mt-3">
            <v-icon color="medium-emphasis" icon="mdi-tune" size="20" />
            <v-slider
              v-model="alphaDraft"
              class="wx-alpha-slider"
              color="primary"
              :max="100"
              :min="50"
              :step="1"
            />
            <span class="text-body-medium text-medium-emphasis" style="min-width: 40px">{{ alphaDraft }}%</span>
          </div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="alphaDialog = false">取消</v-btn>
          <v-btn color="primary" @click="applyAlphaDraft">应用</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-sheet>
</template>

<script setup lang="ts">
  import { computed, onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import { isPageMode } from '@/lib/device'
  import { getFx, getAlpha, saveFx, saveAlpha, getFontLevel, saveFontLevel, FONT_LABELS, FONT_SCALES } from '@/lib/personalize'
  import { useThemeStore } from '@/stores/theme'

  const router = useRouter()
  const theme = useThemeStore()
  const isMobile = isPageMode

  const snack = ref(false)
  const snackMsg = ref('')
  function toast (m: string) { snackMsg.value = m; snack.value = true }

  function goBack () {
    router.push({ name: 'settings' })
  }

  /* ---------- 字体大小（与登录页/旧设置页共用 wx_font_level） ---------- */
  const fontLabels = FONT_LABELS
  const fontScales = FONT_SCALES
  const fontLevel = ref(getFontLevel())
  /** 打开预览弹窗 + 在弹窗内实时预览的草稿级别（点「应用」才真正改全站字号） */
  const fontDialog = ref(false)
  const fontDraft = ref(0)

  /** 弹窗里样张用的预览字号（px），随 fontDraft 实时变化，不影响当前全站字号 */
  const fontPreviewPx = computed(() => (16 * (fontScales[fontDraft.value] ?? 1)).toFixed(1))

  function openFontDialog () {
    fontDraft.value = fontLevel.value
    fontDialog.value = true
  }

  /** 弹窗内点「小/标准/大/特大」：只更新预览样张，不应用 */
  function previewFont (level: number) {
    fontDraft.value = level
  }

  /** 弹窗内点「应用」：走共享 saveFontLevel（即时生效 + 落盘，刷新后 initPersonalize 自动恢复） */
  function applyFontDraft () {
    fontLevel.value = fontDraft.value
    saveFontLevel(fontDraft.value)
    fontDialog.value = false
    toast(`字号已设为「${fontLabels[fontDraft.value]}」`)
  }

  /* ---------- 特效开关 / 半透明（背景图功能已移除） ---------- */
  const motion = ref(true)
  const tilt = ref(true)
  const ripple = ref(true)
  const alpha = ref(100)
  /** 半透明度弹窗（点击行 → 窗内滑条调节，与字体预览同一「草稿 + 应用」范式） */
  const alphaDialog = ref(false)
  const alphaDraft = ref(100)

  function openAlphaDialog () {
    alphaDraft.value = alpha.value
    alphaDialog.value = true
  }

  /** 弹窗内点「应用」：真正落盘并实时生效，关掉弹窗 */
  function applyAlphaDraft () {
    const n = Math.min(100, Math.max(50, Math.round(alphaDraft.value)))
    alpha.value = n
    saveAlpha(n)
    alphaDialog.value = false
    toast(`组件半透明度已设为 ${n}%`)
  }

  /** 任一特效开关变化 → 整体保存（ripple/tilt/motion 三态一起落盘 + 实时生效） */
  function toggleFx () {
    saveFx({ ripple: ripple.value, tilt: tilt.value, motion: motion.value })
  }

  onMounted(() => {
    // 恢复持久化的特效开关与半透明
    const fx = getFx()
    motion.value = fx.motion
    tilt.value = fx.tilt
    ripple.value = fx.ripple
    alpha.value = getAlpha()

    // 恢复字号（共享持久化，main.ts 启动时已应用，这里同步 ref 显示）
    fontLevel.value = getFontLevel()
  })
</script>

<style scoped>
.wx-personalize-bg {
  background: #ededed;
}

.v-theme--dark .wx-personalize-bg {
  background: #121212;
}

.wx-settings-group {
  border-radius: 10px;
  overflow: hidden;
}

/* 字号行：当前字号标签 */
.wx-font-cur {
  font-size: 12px;
  min-width: 30px;
  text-align: right;
}

/* 字体预览弹窗：左侧选项列 + 右侧实时样张 */
.wx-font-opt-col {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.wx-font-chip2 {
  padding: 6px 14px;
  border-radius: 999px;
  font-size: 13px;
  text-align: center;
  color: rgb(var(--v-theme-on-surface, 120 120 120));
  background: rgb(var(--v-theme-wechat-divider, 229 229 229), 0.5);
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.12s ease;
}
.wx-font-chip2.is-on {
  color: rgb(var(--v-theme-on-primary, 255));
  background: rgb(var(--v-theme-primary, 7 193 96));
}
.wx-font-preview {
  flex: 1;
  min-height: 128px;
  padding: 12px;
  border-radius: 10px;
  background: rgb(var(--v-theme-wechat-divider, 229 229 229), 0.35);
  overflow: hidden;
}
.wx-font-preview p {
  margin: 0 0 6px;
}

/* 半透明度弹窗预览：底盒半透明 + 内卡片跟随草稿透明度，直观看出透出程度 */
.wx-alpha-preview {
  min-height: 120px;
  padding: 12px;
  border-radius: 10px;
  background:
    linear-gradient(135deg, rgba(7,193,96,.5), rgba(25,118,210,.45)),
    repeating-linear-gradient(45deg, #9fb0c0 0 8px, #cdd7e0 8px 16px);
  overflow: hidden;
}
.wx-alpha-preview-card {
  display: flex;
  gap: 8px;
  justify-content: center;
  padding: 14px;
  border-radius: 10px;
  color: #1f2933;
  font-size: 13px;
  background: rgb(255 255 255 / calc(var(--wx-alpha-draft, 1) * 1));
}

.wx-mode-toggle {
  width: 150px;
  justify-content: space-between;
}
.wx-mode-toggle :deep(.v-btn) {
  flex: 1;
  margin: 0 2px;
  height: 30px;
}

/* 主题色板 */
.dm-theme-swatches {
  display: flex;
  gap: 6px;
  align-items: center;
}
.dm-swatch {
  width: 20px;
  height: 20px;
  border: 2px solid transparent;
  border-radius: 50%;
  cursor: pointer;
  padding: 0;
  transition: transform 0.12s ease;
}
.dm-swatch:hover {
  transform: scale(1.12);
}
.dm-swatch-active {
  border-color: rgb(var(--v-theme-on-surface));
  transform: scale(1.12);
}
</style>
