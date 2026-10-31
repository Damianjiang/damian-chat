<template>
  <v-app-bar v-if="mode === 'two'" class="wx-shell-bar" density="compact" flat>
    <v-tabs v-model="tab" height="48">
      <v-tab v-for="t in topTabs" :key="t.value" class="wx-top-tab" :value="t.value">
        {{ t.label }}
      </v-tab>
    </v-tabs>
  </v-app-bar>

  <v-navigation-drawer
    v-if="mode === 'three'"
    class="wx-rail"
    :disable-resize-watcher="true"
    permanent
    width="76"
  >
    <v-list class="pt-1" lines="one">
      <v-list-item
        v-for="item in railItems"
        :key="item.to"
        :active="isRailActive(item)"
        class="wx-rail-item"
        @click="navigate(item.to)"
      >
        <div class="wx-rail-inner">
          <v-icon :color="isRailActive(item) ? 'primary' : undefined" :icon="item.icon" size="22" />
          <span class="wx-rail-label">{{ item.label }}</span>
        </div>
      </v-list-item>
    </v-list>
  </v-navigation-drawer>

  <v-main class="wx-main">
    <!-- 手机（<768）：单页 + 底部 tab -->
    <template v-if="mode === 'page'">
      <v-sheet class="wx-phone-page" flat>
        <!-- 方向感知：进详情 = 推入(push)，返回 = 推出(pop)，底部 tab 平级切换 = 淡入 -->
        <transition appear :name="phoneTransition">
          <component :is="phoneComponent" :key="phoneKey" class="wx-panes wx-scroll" />
        </transition>
      </v-sheet>

      <v-bottom-navigation v-if="!hideBottom" class="wx-bottom-nav" height="56">
        <v-btn
          v-for="b in bottomItems"
          :key="b.to"
          :active="bottomTab === b.to"
          :icon="b.icon"
          :text="b.label"
          @click="navigate(b.to)"
        />
      </v-bottom-navigation>
    </template>

    <!-- iPad / 电脑：中列 + 右列（用 flex 容器，避免 v-row/v-col 的栅格约束） -->
    <div v-else class="wx-columns" :class="{ 'wx-columns--moments': momentsWide }">
      <div v-if="!momentsWide" class="wx-middle-col" :class="wxMiddleClass">
        <!-- keep-alive：切走再切回秒开（保留滚动位置与已加载数据），不再每次整棵重挂载重新拉 -->
        <!-- ★ Vue3 正确顺序：keep-alive 包 transition，transition 直接包要切换的组件。
             之前写成 <transition><keep-alive>...</keep-alive></transition>，transition 外层只看到
             keep-alive 一个子节点，不会给每个 section 组件加过渡类 → 部分子页切换无动画。 -->
        <keep-alive>
          <transition appear name="wx-fade">
            <component :is="sectionPane" :key="section" class="wx-panes wx-scroll" />
          </transition>
        </keep-alive>
      </div>

      <div class="wx-right-col">
        <!-- 朋友圈铺满模式：feed 直接渲染进大区域（隐藏窄中列，利用满整个宽度） -->
        <template v-if="momentsWide">
          <keep-alive>
            <transition appear name="wx-fade">
              <component :is="sectionPane" :key="section" class="wx-panes wx-scroll" />
            </transition>
          </keep-alive>
        </template>

        <template v-else-if="detailPane">
          <transition appear name="wx-slide">
            <component :is="detailPane" :key="detail" class="wx-panes" />
          </transition>
        </template>

        <template v-else>
          <v-sheet class="wx-placeholder d-flex flex-column align-center justify-center" flat>
            <!-- 空状态：给一点引导文案，不再是一片死白 -->
            <div class="wx-empty-art">
              <v-icon color="primary" :icon="placeholderIcon" size="64" />
            </div>

            <div class="wx-empty-title">{{ placeholderText }}</div>
            <div class="wx-empty-hint">{{ placeholderHint }}</div>
          </v-sheet>
        </template>
      </div>
    </div>
  </v-main>
</template>

<script setup lang="ts">
  import { computed, ref, watch } from 'vue'
  import { useRoute, useRouter } from 'vue-router'

  import { layoutMode } from '@/lib/device'
  import { t } from '@/lib/i18n'
  import BlockedUsersPane from '@/views/panes/BlockedUsersPane.vue'
  import ChatDetailPane from '@/views/panes/ChatDetailPane.vue'
  import ChatListPane from '@/views/panes/ChatListPane.vue'
  import ContactProfilePane from '@/views/panes/ContactProfilePane.vue'
  import ContactsPane from '@/views/panes/ContactsPane.vue'
  import DiscoveryPane from '@/views/panes/DiscoveryPane.vue'
  import EmojiStorePane from '@/views/panes/EmojiStorePane.vue'
  import FavoritesPane from '@/views/panes/FavoritesPane.vue'
  import GroupPane from '@/views/panes/GroupPane.vue'
  import MePane from '@/views/panes/MePane.vue'
  import MomentDetailPane from '@/views/panes/MomentDetailPane.vue'
  import MomentsPane from '@/views/panes/MomentsPane.vue'
  import PersonalizePane from '@/views/panes/PersonalizePane.vue'
  import ProfilePane from '@/views/panes/ProfilePane.vue'
  import SearchPane from '@/views/panes/SearchPane.vue'
  import SettingsPane from '@/views/panes/SettingsPane.vue'

  const route = useRoute()
  const router = useRouter()

  /**
   * 三端布局：统一在 lib/device.ts 判定（UA + 触点 + 断点），
   * shell 与各 pane 的返回按钮显隐共用同一份 layoutMode / isPageMode。
   */
  const mode = layoutMode

  const sectionPanes: Record<string, any> = {
    chat: ChatListPane,
    contacts: ContactsPane,
    moments: MomentsPane,
    discovery: DiscoveryPane,
    search: SearchPane,
    me: MePane,
  }

  const detailPanes: Record<string, any> = {
    chat: ChatDetailPane,
    group: GroupPane,
    contact: ContactProfilePane,
    moments: MomentDetailPane,
    settings: SettingsPane,
    personalize: PersonalizePane,
    blocked: BlockedUsersPane,
    profile: ProfilePane,
    emojis: EmojiStorePane,
    favorites: FavoritesPane,
  }

  const section = computed(() => (route.meta.section as string) ?? 'chat')
  const detail = computed(() => route.meta.detail as string | undefined)
  const hideBottom = computed(() => !!route.meta.hideBottom)

  const sectionPane = computed(() => sectionPanes[section.value] ?? ChatListPane)
  const detailPane = computed(() => (detail.value ? detailPanes[detail.value] : undefined))
  /** 手机整页切换的过渡 key：section + detail 任一变化即重放过渡 */
  const phoneKey = computed(() => `${section.value}:${detail.value ?? ''}`)

  /**
   * 手机端过渡方向（iOS 式）：
   *   列表 → 详情   用 wx-push（新页从右缘整页推入，旧页左退）
   *   详情 → 列表   用 wx-pop（方向相反，读起来才是"返回"）
   *   底部 tab 平级切换 用 wx-fade（不是层级变化，推入会显得很怪）
   * 监听在渲染前（pre-flush）执行，过渡启动时 name 已是新值。
   */
  const phoneTransition = ref('wx-fade')
  watch(() => [section.value, detail.value ?? ''] as [string, string], ([, nd], [, od]) => {
    if (nd && !od) phoneTransition.value = 'wx-push'
    else if (!nd && od) phoneTransition.value = 'wx-pop'
    else phoneTransition.value = 'wx-fade'
  })

  /** 手机上：detail 路由 → 整页详情；否则整页 section */
  const phoneComponent = computed(() => {
    if (detail.value) return detailPanes[detail.value]
    return sectionPane.value
  })

  function navigate (to: string) {
    router.push(to)
  }

  /* ---------------- 底部导航（手机） ---------------- */
  const bottomItems = computed(() => [
    { icon: 'mdi-chat-outline', label: t('nav.chat'), to: '/chat' },
    { icon: 'mdi-account-multiple', label: t('nav.contacts'), to: '/contacts' },
    { icon: 'mdi-compass', label: t('nav.discovery'), to: '/discovery' },
    { icon: 'mdi-account-outline', label: t('nav.me'), to: '/me' },
  ])

  const bottomTab = computed(() => `/${section.value}`)

  /* ---------------- 顶部 tabs（iPad two） ---------------- */
  const topTabs = computed(() => [
    { label: t('nav.chat'), value: '/chat' },
    { label: t('nav.contacts'), value: '/contacts' },
    { label: t('nav.moments'), value: '/moments' },
    { label: t('nav.discovery'), value: '/discovery' },
    { label: t('nav.me'), value: '/me' },
  ])

  const tab = computed({
    get: () => `/${section.value}`,
    set: (v: string) => router.push(v),
  })

  /* ---------------- 左侧 rail（电脑 three） ---------------- */
  const railItems = computed(() => [
    { icon: 'mdi-chat-outline', label: t('nav.chat'), to: '/chat', prefix: '/chat' },
    { icon: 'mdi-account-multiple', label: t('nav.contacts'), to: '/contacts', prefix: '/contacts' },
    { icon: 'mdi-camera-account', label: t('nav.moments'), to: '/moments', prefix: '/moments' },
    { icon: 'mdi-compass', label: t('nav.discovery'), to: '/discovery', prefix: '/discovery' },
    { icon: 'mdi-account-outline', label: t('nav.me'), to: '/me', prefix: '/me' },
  ])

  function isRailActive (item: { to: string, prefix?: string }) {
    return route.path === item.to || (item.prefix ? route.path.startsWith(item.prefix) : false)
  }

  const wxMiddleClass = computed(() => (mode.value === 'three' ? 'wx-mid-3' : 'wx-mid-2'))

  /**
   * 多栏（iPad/电脑）下，朋友圈 feed 铺满右边主区域（隐藏窄中列 + 相机占位图），
   * 卡片能利用整个宽度，不再浪费大片空白。手机端（page）不变。
   * 仅在朋友圈列表页生效；朋友圈详情（detail=moments）仍走右侧详情列。
   */
  const momentsWide = computed(() =>
    mode.value !== 'page'
    && section.value === 'moments'
    && detail.value !== 'moments',
  )

  /** 右列占位：按当前栏目给出不同引导文案 */
  const placeholderMap: Record<string, { icon: string, key: string }> = {
    chat: { icon: 'mdi-chat-outline', key: 'shell.empty.chat' },
    contacts: { icon: 'mdi-account-outline', key: 'shell.empty.contacts' },
    moments: { icon: 'mdi-camera-account', key: 'shell.empty.moments' },
    discovery: { icon: 'mdi-compass', key: 'shell.empty.discovery' },
    search: { icon: 'mdi-magnify', key: 'shell.empty.search' },
    me: { icon: 'mdi-cog-outline', key: 'shell.empty.me' },
  }

  const placeholderIcon = computed(() => placeholderMap[section.value]?.icon ?? 'mdi-chat-outline')
  /** 空状态副标题：告诉用户下一步做什么，而不是留一片空白 */
  const placeholderHint = computed(() => {
    switch (section.value) {
      case 'chat': return t('shell.empty.chat.hint')
      case 'contacts': return t('shell.empty.contacts.hint')
      case 'moments': return t('shell.empty.moments.hint')
      case 'discovery': return t('shell.empty.discovery.hint')
      case 'search': return t('shell.empty.search.hint')
      case 'me': return t('shell.empty.me.hint')
      default: return ''
    }
  })

  const placeholderText = computed(() => t(placeholderMap[section.value]?.key ?? 'shell.empty.chat'))
</script>

<style scoped>
.wx-shell-bar {
  z-index: 5;
}

.wx-top-tab {
  min-width: 110px;
}

.wx-rail {
  width: 88px;
  background: rgb(var(--v-theme-surface));
  border-right: 1px solid rgb(var(--v-theme-wechat-divider, #e5e5e5));
}

/* rail 项：图标在上、文字在下（PC 端样式），文字不裁切 */
.wx-rail-item {
  padding: 0 !important;
  min-width: 0 !important;
  margin: 2px 8px;
  border-radius: 10px;
  overflow: hidden;
}

/* 选中态：浅灰底 + 主色文字（不再是绿色大色块） */
.wx-rail-item.v-list-item--active {
  background: rgb(var(--v-theme-on-surface), 0.06);
}

.wx-rail-item.v-list-item--active .wx-rail-label {
  color: rgb(var(--v-theme-primary));
  font-weight: 500;
}

.wx-rail-item :deep(.v-list-item__content) {
  width: 100%;
  min-width: 0;
  padding: 0 !important;
}

/* 图标在上、文字在下（PC 端样式），文字不裁切 */
.wx-rail-inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  width: 100%;
  padding: 10px 0;
}

.wx-rail-label {
  font-size: 12px;
  line-height: 1.2;
  white-space: nowrap;
  text-align: center;
  color: inherit;
}

.wx-main {
  position: relative;
  /* 关键：v-main 默认高度自适应，导致下游 height:100% 全部失效，
     聊天区被内容撑高、输入框被挤出视口。这里锁死为可视区高度并允许内部滚动。 */
  height: calc(100dvh - var(--v-app-bar-height, 0px));
  min-height: 0;
  overflow: hidden;
}

.wx-phone-page {
  /* 撑满 .v-main 内容区（已扣除顶栏/底部导航的布局内边距），高度自适应 */
  position: relative; /* 过渡期间两页绝对定位叠放的参照系 */
  height: 100%;
  min-height: 0;
  max-height: 100%;
  overflow: hidden;
}

/* ★ 之前这里是 flex 常规流：过渡期间新旧两页同时存在，会各占一半高度，
   于是每次切页都先"矮一半"再弹回 —— 这是切页观感最差的主因。
   改成绝对定位叠放后，两页共享同一块坐标，互不挤压（与中列/右列一致）。 */
.wx-phone-page > .wx-panes {
  position: absolute;
  inset: 0;
}

.wx-bottom-nav {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 5;
  box-shadow: 0 0 12px rgba(0, 0, 0, 0.08);
  padding-bottom: env(safe-area-inset-bottom, 0px);
}

.wx-columns {
  display: flex;
  height: 100%;
  width: 100%;
  min-height: 0;
  overflow: hidden;
}

/* 中列宽度：保证列表/通讯录/设置能完整显示标题与搜索框 */
.wx-mid-2 {
  flex: 1 1 360px;
  min-width: 320px;
  max-width: 440px;
}

.wx-mid-3 {
  flex: 0 0 clamp(340px, 30vw, 400px);
}

.wx-middle-col,
.wx-right-col {
  height: 100%;
  min-height: 0;
  overflow: hidden;
  border-right: 1px solid rgb(var(--v-theme-wechat-divider, #e5e5e5));
  /* 让过渡期并存的新旧两页同坐标叠放（绝对定位），避免普通流撑高导致布局跳动 */
  position: relative;
}

/* 过渡子级叠放：进入/离开的两页占同一块坐标，互不推挤 */
.wx-middle-col > .wx-panes,
.wx-right-col > .wx-panes {
  position: absolute;
  inset: 0;
}

.wx-right-col {
  border-right: none;
  flex: 1 1 auto;
  min-width: 0;
}

/* 朋友圈铺满模式：中列隐藏后右列占满全宽，feed 从左紧贴 rail 一直铺到右边，
   消除 rail 与 feed 之间的大片白空档（原来居中 760px 会留左右白缝） */
.wx-columns--moments .wx-right-col > .wx-panes {
  left: 0;
  right: 0;
  width: auto;
  max-width: none;
  transform: none;
}

.wx-panes {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-height: 0;
}

.wx-placeholder {
  height: 100%;
  min-height: 0;
  background: rgb(var(--v-theme-surface));
}

/* 空状态：柔和的圆形底衬 + 两行文案 */
.wx-empty-art {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 132px;
  height: 132px;
  margin-bottom: 20px;
  border-radius: 50%;
  background: rgb(var(--v-theme-on-surface), 0.04);
}

.wx-empty-title {
  font-size: 15px;
  color: rgb(var(--v-theme-on-surface), 0.65);
}

.wx-empty-hint {
  margin-top: 6px;
  font-size: 13px;
  color: rgb(var(--v-theme-on-surface), 0.4);
}
</style>
