<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">{{ t('settings.profile') }}</v-toolbar-title>
    </v-app-bar>

    <div class="flex-grow-1 overflow-y-auto wx-scroll wx-profile-scroll pa-4">
      <!-- 身份区：头像（真实照片，经 fileUrl 解析） + 昵称 + 手机号 -->
      <div class="wx-profile-hero">
        <div class="wx-profile-avatar-ring">
          <user-avatar
            :alt="user?.nickname ?? ''"
            :circle="true"
            :nickname="user?.nickname"
            :size="96"
            :src="user?.avatar"
          />
        </div>
        <div class="text-h5 font-weight-medium mt-3">{{ user?.nickname ?? t('common.loading') }}</div>
        <div v-if="user?.phone" class="text-body-medium text-medium-emphasis mt-1">{{ user.phone }}</div>
      </div>

      <!-- 详细资料卡（微信风格：白底圆角、细线分隔、图标+字段） -->
      <v-card class="wx-profile-card" flat>
        <v-card-title class="text-body-small text-medium-emphasis wx-profile-card-title">{{ t('profile.details') }}</v-card-title>

        <!-- 备注（仅好友/自己） -->
        <div class="wx-profile-row">
          <span class="wx-profile-field-label">{{ t('profile.remark') }}</span>
          <div class="wx-profile-field-value">
            <v-text-field
              v-if="!isOwn"
              v-model="remarkDraft"
              density="compact"
              hide-details
              :placeholder="t('profile.remark.ph')"
              style="max-width: 140px"
              variant="plain"
            />
            <span v-else class="text-body-medium text-medium-emphasis">{{ auth.user?.nickname || '—' }}</span>
          </div>
        </div>

        <!-- 大面号 + 复制 -->
        <div class="wx-profile-row">
          <span class="wx-profile-field-label">{{ t('me.wxid') }}</span>
          <div class="wx-profile-field-value">
            <template v-if="user?.wxId">
              <span class="wx-wxid">{{ user.wxId }}</span>
              <v-icon
                :color="copiedWxId ? 'success' : 'medium-emphasis'"
                :icon="copiedWxId ? 'mdi-check-circle' : 'mdi-content-copy'"
                class="mx-1 wx-wxid-copy"
                size="18"
                @click="copyWxId"
              />
            </template>
            <span v-else class="text-body-medium text-medium-emphasis">—</span>
          </div>
        </div>

        <!-- 地区 -->
        <div class="wx-profile-row">
          <span class="wx-profile-field-label">{{ t('me.region') }}</span>
          <span class="wx-profile-field-value text-body-medium text-medium-emphasis">{{ user?.region || '—' }}</span>
        </div>

        <!-- 签名 -->
        <div class="wx-profile-row">
          <span class="wx-profile-field-label">{{ t('profile.signature') }}</span>
          <span class="wx-profile-field-value text-body-medium text-medium-emphasis wx-sig">{{ user?.signature || '—' }}</span>
        </div>

        <!-- 保存备注：非自己时始终有「保存备注」确认按钮，改动后额外给「取消」还原 -->
        <div v-if="!isOwn" class="wx-profile-save">
          <v-btn v-if="dirty" color="medium-emphasis" size="small" variant="text" @click="remarkDraft = remarkOrigin">{{ t('common.cancel') }}</v-btn>
          <v-btn
            color="primary"
            size="small"
            :disabled="!dirty"
            @click="saveRemark"
          >{{ t('profile.remark.save') }}</v-btn>
        </div>
      </v-card>

      <!-- 操作按钮区 -->
      <div class="wx-profile-actions">
        <v-btn
          v-if="!isOwn && isFriend"
          class="wx-act-btn wx-act-primary"
          color="primary"
          icon="mdi-chat-outline"
          variant="tonal"
          @click="message"
        >{{ t('profile.message') }}</v-btn>

        <v-btn
          v-if="!isOwn && !isFriend"
          class="wx-act-btn wx-act-primary"
          color="primary"
          icon="mdi-account-plus-outline"
          :loading="adding"
          variant="tonal"
          @click="addFriend"
        >{{ t('profile.addFriend') }}</v-btn>

        <template v-if="!isOwn && isFriend">
          <v-btn
            :class="['wx-act-btn', blocked ? 'wx-act-danger wx-act-active' : 'wx-act-danger']"
            color="error"
            :icon="blocked ? 'mdi-account-check-outline' : 'mdi-account-off-outline'"
            :variant="blocked ? 'flat' : 'tonal'"
            @click="toggleBlock"
          >{{ blocked ? t('contacts.unblock') : t('contacts.block') }}</v-btn>

          <v-btn
            class="wx-act-btn wx-act-danger"
            color="error"
            icon="mdi-delete-outline"
            variant="tonal"
            @click="removeFriend"
          >{{ t('common.delete') }}</v-btn>
        </template>
      </div>
    </div>

    <v-snackbar v-model="snack" :timeout="2000">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import type { User } from '@/types'
  import { computed, onMounted, ref } from 'vue'
  import { useRoute, useRouter } from 'vue-router'
  import { api } from '@/lib/api'
  import { isPageMode } from '@/lib/device'
  import { t } from '@/lib/i18n'
  import { confirm } from '@/lib/confirm'
  import UserAvatar from '@/components/UserAvatar.vue'
  import { useAuthStore } from '@/stores/auth'
  import { useContactsStore } from '@/stores/contacts'
  import { useConversationsStore } from '@/stores/conversations'

  const route = useRoute()
  const router = useRouter()
  const auth = useAuthStore()
  const contacts = useContactsStore()

  const isMobile = isPageMode
  const contactId = computed(() => String(route.params.id ?? ''))
  const isOwn = computed(() => contactId.value === auth.user?.id)

  const user = ref<User | null>(null)
  const remarkDraft = ref('')
  const remarkOrigin = ref('')
  /** 程序赋值（load 填充）不触发 watch；改回原值按钮自动消失 */
  const dirty = computed(() => remarkDraft.value !== remarkOrigin.value)
  const adding = ref(false)

  const isFriend = computed(() =>
    !isOwn.value && contacts.items.some(c => c.user?.id === contactId.value),
  )

  /** 该联系人是否已被「我」拉黑。 */
  const blocked = computed(() =>
    contacts.items.find(c => c.user?.id === contactId.value)?.blocked ?? false,
  )

  async function addFriend () {
    if (!user.value) return
    adding.value = true
    try {
      const res = await api.post('/contacts', { userId: user.value.id, message: t('profile.add.msg') })
      snackMsg.value = res.data.status === 'accepted' ? t('scan.added') : t('scan.pending')
      snack.value = true
      await contacts.load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('scan.fail')
      snack.value = true
    } finally {
      adding.value = false
    }
  }

  const snack = ref(false)
  const snackMsg = ref('')

  /* ---------- 大面号复制（clipboard + execCommand 兜底，失败降级提示） ---------- */
  const copiedWxId = ref(false)
  let copyTimer: ReturnType<typeof setTimeout> | null = null

  async function copyWxId () {
    const wxid = user.value?.wxId
    if (!wxid) return
    let ok = false
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(wxid)
        ok = true
      }
    } catch { /* 降级 */ }
    if (!ok) {
      try {
        const ta = document.createElement('textarea')
        ta.value = wxid
        ta.style.position = 'fixed'
        ta.style.opacity = '0'
        document.body.appendChild(ta)
        ta.select()
        ok = document.execCommand('copy')
        document.body.removeChild(ta)
      } catch { ok = false }
    }
    if (ok) {
      copiedWxId.value = true
      if (copyTimer) clearTimeout(copyTimer)
      copyTimer = setTimeout(() => { copiedWxId.value = false }, 1500)
    } else {
      snackMsg.value = t('profile.copy.fail')
      snack.value = true
    }
  }

  async function load () {
    if (isOwn.value) {
      user.value = auth.user
      return
    }
    // 同步 store.items（若尚未加载则先加载一次，供 blocked 等状态使用）
    if (contacts.items.length === 0) {
      await contacts.load().catch(() => {})
    }
    const c = contacts.items.find((x: any) => x.user?.id === contactId.value)
    if (c?.user) {
      user.value = c.user
      remarkDraft.value = c.remark ?? ''
      remarkOrigin.value = c.remark ?? ''
      return
    }
    // 不是好友（如从群成员点进来）：直接查公开资料
    try {
      const res = await api.get(`/users/${contactId.value}`)
      user.value = res.data
    } catch {
      user.value = null
    }
  }

  async function saveRemark () {
    const c = contacts.items.find(x => x.user?.id === contactId.value)
    if (!c) return
    try {
      await contacts.setRemark(c.id, remarkDraft.value)
      remarkOrigin.value = remarkDraft.value
    } catch {
      // 保存失败保持 dirty，用户可重试
    }
  }
  async function message () {
    // 找该联系人的单聊会话；没有则由服务端创建/返回
    const conv = useConversationsStore()
    const s = conv.sessions.find(x => x.kind === 'private' && x.memberId === contactId.value)
    if (s) {
      router.push({ name: 'chat-detail', params: { id: s.id } })
      return
    }
    try {
      const res = await api.post('/conversations', { memberId: contactId.value })
      router.push({ name: 'chat-detail', params: { id: res.data.id } })
    } catch {
      // 兜底：进入后由会话页自行处理
      router.push({ name: 'chat' })
    }
  }

  /** 拉黑 / 取消拉黑（toggle），危险操作先弹确认。 */
  async function toggleBlock () {
    const c = contacts.items.find(x => x.user?.id === contactId.value)
    if (!c) return
    const next = !c.blocked
    const nick = c.user?.nickname ?? t('contacts.peer')
    if (next) {
      const ok = await confirm({
        title: t('contacts.block.confirm'),
        text: t('contacts.block.confirm.text', { name: nick }),
        confirmText: t('contacts.block'),
      })
      if (!ok) return
    } else {
      const ok = await confirm({
        title: t('contacts.unblock.confirm'),
        text: t('contacts.unblock.confirm.text', { name: nick }),
        confirmText: t('contacts.unblock'),
        danger: false,
      })
      if (!ok) return
    }
    try {
      await contacts.toggleBlock(c.id, next)
      snackMsg.value = next ? t('contacts.blocked.ok', { name: nick }) : t('contacts.unblocked.ok', { name: nick })
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('contacts.op.fail')
      snack.value = true
    }
  }

  /** 删除好友关系（双向），危险操作先弹确认。 */
  async function removeFriend () {
    const c = contacts.items.find(x => x.user?.id === contactId.value)
    if (!c) return
    const ok = await confirm({
      title: t('contacts.deleteFriend.confirm'),
      text: t('contacts.deleteFriend.confirm.text', { name: c.user?.nickname ?? t('contacts.peer') }),
      confirmText: t('common.delete'),
    })
    if (!ok) return
    try {
      await contacts.removeFriend(c.id)
      snackMsg.value = t('profile.friendRemoved')
      snack.value = true
      router.push({ name: 'contacts' })
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('contacts.deleteFriend.fail')
      snack.value = true
    }
  }

  function goBack () {
    // 从群成员/群聊天进来的资料卡：返回原上下文（而非通讯录）。
    // 关键点：从「群聊天详情」点头像进来时 query 带 convId —— 这时返回必须回那个群会话
    // （chat-detail），而不是群设置页（group）；只有从群设置里点进成员资料才回群设置。
    const from = String(route.query.from ?? '')
    const gid = String(route.query.gid ?? '')
    const convId = String(route.query.convId ?? '')
    if (from === 'group' && gid) {
      if (convId) {
        router.push({ name: 'chat-detail', params: { id: convId } })
        return
      }
      router.push({ name: 'group', params: { id: gid } })
      return
    }
    router.push({ name: 'contacts' })
  }

  onMounted(load)
</script>

<style scoped>
.wx-chat-bar {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

/* ---- 滚动容器：居中 + 最大宽度，避免超宽屏下内容被拉得太开 ---- */
.wx-profile-scroll {
  display: flex;
  flex-direction: column;
  align-items: center;
  max-width: 560px;
  width: 100%;
  margin: 0 auto;
}

/* ---- 身份区（头像 + 昵称 + 手机号）---- */
.wx-profile-hero {
  text-align: center;
  padding: 24px 0 8px;
  width: 100%;
}

.wx-profile-avatar-ring {
  display: inline-flex;
  padding: 4px;
  border-radius: 50%;
  background: linear-gradient(135deg, rgba(15, 191, 159, 0.18), rgba(255, 255, 255, 0.4));
  box-shadow: 0 4px 18px rgba(0, 0, 0, 0.12);
}

/* ---- 详细资料卡（微信白底风格）---- */
.wx-profile-card {
  width: 100%;
  border-radius: 14px;
  border: 1px solid rgb(var(--v-theme-wechat-divider, #e5e5e5));
  overflow: hidden;
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.05);
}

.wx-profile-card-title {
  padding: 10px 18px 8px;
  letter-spacing: 0.02em;
}

.wx-profile-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 18px;
  border-top: 1px solid rgb(var(--v-theme-wechat-divider, #e5e5e5));
}

.wx-profile-row:first-of-type {
  border-top: none;
}

.wx-profile-field-label {
  font-size: 14px;
  color: rgb(var(--v-theme-medium-emphasis));
  flex-shrink: 0;
  min-width: 44px;
}

.wx-profile-field-value {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 4px;
  font-size: 14px;
}

.wx-sig {
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 220px;
}

.wx-profile-save {
  padding: 8px 18px 14px;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  border-top: 1px solid rgb(var(--v-theme-wechat-divider, #e5e5e5));
}

/* ---- 大面号 ---- */
.wx-wxid {
  font-family: 'Consolas', 'SF Mono', monospace;
  font-size: 13.5px;
  user-select: text;
  -webkit-user-select: text;
}

.wx-wxid-copy {
  cursor: pointer;
  transition: transform 0.15s ease, color 0.15s ease;
}

.wx-wxid-copy:hover {
  transform: scale(1.12);
}

/* ---- 操作按钮区 ---- */
.wx-profile-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 12px;
  padding: 22px 0 12px;
  width: 100%;
}

.wx-act-btn {
  min-width: 96px;
  height: 44px;
  border-radius: 10px;
  font-size: 14px;
  font-weight: 500;
  justify-content: center;
}

.wx-act-danger.wx-act-active {
  background: rgb(var(--v-theme-error));
  color: rgb(var(--v-theme-on-error));
}

/* 窄屏：按钮铺满，更易点 */
@media (max-width: 480px) {
  .wx-profile-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .wx-act-btn {
    width: 100%;
  }

  .wx-profile-row {
    padding: 11px 14px;
  }

  .wx-sig {
    max-width: 160px;
  }
}
</style>
