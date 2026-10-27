<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">群聊设置</v-toolbar-title>
    </v-app-bar>

    <div class="flex-grow-1 overflow-y-auto wx-scroll">
      <!-- 成员宫格（人数过多自动折叠，点「查看全部」展开；点头像查资料卡） -->
      <div class="wx-member-sec pa-3">
        <div class="wx-member-sec-head d-flex align-center justify-space-between">
          <span class="text-body-small text-medium-emphasis">成员 <span class="text-primary font-weight-medium">{{ group?.members?.length ?? 0 }}</span> 人</span>
          <v-btn v-if="membersTruncated" color="primary" density="compact" size="x-small" variant="text" @click="showAllMembers = true">
            <v-icon icon="mdi-eye" size="16" class="mr-1" /> 查看全部 {{ group?.members?.length }} 人
          </v-btn>
          <v-btn v-else-if="membersTotal > MAX_COLLAPSED" color="medium-emphasis" density="compact" size="x-small" variant="text" @click="showAllMembers = false">收起</v-btn>
        </div>

        <div class="wx-member-grid">
          <div
            v-for="m in visibleMembers"
            :key="m.userId"
            class="wx-member-cell"
            @click="onMemberTap(m)"
          >
            <div class="wx-member-avatar" :class="{ 'is-me': m.userId === meId, 'is-removable': removeMode && m.userId !== group?.ownerId }">
              <user-avatar :nickname="m.nickname" :src="m.avatar" :size="52" circle />
              <span v-if="removeMode && m.userId !== group?.ownerId" class="wx-remove-badge">−</span>
              <span v-else-if="m.role === 'owner'" class="wx-owner-badge">群主</span>
              <span v-else-if="m.role === 'admin'" class="wx-admin-badge">管理员</span>
              <span v-else-if="m.title" class="wx-title-badge">{{ m.title }}</span>
            </div>

            <div class="wx-member-name text-truncate">{{ m.nickname }}</div>
          </div>

          <template v-if="canManage">
            <div class="wx-member-cell" @click="inviteSheet = true">
              <div class="wx-member-avatar wx-member-action"><v-icon icon="mdi-plus" size="28" /></div>
              <div class="wx-member-name">邀请</div>
            </div>

            <div class="wx-member-cell" @click="removeMode = !removeMode">
              <div class="wx-member-avatar wx-member-action" :class="{ 'is-on': removeMode }"><v-icon icon="mdi-minus" size="28" /></div>
              <div class="wx-member-name">移除</div>
            </div>
          </template>
        </div>
      </div>

      <!-- 群资料 -->
      <v-list bg-color="surface" class="wx-group-list" lines="two">
        <v-list-item @click="canManage ? openAvatarDialog() : null">
          <template #prepend>
            <user-avatar :nickname="group?.name ?? ''" :src="group?.avatar" :size="44" />
          </template>
          <v-list-item-title>群头像</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ group?.avatar ? '已设置' : '未设置' }}</span>
            <v-icon v-if="canManage" icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="canManage ? openEdit('name') : null">
          <v-list-item-title>群名称</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ group?.name }}</span>
            <v-icon v-if="canManage" icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="openEdit('notice')">
          <v-list-item-title>群公告</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ group?.notice || '未设置' }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="openEdit('groupNick')">
          <v-list-item-title>我在本群的昵称</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ myGroupNick || '默认大面昵称' }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>
      </v-list>

      <!-- 消息设置 -->
      <v-list bg-color="surface" class="wx-group-list mt-3">
        <v-list-item>
          <template #prepend><v-icon icon="mdi-bell-off-outline" /></template>
          <v-list-item-title>消息免打扰</v-list-item-title>

          <template #append>
            <v-switch
              class="wx-row-switch"
              color="primary"
              density="compact"
              hide-details
              :model-value="!!conv?.muted"
              @update:model-value="v => setFlag('muted', !!v)"
            />
          </template>
        </v-list-item>

        <v-list-item>
          <template #prepend><v-icon icon="mdi-pin-outline" /></template>
          <v-list-item-title>置顶聊天</v-list-item-title>

          <template #append>
            <v-switch
              class="wx-row-switch"
              color="primary"
              density="compact"
              hide-details
              :model-value="!!conv?.pinned"
              @update:model-value="v => setFlag('pinned', !!v)"
            />
          </template>
        </v-list-item>

        <v-list-item @click="findHistory">
          <template #prepend><v-icon icon="mdi-magnify" /></template>
          <v-list-item-title>查找聊天记录</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item v-if="canManage" @click="inviteSheet = true">
          <template #prepend><v-icon icon="mdi-account-plus-outline" /></template>
          <v-list-item-title>邀请成员</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item v-if="canManage" @click="openJoinSheet">
          <template #prepend><v-icon icon="mdi-account-check" /></template>
          <v-list-item-title>加群申请</v-list-item-title>
          <template #append>
            <v-badge v-if="group?.joinPending" color="wechat-unread" :content="group!.joinPending" />
            <v-icon v-else icon="mdi-chevron-right" />
          </template>
        </v-list-item>

        <v-list-item v-if="canManage">
          <template #prepend><v-icon icon="mdi-key" /></template>
          <v-list-item-title>入群方式</v-list-item-title>
          <v-list-item-subtitle>{{ group?.joinPolicy === 'open' ? '开放（知道群号直接进）' : '需群主审批' }}</v-list-item-subtitle>
          <template #append>
            <v-switch
              class="wx-row-switch"
              color="primary"
              density="compact"
              hide-details
              :model-value="group?.joinPolicy === 'open'"
              @update:model-value="v => setJoinPolicy(!!v ? 'open' : 'approve')"
            />
          </template>
        </v-list-item>
      </v-list>

      <!-- 危险操作 -->
      <v-list bg-color="surface" class="wx-group-list mt-3">
        <v-list-item v-if="isOwner" @click="transferSheet = true">
          <template #prepend><v-icon icon="mdi-crown-outline" /></template>
          <v-list-item-title>转让群主</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="confirmLeave = true">
          <template #prepend><v-icon color="error" icon="mdi-exit-run" /></template>
          <v-list-item-title class="text-error">退出群聊</v-list-item-title>
        </v-list-item>

        <v-list-item v-if="isOwner" @click="confirmDissolve = true">
          <template #prepend><v-icon color="error" icon="mdi-delete-outline" /></template>
          <v-list-item-title class="text-error">解散群聊</v-list-item-title>
        </v-list-item>
      </v-list>

      <div class="pa-4 d-flex align-center justify-center gap-2 text-body-small text-medium-emphasis">
        <span>群号：{{ groupId }}</span>
        <v-btn icon="mdi-content-copy" size="x-small" variant="text" title="复制群号" @click="copyGroupId" />
      </div>
    </div>

    <!-- 成员操作面板 -->
    <v-bottom-sheet v-model="memberSheet">
      <v-card class="pa-2">
        <div class="d-flex align-center pa-3">
          <div class="mr-3">
            <user-avatar :nickname="activeMember?.nickname ?? ''" :src="activeMember?.avatar" :size="44" circle />
          </div>

          <div>
            <div class="font-weight-medium">{{ activeMember?.nickname }}</div>

            <div class="text-body-small text-medium-emphasis">
              {{ activeMember?.role === 'owner' ? '群主' : activeMember?.role === 'admin' ? '管理员' : '群成员' }}
            </div>
          </div>
        </div>

        <v-list density="compact">
          <v-list-item prepend-icon="mdi-account-outline" title="查看资料" @click="viewProfile" />

          <v-list-item
            v-if="activeMember?.userId === meId"
            prepend-icon="mdi-card-account-details-outline"
            title="设置我在本群的昵称"
            @click="memberSheet = false; openEdit('groupNick')"
          />

          <template v-if="canManage && activeMember && activeMember.userId !== group?.ownerId">
            <v-list-item
              v-if="activeMember.userId !== meId && activeMember.role !== 'admin'"
              prepend-icon="mdi-shield-plus"
              title="设为群管理员"
              @click="setRole(activeMember, 'admin')"
            />

            <v-list-item
              v-if="activeMember.userId !== meId && activeMember.role === 'admin'"
              prepend-icon="mdi-shield-off"
              title="取消群管理员"
              @click="setRole(activeMember, 'member')"
            />

            <v-list-item
              v-if="activeMember.userId !== meId"
              :prepend-icon="activeMember.muted ? 'mdi-volume-high' : 'mdi-volume-off'"
              :title="activeMember.muted ? '解除禁言' : '禁言该成员'"
              @click="setMute(activeMember, !activeMember.muted)"
            />

            <v-list-item
              v-if="activeMember.userId !== meId"
              prepend-icon="mdi-tag-multiple-outline"
              :title="`设置头衔（当前：${activeMember.title || '无'}）`"
              @click="openTitleEditor(activeMember)"
            />

            <v-list-item
              v-if="activeMember.userId !== meId"
              class="text-error"
              prepend-icon="mdi-account-remove"
              title="移出群聊"
              @click="askRemove(activeMember)"
            />
          </template>
        </v-list>
      </v-card>
    </v-bottom-sheet>

    <!-- 编辑群名称 / 公告 / 本群昵称 -->
    <v-dialog v-model="editDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">{{ editTitle }}</v-card-title>

        <v-card-text>
          <v-text-field
            v-if="editKind === 'name'"
            v-model="editValue"
            counter
            label="群名称"
            maxlength="32"
            variant="outlined"
          />

          <v-textarea
            v-else-if="editKind === 'notice'"
            v-model="editValue"
            counter
            label="群公告"
            maxlength="200"
            rows="3"
            variant="outlined"
          />

          <v-text-field
            v-else
            v-model="editValue"
            counter
            label="本群昵称"
            maxlength="24"
            variant="outlined"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="editDialog = false">取消</v-btn>
          <v-btn color="primary" :disabled="!editValue.trim()" @click="saveEdit">保存</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 群头像：群主/管理员选图上传（/files/upload kind=avatar 拿 fileId 再 PUT 群） -->
    <v-dialog v-model="avatarDialog" max-width="320">
      <v-card>
        <v-card-title class="text-body-large">群头像</v-card-title>

        <v-card-text class="d-flex flex-column align-center">
          <user-avatar :nickname="group?.name ?? ''" :src="avatarPreview || group?.avatar" :size="88" />
          <v-btn
            class="mt-4"
            color="primary"
            variant="tonal"
            size="small"
            :loading="avatarBusy"
            @click="pickAvatarFile"
          >
            <v-icon :icon="avatarUploaded ? 'mdi-check' : 'mdi-camera-plus-outline'" class="mr-1" />
            {{ avatarUploaded ? '更换图片' : '选择图片' }}
          </v-btn>
          <div class="text-caption text-medium-emphasis mt-2 text-center">支持 JPG/PNG，≤5MB</div>
        </v-card-text>

        <v-card-actions>
          <v-btn variant="text" @click="closeAvatarDialog">取消</v-btn>
          <v-btn
            color="primary"
            :disabled="!avatarUploaded || avatarBusy"
            @click="saveAvatar"
          >保存</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
    <!-- 隐藏文件选择器：群头像专用 -->
    <input
      ref="avatarInput"
      type="file"
      accept="image/*"
      class="d-none"
      @change="onAvatarFile"
    >

    <!-- 邀请成员 -->
    <v-bottom-sheet v-model="inviteSheet" max-width="440" scrollable>
      <v-card class="pa-2">
        <div class="text-title-small pa-2">邀请好友加入</div>

        <v-list density="compact">
          <v-list-item v-for="c in inviteCandidates" :key="c.id" @click="invite(c)">
            <template #prepend>
              <user-avatar :nickname="c.user?.nickname" :src="c.user?.avatar" :size="34" />
            </template>

            <v-list-item-title>{{ c.remark || c.user?.nickname }}</v-list-item-title>
            <template #append><v-icon color="primary" icon="mdi-account-plus" /></template>
          </v-list-item>
        </v-list>

        <div v-if="inviteCandidates.length === 0" class="pa-4 text-body-small text-medium-emphasis">没有可邀请的好友</div>
      </v-card>
    </v-bottom-sheet>

    <!-- 转让群主 -->
    <v-bottom-sheet v-model="transferSheet" max-width="440" scrollable>
      <v-card class="pa-2">
        <div class="text-title-small pa-2">选择新群主</div>

        <v-list density="compact">
          <v-list-item
            v-for="m in (group?.members ?? [])"
            :key="m.userId"
            :disabled="m.userId === group?.ownerId"
            @click="transfer(m.userId)"
          >
            <template #prepend>
              <user-avatar :nickname="m.nickname" :src="m.avatar" :size="34" />
            </template>

            <v-list-item-title>{{ m.nickname }}</v-list-item-title>
            <template #append><v-icon color="primary" icon="mdi-crown" /></template>
          </v-list-item>
        </v-list>
      </v-card>
    </v-bottom-sheet>

    <!-- 加群申请审批（群主/管理员；open 群无待审批、approve 群等群主点头） -->
    <v-bottom-sheet v-model="joinSheet" max-width="440" scrollable>
      <v-card class="pa-2">
        <div class="text-title-small pa-2">加群申请</div>

        <v-list v-if="!joinBusy && joinList.length" density="compact">
          <v-list-item v-for="r in joinList" :key="r.userId">
            <template #prepend>
              <v-avatar :color="avatarColor(r.userId)" :size="34">
                <span>{{ (r.nickname || '?').slice(0, 1) }}</span>
              </v-avatar>
            </template>
            <template #title>
              <div>{{ r.nickname }}</div>
              <div v-if="r.note" class="text-body-small text-medium-emphasis">{{ r.note }}</div>
            </template>
            <template #append>
              <v-btn color="primary" size="x-small" :disabled="joinBusy" @click="acceptJoin(r.userId)">通过</v-btn>
              <v-btn size="x-small" variant="text" :disabled="joinBusy" @click="rejectJoin(r.userId)">拒绝</v-btn>
            </template>
          </v-list-item>
        </v-list>

        <v-list v-else-if="!joinBusy" class="justify-center" disabled>
          <v-list-item-title class="text-medium-emphasis">暂无待审批的加群申请</v-list-item-title>
        </v-list>

        <div class="d-flex justify-end ga-2 pa-2">
          <v-btn variant="text" @click="joinSheet = false">关闭</v-btn>
        </div>
      </v-card>
    </v-bottom-sheet>

    <!-- 退出群聊确认 -->
    <v-dialog v-model="confirmLeave" max-width="320">
      <v-card class="pa-4">
        <div class="text-title-large">退出群聊？</div>

        <div class="text-body-medium text-medium-emphasis my-2">
          退出后将不再接收群「{{ group?.name }}」的消息，聊天记录将被删除。
        </div>

        <div class="d-flex justify-end gap-2">
          <v-btn variant="text" @click="confirmLeave = false">取消</v-btn>
          <v-btn color="error" @click="leave">退出</v-btn>
        </div>
      </v-card>
    </v-dialog>

    <!-- 移出成员确认 -->
    <v-dialog v-model="confirmRemove" max-width="320">
      <v-card class="pa-4">
        <div class="text-title-large">移出群聊？</div>

        <div class="text-body-medium text-medium-emphasis my-2">
          确定将「{{ pendingRemove?.nickname }}」移出群聊？对方将不再接收本群消息。
        </div>

        <div class="d-flex justify-end gap-2">
          <v-btn variant="text" @click="confirmRemove = false">取消</v-btn>
          <v-btn color="error" @click="doRemoveMember">移出</v-btn>
        </div>
      </v-card>
    </v-dialog>

    <!-- 解散确认 -->
    <v-dialog v-model="confirmDissolve" max-width="320">
      <v-card class="pa-4">
        <div class="text-title-large">解散群聊？</div>
        <div class="text-body-medium text-medium-emphasis my-2">解散后群「{{ group?.name }}」将被删除，无法恢复。</div>
        <div class="d-flex justify-end gap-2">
          <v-btn variant="text" @click="confirmDissolve = false">取消</v-btn>
          <v-btn color="error" @click="dissolve">解散</v-btn>
        </div>
      </v-card>
    </v-dialog>

    <!-- 设置头衔（群主/管理员为任意成员单独设置，空 = 清除） -->
    <v-dialog v-model="titleDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">设置头衔</v-card-title>

        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-2">
            为「{{ titleTarget?.nickname }}」设置群头衔，所有群成员可见（留空表示清除）
          </div>
          <v-text-field
            v-model="titleValue"
            counter
            label="头衔（如：群管 / 元老 / 管理员）"
            maxlength="12"
            placeholder="留空 = 清除头衔"
            variant="outlined"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="titleDialog = false">取消</v-btn>
          <v-btn color="primary" @click="saveTitle">保存</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar v-model="snack" :timeout="2000">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import type { Contact, Group, GroupMember } from '@/types'
  import { computed, onMounted, ref, watch } from 'vue'
  import { useRoute, useRouter } from 'vue-router'
  import { api } from '@/lib/api'
  import { fileUrl } from '@/lib/files'
  import { avatarColor } from '@/lib/utils'
  import { isPageMode } from '@/lib/device'
  import { useAuthStore } from '@/stores/auth'
  import { useContactsStore } from '@/stores/contacts'
  import { useConversationsStore } from '@/stores/conversations'
  import UserAvatar from '@/components/UserAvatar.vue'

  const route = useRoute()
  const router = useRouter()
  const auth = useAuthStore()
  const contacts = useContactsStore()
  const convStore = useConversationsStore()

  const isMobile = isPageMode
  const meId = computed(() => auth.user?.id ?? 'me')
  const groupId = computed(() => String(route.params.id ?? ''))

  const group = ref<Group | null>(null)
  const snack = ref(false)
  const snackMsg = ref('')
  const inviteSheet = ref(false)
  const transferSheet = ref(false)
  const confirmDissolve = ref(false)
  const removeMode = ref(false)
  const confirmLeave = ref(false)
  const confirmRemove = ref(false)
  const pendingRemove = ref<GroupMember | null>(null)

  const isOwner = computed(() => myRole.value === 'owner')
  const myRole = computed(() => group.value?.members.find(m => m.userId === meId.value)?.role ?? 'member')
  const canManage = computed(() => myRole.value === 'owner' || myRole.value === 'admin')

  /** 当前会话（置顶/免打扰开关的数据源，与聊天列表同源） */
  const conv = computed(() => convStore.sessions.find(s => s.kind === 'group' && s.memberId === groupId.value))

  const myGroupNick = computed(() =>
    group.value?.members.find(m => m.userId === meId.value)?.groupNick ?? '')

  /** 可邀请：联系人里且还不是群成员的 */
  const inviteCandidates = computed(() => {
    const memberIds = new Set((group.value?.members ?? []).map(m => m.userId))
    return contacts.items.filter(c => c.user && !memberIds.has(c.user.id))
  })

  /* ---------- 成员宫格：人数过多自动折叠（默认显示前 N 人，点「查看全部」展开） ---------- */
  /** 折叠态默认最多显示的人数；超过则收起，露出「查看全部 X 人」。移除模式下强制展开。 */
  const MAX_COLLAPSED = 8
  const showAllMembers = ref(false)
  const membersTotal = computed(() => (group.value?.members ?? []).length)
  const visibleMembers = computed(() => {
    const all = group.value?.members ?? []
    if (removeMode.value || showAllMembers.value || all.length <= MAX_COLLAPSED) return all
    return all.slice(0, MAX_COLLAPSED)
  })
  const membersTruncated = computed(() => !removeMode.value && !showAllMembers.value && membersTotal.value > MAX_COLLAPSED)

  onMounted(() => {
    contacts.load().catch(() => {})
    convStore.loadSessions().catch(() => {})
  })

  async function load () {
    try {
      const res = await api.get(`/groups/${groupId.value}`)
      group.value = res.data
      removeMode.value = false
    } catch {
      snackMsg.value = '群信息加载失败'
      snack.value = true
    }
  }

  /* ---------------- 群头像：选图上传（/files/upload kind=avatar → fileId → PUT 群） ---------------- */
  const avatarDialog = ref(false)
  const avatarBusy = ref(false)
  const avatarUploaded = ref('')
  const avatarPreview = ref('')
  const avatarInput = ref<HTMLInputElement | null>(null)
  let avatarObjectUrl = ''

  function openAvatarDialog () {
    avatarPreview.value = ''
    avatarUploaded.value = ''
    if (avatarObjectUrl) {
      URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = ''
    }
    avatarDialog.value = true
  }

  function pickAvatarFile () {
    avatarInput.value?.click()
  }

  async function onAvatarFile (e: Event) {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = ''
    if (!file) return
    if (!file.type.startsWith('image/')) {
      snackMsg.value = '请选择图片文件'; snack.value = true; return
    }
    if (file.size > 5 * 1024 * 1024) {
      snackMsg.value = '头像图片不能超过 5MB'; snack.value = true; return
    }
    if (avatarObjectUrl) { URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = '' }
    avatarObjectUrl = URL.createObjectURL(file)
    avatarPreview.value = avatarObjectUrl

    avatarBusy.value = true
    try {
      const form = new FormData()
      form.append('file', file)
      form.append('kind', 'avatar')
      const res = await api.post('/files/upload', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      const id = (res.data as { id?: string })?.id
      if (!id) throw new Error('上传未返回 fileId')
      avatarUploaded.value = id
      const blob = await fileUrl(id).catch(() => '')
      if (blob) {
        if (avatarObjectUrl) { URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = '' }
        avatarPreview.value = blob
      }
    } catch (err: any) {
      avatarUploaded.value = ''
      snackMsg.value = err?.response?.data?.error ?? '群头像上传失败，请重试'
      snack.value = true
    } finally {
      avatarBusy.value = false
    }
  }

  function closeAvatarDialog () {
    avatarDialog.value = false
    if (avatarObjectUrl) { URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = '' }
  }

  async function saveAvatar () {
    if (!avatarUploaded.value || avatarBusy.value) return
    avatarBusy.value = true
    try {
      await api.put(`/groups/${groupId.value}`, { avatar: avatarUploaded.value })
      snackMsg.value = '群头像已更新'
      snack.value = true
      await load()
      closeAvatarDialog()
    } catch (err: any) {
      snackMsg.value = err?.response?.data?.error ?? '群头像保存失败，请重试'
      snack.value = true
    } finally {
      avatarBusy.value = false
    }
  }

  /* ---------------- 成员操作 ---------------- */
  const memberSheet = ref(false)
  /* 加群申请（群主/管理员审批 QQ 式） */
  const joinSheet = ref(false)
  const joinBusy = ref(false)
  const joinList = ref<{ userId: string, nickname: string, note: string, at: string }[]>([])
  /** 我的入群方式切换走 setJoinPolicy；审批操作走 acceptJoin/rejectJoin（后端仅群主/管理员，非成员 404/403） */
  const activeMember = ref<GroupMember | null>(null)

  function onMemberTap (m: GroupMember) {
    if (removeMode.value) {
      if (m.userId === group.value?.ownerId) {
        snackMsg.value = '群主不能被移除'
        snack.value = true
        return
      }
      if (m.userId === meId.value) {
        snackMsg.value = '请使用「退出群聊」'
        snack.value = true
        return
      }
      askRemove(m)
      return
    }
    // 普通成员：一点直达对方资料卡（不再套一层底部弹窗）。
    // 管理员/群主：开底部操作面板（置顶「查看资料」+ 管理动作）。
    if (canManage.value && m.userId !== meId.value) {
      activeMember.value = m
      memberSheet.value = true
    } else {
      viewProfileFor(m)
    }
  }

  /** 底部面板里的「查看资料」与宫格直达共用：切到该成员的资料卡页。 */
  function viewProfileFor (m: GroupMember) {
    memberSheet.value = false
    const id = m.userId
    if (!id) return
    const convId = String(route.query.convId ?? '')
    router.push({ name: 'contact-profile', params: { id }, query: { from: 'group', gid: groupId.value, ...(convId ? { convId } : {}) } })
  }

  function viewProfile () {
    const m = activeMember.value
    if (m) viewProfileFor(m)
  }

  async function setRole (m: GroupMember, role: string) {
    try {
      await api.put(`/groups/${groupId.value}/members/${m.userId}`, { role })
      memberSheet.value = false
      await load()
      snackMsg.value = role === 'admin' ? `已将 ${m.nickname} 设为管理员` : `已取消 ${m.nickname} 的管理员`
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '操作失败'
      snack.value = true
    }
  }

  async function setMute (m: GroupMember, muted: boolean) {
    try {
      await api.put(`/groups/${groupId.value}/members/${m.userId}`, { muted })
      memberSheet.value = false
      await load()
      snackMsg.value = muted ? `已禁言 ${m.nickname}` : `已解除 ${m.nickname} 的禁言`
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '操作失败'
      snack.value = true
    }
  }

  function askRemove (m: GroupMember) {
    pendingRemove.value = m
    memberSheet.value = false
    confirmRemove.value = true
  }

  async function doRemoveMember () {
    const m = pendingRemove.value
    confirmRemove.value = false
    if (!m) return
    try {
      await api.delete(`/groups/${groupId.value}/members/${m.userId}`)
      removeMode.value = false
      pendingRemove.value = null
      snackMsg.value = `已将 ${m.nickname} 移出群聊`
      snack.value = true
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '操作失败'
      snack.value = true
    }
  }

  /* ---------------- 编辑：群名称 / 公告 / 本群昵称 ---------------- */
  const editDialog = ref(false)
  const editKind = ref<'name' | 'notice' | 'groupNick'>('name')
  const editValue = ref('')
  const editTitle = computed(() =>
    editKind.value === 'name' ? '群名称' : (editKind.value === 'notice' ? '群公告' : '我在本群的昵称'))

  function openEdit (kind: 'name' | 'notice' | 'groupNick') {
    editKind.value = kind
    editValue.value = kind === 'name'
      ? (group.value?.name ?? '')
      : (kind === 'notice'
        ? (group.value?.notice ?? '')
        : (myGroupNick.value || auth.user?.nickname || ''))
    editDialog.value = true
  }

  async function saveEdit () {
    const value = editValue.value.trim()
    try {
      await (editKind.value === 'groupNick' ? api.put(`/groups/${groupId.value}/members/${meId.value}`, { nickname: value }) : api.put(`/groups/${groupId.value}`, editKind.value === 'name' ? { name: value } : { notice: value }))
      editDialog.value = false
      await load()
      snackMsg.value = '已保存'
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '保存失败'
      snack.value = true
    }
  }

  /* ---------------- 设置头衔（群主/管理员为成员单独设置） ---------------- */
  const titleDialog = ref(false)
  const titleTarget = ref<GroupMember | null>(null)
  const titleValue = ref('')

  function openTitleEditor (m: GroupMember) {
    titleTarget.value = m
    titleValue.value = m.title ?? ''
    memberSheet.value = false
    titleDialog.value = true
  }

  async function saveTitle () {
    const t = titleTarget.value
    if (!t) return
    try {
      await api.put(`/groups/${groupId.value}/members/${t.userId}`, { title: titleValue.value.trim() })
      titleDialog.value = false
      await load()
      snackMsg.value = titleValue.value.trim() ? `已为 ${t.nickname} 设置头衔「${titleValue.value.trim()}」` : `已清除 ${t.nickname} 的头衔`
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '设置头衔失败'
      snack.value = true
    }
  }

  /* ---------------- 会话开关：免打扰 / 置顶 ---------------- */
  async function setFlag (flag: 'muted' | 'pinned', value: boolean) {
    if (!conv.value) return
    try {
      await api.put(`/conversations/${conv.value.id}`, { [flag]: value })
      await convStore.loadSessions()
    } catch {
      snackMsg.value = '设置失败，请重试'
      snack.value = true
    }
  }

  /* ---------------- 复制群号 / 入群方式（QQ 式加群：开放 or 需审批） ---------------- */
  async function copyGroupId () {
    try {
      await navigator.clipboard.writeText(groupId.value)
      snackMsg.value = '群号已复制'
      snack.value = true
    } catch {
      // 剪贴板 API 不可用（非安全上下文）时回退到传统方式
      snackMsg.value = '复制失败，请手动长按群号'
      snack.value = true
    }
  }

  async function setJoinPolicy (policy: 'open' | 'approve') {
    try {
      await api.put(`/groups/${groupId.value}`, { joinPolicy: policy })
      if (group.value) group.value.joinPolicy = policy
      snackMsg.value = policy === 'open' ? '已设为开放（知道群号可直接进）' : '已设为需审批'
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '设置失败'
      snack.value = true
    }
  }

  /* ---------------- 加群申请审批（群主/管理员；后端非管理者 404） ---------------- */
  async function openJoinSheet () {
    joinSheet.value = true
    joinBusy.value = true
    try {
      const res = await api.get(`/groups/${groupId.value}/join-requests`)
      joinList.value = res.data
      if (group.value) group.value.joinPending = joinList.value.length
    } catch {
      joinList.value = []
      snackMsg.value = '加群申请加载失败'
      snack.value = true
    } finally {
      joinBusy.value = false
    }
  }

  async function acceptJoin (userId: string) {
    joinBusy.value = true
    try {
      await api.post(`/groups/${groupId.value}/join-requests/${encodeURIComponent(userId)}/accept`)
      joinList.value = joinList.value.filter(x => x.userId !== userId)
      if (group.value) group.value.joinPending = joinList.value.length
      snackMsg.value = '已通过申请'
      snack.value = true
      // 刷新群成员（新成员进来）
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '操作失败'
      snack.value = true
    } finally {
      joinBusy.value = false
    }
  }

  async function rejectJoin (userId: string) {
    joinBusy.value = true
    try {
      await api.post(`/groups/${groupId.value}/join-requests/${encodeURIComponent(userId)}/reject`)
      joinList.value = joinList.value.filter(x => x.userId !== userId)
      if (group.value) group.value.joinPending = joinList.value.length
      snackMsg.value = '已拒绝申请'
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '操作失败'
      snack.value = true
    } finally {
      joinBusy.value = false
    }
  }

  /* ---------------- 其它 ---------------- */
  function findHistory () {
    // conv 未就绪时绝不能拿 groupId 顶替会话 id —— 那会打开一个不存在的会话，
    // 聊天页拉到空列表还带 search 参数，用户看到的是「坏了」而不是「还没聊天」。
    const id = conv.value?.id
    if (!id) {
      snackMsg.value = '该群还没有聊天记录'
      snack.value = true
      return
    }
    router.push({ name: 'chat-detail', params: { id }, query: { search: '1' } })
  }

  async function invite (c: Contact) {
    if (!c.user) return
    try {
      await api.post(`/groups/${groupId.value}/members`, { userId: c.user.id })
      snackMsg.value = `已邀请 ${c.remark || c.user.nickname} 入群`
      snack.value = true
      inviteSheet.value = false
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '邀请失败'
      snack.value = true
    }
  }

  async function transfer (userId: string) {
    try {
      await api.post(`/groups/${groupId.value}/transfer`, { userId })
      const name = group.value?.members.find(x => x.userId === userId)?.nickname ?? ''
      snackMsg.value = `已将群主转让给 ${name}`
      snack.value = true
      transferSheet.value = false
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '转让失败'
      snack.value = true
    }
  }

  async function dissolve () {
    try {
      await api.delete(`/groups/${groupId.value}`)
      confirmDissolve.value = false
      snackMsg.value = '群已解散'
      snack.value = true
      convStore.loadSessions().catch(() => {})
      goBack(true)
    } catch (error: any) {
      confirmDissolve.value = false
      snackMsg.value = error?.response?.data?.error ?? '解散失败'
      snack.value = true
    }
  }

  async function leave () {
    confirmLeave.value = false
    try {
      await api.post(`/groups/${groupId.value}/leave`)
      snackMsg.value = '已退出群聊'
      snack.value = true
      convStore.loadSessions().catch(() => {})
      goBack(true)
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? '退出失败'
      snack.value = true
    }
  }

  function goBack (toListOnly = false) {
    // 群已退出/解散：会话已不存在，强制回列表。
    if (toListOnly) {
      router.push({ name: 'chat' })
      return
    }
    // 从群会话详情页跳进本页时 query 带 convId → 直接回那个会话页。
    const convId = String(route.query.convId ?? '')
    if (convId) {
      router.replace({ name: 'chat-detail', params: { id: convId } })
      return
    }
    // 无 convId（如从别处直达本页）：按群 id 反查该群会话，回到群聊天而非首页。
    const conv = convStore.sessions.find(s => s.kind === 'group' && s.memberId === groupId.value)
    if (conv) {
      router.replace({ name: 'chat-detail', params: { id: conv.id } })
      return
    }
    router.push({ name: 'chat' })
  }

  watch(groupId, () => {
    void load()
  })
  onMounted(load)
</script>

<style scoped>
.wx-chat-bar {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

.wx-member-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(58px, 1fr));
  gap: 12px 6px;
  background: rgb(var(--v-theme-surface));
  border-radius: 10px;
  overflow: hidden;
  margin-top: 8px;
}

.wx-member-sec-head {
  padding: 2px 4px;
}

.wx-member-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  cursor: pointer;
  min-width: 0;
}

.wx-member-avatar {
  position: relative;
  width: 52px;
  height: 52px;
  border-radius: 50%;
  overflow: visible;
}
.wx-member-avatar :deep(.user-avatar) {
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.12);
}

.wx-member-name {
  font-size: 12px;
  color: rgb(var(--v-theme-on-surface));
  max-width: 100%;
}

.wx-member-action {
  width: 52px;
  height: 52px;
  border: 1px dashed rgb(var(--v-theme-wechat-divider, #d0d0d0));
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: rgb(var(--v-theme-on-surface));
  background: rgb(var(--v-theme-surface));

  &.is-on {
    border-color: #fa5151;
    color: #fa5151;
  }
}

.wx-remove-badge {
  position: absolute;
  top: -4px;
  right: -6px;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: #fa5151;
  color: #fff;
  font-size: 14px;
  line-height: 18px;
  text-align: center;
}

.wx-owner-badge {
  position: absolute;
  bottom: -6px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(0, 0, 0, 0.65);
  color: #ffd60a;
  font-size: 10px;
  line-height: 1;
  padding: 2px 5px;
  border-radius: 6px;
  white-space: nowrap;
}

.wx-admin-badge {
  position: absolute;
  bottom: -6px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(0, 0, 0, 0.65);
  color: #4f9cff;
  font-size: 10px;
  line-height: 1;
  padding: 2px 5px;
  border-radius: 6px;
  white-space: nowrap;
}

/* 头衔徽标（群主/管理员为成员单独设置，如「群管」「元老」） */
.wx-title-badge {
  position: absolute;
  bottom: -6px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(0, 0, 0, 0.65);
  color: #ffd60a;
  font-size: 10px;
  line-height: 1;
  padding: 2px 5px;
  border-radius: 6px;
  white-space: nowrap;
  max-width: 56px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.wx-group-list {
  border-radius: 0;
}

.wx-row-value {
  /* 用 min(视口, 固定值) 而非纯 vw：vw 是「视口」宽，但本页在电脑/iPad 上只占右侧一列，
     55vw 会超出该列实际宽度 → 值文本被挤进开关/箭头区。限到「45vw 与 220px 里更小的那个」：
     手机整页（= 视口）时 45vw 生效，电脑窄列时 220px 生效。 */
  max-width: min(45vw, 220px);
  margin-left: 12px;
}

.wx-row-switch {
  margin-left: 8px;
}

.is-me {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
  border-radius: 50%;
}
</style>
