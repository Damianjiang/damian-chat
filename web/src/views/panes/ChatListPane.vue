<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <div class="wx-pane-header d-flex align-center justify-space-between pa-2">
      <span class="wx-pane-title">大面聊天</span>

      <div class="d-flex align-center gap-1 ml-auto">
        <v-btn icon="mdi-magnify" size="small" variant="text" @click="search = true" />

        <v-menu location="bottom end">
          <template #activator="{ props }">
            <v-btn icon="mdi-plus-circle-outline" size="small" variant="text" v-bind="props" />
          </template>

          <v-list density="compact" width="180">
            <v-list-item @click="createGroup">
              <template #prepend><v-icon icon="mdi-message-badge-outline" size="20" /></template>
              <v-list-item-title>发起群聊</v-list-item-title>
            </v-list-item>

            <v-list-item @click="router.push({ name: 'contacts' })">
              <template #prepend><v-icon icon="mdi-account-plus-outline" size="20" /></template>
              <v-list-item-title>添加朋友</v-list-item-title>
            </v-list-item>

            <v-list-item @click="router.push({ name: 'discovery' })">
              <template #prepend><v-icon icon="mdi-qrcode-scan" size="20" /></template>
              <v-list-item-title>扫一扫</v-list-item-title>
            </v-list-item>
          </v-list>
        </v-menu>
      </div>
    </div>

    <v-dialog v-model="search" max-width="420" scrollable>
      <v-card>
        <v-text-field
          v-model="kw"
          autofocus
          clearable
          hide-details
          placeholder="搜索会话 / 联系人"
          prepend-inner-icon="mdi-magnify"
          variant="solo"
          @keydown.esc="closeSearch"
        />

        <div class="wx-search-body">
          <search-result-list :items="searchResults" @select="onSearchPick" />
        </div>
      </v-card>
    </v-dialog>

    <div class="flex-grow-1 overflow-y-auto wx-scroll">
      <!-- 折叠群聊入口：集中管理所有被折叠的群聊（含恢复/打开）。无折叠群时仍保留入口方便进入（空态提示） -->
      <v-list class="wx-folded-entry" lines="one">
        <v-list-item @click="foldedDialog = true">
          <template #prepend>
            <v-avatar color="secondary" :size="40">
              <v-icon icon="mdi-folder-lock-open" />
            </v-avatar>
          </template>

          <template #title>
            <span>折叠群聊</span>
            <v-badge v-if="foldedGroups.length > 0" class="ml-2" color="wechat-unread" :content="foldedGroups.length" />
          </template>

          <template #append>
            <span class="text-body-small text-medium-emphasis">{{ foldedGroups.length }} 个</span>
            <v-icon icon="mdi-chevron-right" />
          </template>
        </v-list-item>
      </v-list>

      <session-list :active-id="store.activeId" :sessions="visibleSessions" @open="onOpen" />
    </div>

    <!-- 折叠群聊管理：列出所有被折叠的群 + 恢复 / 打开 / 取消免打扰 -->
    <v-dialog v-model="foldedDialog" max-width="440" scrollable>
      <v-card>
        <v-card-title class="text-body-large d-flex align-center gap-2">
          <v-icon color="secondary" icon="mdi-folder-lock-open" />
          折叠群聊
        </v-card-title>

        <v-list class="pa-0">
          <v-list-item v-for="s in foldedGroups" :key="s.id">
            <template #prepend>
              <v-avatar :color="avatarColor(s.id)" :size="40">
                <img v-if="s.avatar" :alt="s.title" :src="s.avatar">
                <span v-else>{{ s.title.slice(0, 1) }}</span>
              </v-avatar>
            </template>

            <template #title>
              <div class="d-flex align-center">
                <span class="text-truncate">{{ s.title }}</span>
                <v-icon v-if="s.muted" class="ml-1" color="secondary" icon="mdi-volume-off" size="small" />
              </div>
            </template>

            <template #subtitle>
              <span class="text-body-small text-medium-emphasis">
                {{ s.unread > 0 ? `${s.unread} 条未读 · ` : '' }}已折叠
              </span>
            </template>

            <template #append>
              <v-btn class="mr-1" size="x-small" variant="text" @click="restoreFolded(s)">恢复</v-btn>
              <v-btn color="primary" size="x-small" variant="text" @click="openFolded(s)">打开</v-btn>
            </template>
          </v-list-item>

          <v-list-item v-if="foldedGroups.length === 0" class="justify-center" disabled>
            <v-list-item-title class="text-medium-emphasis">暂无被折叠的群聊</v-list-item-title>
          </v-list-item>
        </v-list>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="foldedDialog = false">关闭</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 发起群聊：全屏选人面板 -->
    <v-dialog v-model="groupSheet" fullscreen transition="dialog-bottom-transition">
      <v-card class="wx-group-create">
        <v-toolbar class="wx-group-toolbar" density="compact" flat>
          <v-btn icon="mdi-close" variant="text" @click="groupSheet = false" />
          <v-toolbar-title class="text-body-large font-weight-medium">发起群聊</v-toolbar-title>
          <v-spacer />

          <v-btn color="primary" :disabled="gMembers.length === 0" variant="text" @click="openNameDialog">
            下一步
          </v-btn>
        </v-toolbar>

        <v-text-field
          v-model="gQuery"
          class="ma-2"
          clearable
          density="compact"
          hide-details
          placeholder="搜索"
          prepend-inner-icon="mdi-magnify"
          variant="solo"
        />

        <div class="wx-pick-body">
          <!-- 左：通讯录，每人带勾选框 -->
          <div class="wx-pick-left">
            <v-list bg-color="surface">
              <v-list-item
                v-for="c in filteredFriends"
                :key="c.id"
                class="wx-pick-row"
                @click="toggleMember(uidOf(c))"
              >
                <template #prepend>
                  <v-checkbox-btn
                    color="primary"
                    :model-value="gMembers.includes(uidOf(c))"
                    @click.stop
                    @update:model-value="toggleMember(uidOf(c))"
                  />
                </template>

                <v-avatar :color="avatarColor(uidOf(c))" :size="36">
                  <img v-if="c.user?.avatar" :alt="c.user.nickname" :src="c.user.avatar">
                  <span v-else>{{ (c.user?.nickname ?? '?').slice(0, 1) }}</span>
                </v-avatar>

                <v-list-item-title>{{ c.remark || c.user?.nickname }}</v-list-item-title>
              </v-list-item>

              <div v-if="filteredFriends.length === 0" class="pa-6 text-center text-medium-emphasis">
                通讯录里还没有好友
              </div>
            </v-list>
          </div>

          <!-- 右：已选成员 -->
          <div class="wx-pick-right">
            <v-list bg-color="surface">
              <v-list-item v-for="id in gMembers" :key="id">
                <v-avatar :color="avatarColor(id)" :size="36">
                  <img v-if="avatarOf(id)" :alt="nickOf(id)" :src="avatarOf(id)">
                  <span v-else>{{ nickOf(id).slice(0, 1) }}</span>
                </v-avatar>

                <v-list-item-title>{{ nickOf(id) }}</v-list-item-title>

                <template #append>
                  <v-btn icon="mdi-close" size="x-small" variant="text" @click="toggleMember(id)" />
                </template>
              </v-list-item>

              <div v-if="gMembers.length === 0" class="pa-6 text-center text-medium-emphasis">
                从左侧勾选要拉进群的人
              </div>
            </v-list>
          </div>
        </div>

        <!-- 底部：全选 + 完成 -->
        <div class="d-flex align-center justify-space-between px-4 py-2">
          <v-btn variant="text" @click="toggleSelectAll">
            {{ allSelected ? '取消全选' : '全选' }}
          </v-btn>

          <span class="text-body-medium text-medium-emphasis">已选 {{ gMembers.length }} 人</span>

          <v-btn color="primary" :disabled="gMembers.length === 0" variant="text" @click="submitGroup">
            完成
          </v-btn>
        </div>
      </v-card>
    </v-dialog>

    <!-- 群名称 -->
    <v-dialog v-model="nameDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">群名称</v-card-title>

        <v-card-text>
          <v-text-field v-model="gName" autofocus placeholder="群名称" variant="plain" />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="nameDialog = false">取消</v-btn>
          <v-btn color="primary" @click="submitGroup">确定</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

  </v-sheet>
</template>

<script setup lang="ts">
  import type { ResultItem } from '@/components/SearchResultList.vue'
  import type { Conversation } from '@/types'
  import { computed, onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import SearchResultList from '@/components/SearchResultList.vue'
  import SessionList from '@/components/SessionList.vue'
  import { avatarColor } from '@/lib/utils'
  import { useContactsStore } from '@/stores/contacts'
  import { useConversationsStore } from '@/stores/conversations'

  const router = useRouter()
  const store = useConversationsStore()
  const contacts = useContactsStore()

  const search = ref(false)
  const kw = ref('')
  const groupSheet = ref(false)
  const gName = ref('')
  const gMembers = ref<string[]>([])
  const gQuery = ref('')
  const nameDialog = ref(false)
  const foldedDialog = ref(false)

  /** 被折叠的群聊（集中在"折叠群聊"里管理，从主列表隐藏）。 */
  const foldedGroups = computed(() => store.sessions.filter(s => s.kind === 'group' && s.folded))
  /** 主列表只显示未折叠的会话（被折叠的群聊收进上方入口）。 */
  const visibleSessions = computed(() => store.sessions.filter(s => !s.folded))

  function onOpen (id: string) {
    router.push({ name: 'chat-detail', params: { id } })
  }

  /** 从"折叠群聊"恢复某群（取消折叠 → 回到主列表）。 */
  async function restoreFolded (s: Conversation) {
    await store.toggleFold(s.id, false)
  }

  /** 从"折叠群聊"直接打开某群（保持折叠状态，仅进入聊天）。 */
  function openFolded (s: Conversation) {
    foldedDialog.value = false
    router.push({ name: 'chat-detail', params: { id: s.id } })
  }

  /** 本地搜索：过滤已有会话 + 通讯录联系人（无请求、不污染 store） */
  const searchResults = computed<ResultItem[]>(() => {
    const k = kw.value.trim().toLowerCase()
    if (!k) return []
    const convs = store.sessions
      .filter(s => s.title.toLowerCase().includes(k))
      .map(s => ({ id: s.id, title: s.title, subtitle: '会话', unread: s.unread > 0 }))
    const ctts = contacts.items
      .filter(c =>
        (c.user?.nickname ?? '').toLowerCase().includes(k)
        || (c.remark ?? '').toLowerCase().includes(k)
        || (c.user?.phone ?? '').includes(k))
      .map(c => ({
        id: c.user?.id ?? c.id,
        title: c.remark || c.user?.nickname || '',
        subtitle: c.user?.phone ? `联系人 · ${c.user.phone}` : '联系人',
      }))
    return [...convs, ...ctts]
  })

  function closeSearch () {
    search.value = false
    kw.value = ''
  }

  function onSearchPick (it: ResultItem) {
    closeSearch()
    if (store.sessions.some(s => s.id === it.id)) {
      router.push({ name: 'chat-detail', params: { id: it.id } })
    } else {
      router.push({ name: 'contact-profile', params: { id: it.id } })
    }
  }

  const filteredFriends = computed(() => {
    const k = gQuery.value.trim().toLowerCase()
    if (!k) return contacts.items
    return contacts.items.filter(c =>
      (c.user?.nickname ?? '').toLowerCase().includes(k)
      || (c.remark ?? '').toLowerCase().includes(k)
      || (c.user?.phone ?? '').includes(k))
  })

  function uidOf (c: { user?: { id: string } | null, id: string }) {
    return c.user?.id ?? c.id
  }
  function nickOf (id: string) {
    return contacts.items.find(c => (c.user?.id ?? c.id) === id)?.remark
      || contacts.items.find(c => (c.user?.id ?? c.id) === id)?.user?.nickname
      || '好友'
  }
  function avatarOf (id: string) {
    return contacts.items.find(c => (c.user?.id ?? c.id) === id)?.user?.avatar || ''
  }

  const allSelected = computed(() =>
    filteredFriends.value.length > 0
    && filteredFriends.value.every(c => gMembers.value.includes(c.user?.id ?? c.id)))

  function toggleMember (id: string) {
    const i = gMembers.value.indexOf(id)
    if (i === -1) {
      gMembers.value.push(id)
    } else {
      gMembers.value.splice(i, 1)
    }
  }

  function toggleSelectAll () {
    gMembers.value = allSelected.value ? [] : filteredFriends.value.map(c => c.user?.id ?? c.id)
  }

  onMounted(async () => {
    await Promise.allSettled([store.loadSessions(), contacts.load(), contacts.loadGroups()])
  })

  async function createGroup () {
    gQuery.value = ''
    gMembers.value = []
    gName.value = ''
    groupSheet.value = true
  }

  function openNameDialog () {
    nameDialog.value = true
  }

  async function submitGroup () {
    if (gMembers.value.length === 0) return
    try {
      const g = await contacts.createGroup(gName.value || '群聊', gMembers.value)
      groupSheet.value = false
      nameDialog.value = false
      await store.loadSessions()
      router.push({ name: 'group', params: { id: g.id } })
    } catch {
      // 创建失败：关闭命名框保留选人面板，用户可重试
      nameDialog.value = false
    }
  }
</script>

<style scoped>
.wx-folded-entry {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

.wx-search-body {
  max-height: 60vh;
  overflow-y: auto;
}

.wx-group-create {
  height: 100%;
  display: flex;
  flex-direction: column;
  background: #ededed;
}

/* 工具栏固定高度，不被 flex 拉伸（否则会撑成整屏空白） */
.wx-group-toolbar {
  flex: 0 0 auto;
  height: 48px;
}

/* 搜索框所在行也不拉伸 */
.wx-group-create > .v-input {
  flex: 0 0 auto;
}

/* 选人区：左通讯录 / 右已选 */
.wx-pick-body {
  display: flex;
  flex: 1 1 auto;
  min-height: 0;
  gap: 10px;
  padding: 0 8px 8px;
  overflow: hidden;
}

.wx-pick-body > .v-list {
  height: 100%;
  overflow-y: auto;
  border-radius: 10px;
}

.wx-pick-left {
  flex: 1 1 58%;
  min-width: 0;
  overflow-y: auto;
  border-radius: 10px;
  background: rgb(var(--v-theme-surface));
}

.wx-pick-right {
  flex: 1 1 42%;
  min-width: 0;
  overflow-y: auto;
  border-radius: 10px;
  background: rgb(var(--v-theme-surface));
}

.wx-pick-row {
  cursor: pointer;
}

/* 手机/窄屏：左右两列挤在一起（右列已选区只有 ~150px，名字被砍成一坨）。
   改为上下堆叠、各自可滚动，各占一半高度。iPad 横屏/电脑（≥561px）保持并排。 */
@media (max-width: 560px) {
  .wx-pick-body {
    flex-direction: column;
  }

  .wx-pick-left,
  .wx-pick-right {
    flex: 1 1 50%;
    min-height: 140px;
  }

  .wx-pick-left {
    border-bottom-right-radius: 0;
    border-bottom-left-radius: 0;
  }

  .wx-pick-right {
    border-top-left-radius: 0;
    border-top-right-radius: 0;
  }
}
</style>
