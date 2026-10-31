<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">{{ t('group.settings') }}</v-toolbar-title>
    </v-app-bar>

    <div class="flex-grow-1 overflow-y-auto wx-scroll">
      <!-- 成员宫格（人数过多自动折叠，点「查看全部」展开；点头像查资料卡） -->
      <div class="wx-member-sec pa-3">
        <div class="wx-member-sec-head d-flex align-center justify-space-between">
          <span class="text-body-small text-medium-emphasis">{{ t('group.member.count', { n: group?.members?.length ?? 0 }) }}</span>
          <v-btn v-if="membersTruncated" color="primary" density="compact" size="x-small" variant="text" @click="showAllMembers = true">
            <v-icon icon="mdi-eye" size="16" class="mr-1" /> {{ t('gp.showAll', { n: group?.members?.length ?? 0 }) }}
          </v-btn>
          <v-btn v-else-if="membersTotal > MAX_COLLAPSED" color="medium-emphasis" density="compact" size="x-small" variant="text" @click="showAllMembers = false">{{ t('gp.collapse') }}</v-btn>
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
              <span v-else-if="m.role === 'owner'" class="wx-owner-badge">{{ t('group.role.owner') }}</span>
              <span v-else-if="m.role === 'admin'" class="wx-admin-badge">{{ t('group.role.admin') }}</span>
              <span v-else-if="m.title" class="wx-title-badge">{{ m.title }}</span>
            </div>

            <div class="wx-member-name text-truncate">{{ m.nickname }}</div>
          </div>

          <template v-if="canManage">
            <div class="wx-member-cell" @click="inviteSheet = true">
              <div class="wx-member-avatar wx-member-action"><v-icon icon="mdi-plus" size="28" /></div>
              <div class="wx-member-name">{{ t('group.invite') }}</div>
            </div>

            <div class="wx-member-cell" @click="removeMode = !removeMode">
              <div class="wx-member-avatar wx-member-action" :class="{ 'is-on': removeMode }"><v-icon icon="mdi-minus" size="28" /></div>
              <div class="wx-member-name">{{ t('group.kick') }}</div>
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
          <v-list-item-title>{{ t('group.avatar') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ group?.avatar ? t('group.avatar.set') : t('group.avatar.none') }}</span>
            <v-icon v-if="canManage" icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="canManage ? openEdit('name') : null">
          <v-list-item-title>{{ t('group.name') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ group?.name }}</span>
            <v-icon v-if="canManage" icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="openEdit('notice')">
          <v-list-item-title>{{ t('group.notice') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ group?.notice || t('common.notSet') }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="openEdit('groupNick')">
          <v-list-item-title>{{ t('group.myNick') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis text-truncate wx-row-value">{{ myGroupNick || t('group.myNick.default') }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>
      </v-list>

      <!-- 消息设置 -->
      <v-list bg-color="surface" class="wx-group-list mt-3">
        <v-list-item>
          <template #prepend><v-icon icon="mdi-bell-off-outline" /></template>
          <v-list-item-title>{{ t('group.mute') }}</v-list-item-title>

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
          <v-list-item-title>{{ t('group.pin') }}</v-list-item-title>

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
          <v-list-item-title>{{ t('group.search.msg') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item v-if="canManage" @click="inviteSheet = true">
          <template #prepend><v-icon icon="mdi-account-plus-outline" /></template>
          <v-list-item-title>{{ t('group.invite.members') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item v-if="canManage" @click="openJoinSheet">
          <template #prepend><v-icon icon="mdi-account-check" /></template>
          <v-list-item-title>{{ t('group.join.requests') }}</v-list-item-title>
          <template #append>
            <v-badge v-if="group?.joinPending" color="wechat-unread" :content="group!.joinPending" />
            <v-icon v-else icon="mdi-chevron-right" />
          </template>
        </v-list-item>

        <v-list-item v-if="canManage">
          <template #prepend><v-icon icon="mdi-key" /></template>
          <v-list-item-title>{{ t('group.join.policy') }}</v-list-item-title>
          <v-list-item-subtitle>{{ joinPolicyLabel(group?.joinPolicy ?? 'approve') }}</v-list-item-subtitle>
          <template #append>
            <v-btn
              density="compact"
              icon="mdi-chevron-right"
              size="small"
              variant="text"
              @click="openPolicyDialog"
            />
          </template>
        </v-list-item>

        <!-- 全员禁言（QQ 式）：开启后普通成员不能发言，群主/管理员不受限 -->
        <v-list-item v-if="canManage">
          <template #prepend><v-icon icon="mdi-megaphone-off" /></template>
          <v-list-item-title>{{ t('group.muteAll') }}</v-list-item-title>
          <v-list-item-subtitle>{{ t('group.muteAll.desc') }}</v-list-item-subtitle>
          <template #append>
            <v-switch
              class="wx-row-switch"
              color="primary"
              density="compact"
              hide-details
              :model-value="!!group?.muteAll"
              @update:model-value="v => setMuteAll(!!v)"
            />
          </template>
        </v-list-item>
      </v-list>

      <!-- 危险操作 -->
      <v-list bg-color="surface" class="wx-group-list mt-3">
        <v-list-item v-if="isOwner" @click="transferSheet = true">
          <template #prepend><v-icon icon="mdi-crown-outline" /></template>
          <v-list-item-title>{{ t('group.transfer') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="confirmLeave = true">
          <template #prepend><v-icon color="error" icon="mdi-exit-run" /></template>
          <v-list-item-title class="text-error">{{ t('group.leave') }}</v-list-item-title>
        </v-list-item>

        <v-list-item v-if="isOwner" @click="confirmDissolve = true">
          <template #prepend><v-icon color="error" icon="mdi-delete-outline" /></template>
          <v-list-item-title class="text-error">{{ t('group.dissolve') }}</v-list-item-title>
        </v-list-item>
      </v-list>

      <div class="pa-4 d-flex align-center justify-center gap-2 text-body-small text-medium-emphasis">
        <span>{{ t('group.id', { id: groupId }) }}</span>
        <v-btn icon="mdi-content-copy" size="x-small" variant="text" :title="t('group.copy.id')" @click="copyGroupId" />
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
              {{ activeMember?.role === 'owner' ? t('group.role.owner') : activeMember?.role === 'admin' ? t('group.role.admin') : t('group.role.member') }}
            </div>
          </div>
        </div>

        <v-list density="compact">
          <v-list-item prepend-icon="mdi-account-outline" :title="t('group.viewProfile')" @click="viewProfile" />

          <v-list-item
            v-if="activeMember?.userId === meId"
            prepend-icon="mdi-card-account-details-outline"
            :title="t('group.myNick.edit')"
            @click="memberSheet = false; openEdit('groupNick')"
          />

          <template v-if="canManage && activeMember && activeMember.userId !== group?.ownerId">
            <v-list-item
              v-if="activeMember.userId !== meId && activeMember.role !== 'admin'"
              prepend-icon="mdi-shield-plus"
              :title="t('group.role.setAdmin')"
              @click="setRole(activeMember, 'admin')"
            />

            <v-list-item
              v-if="activeMember.userId !== meId && activeMember.role === 'admin'"
              prepend-icon="mdi-shield-off"
              :title="t('group.role.unsetAdmin')"
              @click="setRole(activeMember, 'member')"
            />

            <v-list-item
              v-if="activeMember.userId !== meId"
              :prepend-icon="memberMuted(activeMember) ? 'mdi-volume-high' : 'mdi-volume-off'"
              :title="memberMuted(activeMember) ? t('group.unmuteMember') : t('group.muteMember')"
              @click="openMuteDuration(activeMember)"
            />

            <v-list-item
              v-if="activeMember.userId !== meId"
              prepend-icon="mdi-tag-multiple-outline"
              :title="t('group.title.edit', { s: activeMember.title || t('group.title.none') })"
              @click="openTitleEditor(activeMember)"
            />

            <v-list-item
              v-if="activeMember.userId !== meId"
              class="text-error"
              prepend-icon="mdi-account-remove"
              :title="t('group.remove.member')"
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
            :label="t('group.name')"
            maxlength="32"
            variant="outlined"
          />

          <v-textarea
            v-else-if="editKind === 'notice'"
            v-model="editValue"
            counter
            :label="t('group.notice')"
            maxlength="200"
            rows="3"
            variant="outlined"
          />

          <v-text-field
            v-else
            v-model="editValue"
            counter
            :label="t('group.myNick.field')"
            maxlength="24"
            variant="outlined"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="editDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!editValue.trim()" @click="saveEdit">{{ t('common.save') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 群头像：群主/管理员选图上传（/files/upload kind=avatar 拿 fileId 再 PUT 群） -->
    <v-dialog v-model="avatarDialog" max-width="320">
      <v-card>
        <v-card-title class="text-body-large">{{ t('group.avatar') }}</v-card-title>

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
            {{ avatarUploaded ? t('group.avatar.change') : t('group.avatar.pick') }}
          </v-btn>
          <div class="text-caption text-medium-emphasis mt-2 text-center">{{ t('group.avatar.hint') }}</div>
        </v-card-text>

        <v-card-actions>
          <v-btn variant="text" @click="closeAvatarDialog">{{ t('common.cancel') }}</v-btn>
          <v-btn
            color="primary"
            :disabled="!avatarUploaded || avatarBusy"
            @click="saveAvatar"
          >{{ t('common.save') }}</v-btn>
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
        <div class="text-title-small pa-2">{{ t('group.invite.friends') }}</div>

        <v-list density="compact">
          <v-list-item v-for="c in inviteCandidates" :key="c.id" @click="invite(c)">
            <template #prepend>
              <user-avatar :nickname="c.user?.nickname" :src="c.user?.avatar" :size="34" />
            </template>

            <v-list-item-title>{{ c.remark || c.user?.nickname }}</v-list-item-title>
            <template #append><v-icon color="primary" icon="mdi-account-plus" /></template>
          </v-list-item>
        </v-list>

        <div v-if="inviteCandidates.length === 0" class="pa-4 text-body-small text-medium-emphasis">{{ t('group.invite.none') }}</div>
      </v-card>
    </v-bottom-sheet>

    <!-- 转让群主 -->
    <v-bottom-sheet v-model="transferSheet" max-width="440" scrollable>
      <v-card class="pa-2">
        <div class="text-title-small pa-2">{{ t('group.transfer.pick') }}</div>

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
        <div class="text-title-small pa-2">{{ t('group.join.requests') }}</div>

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
              <v-btn color="primary" size="x-small" :disabled="joinBusy" @click="acceptJoin(r.userId)">{{ t('group.join.accept') }}</v-btn>
              <v-btn size="x-small" variant="text" :disabled="joinBusy" @click="openReject(r.userId)">{{ t('contacts.reject') }}</v-btn>
            </template>
          </v-list-item>
        </v-list>

        <v-list v-else-if="!joinBusy" class="justify-center" disabled>
          <v-list-item-title class="text-medium-emphasis">{{ t('group.join.none') }}</v-list-item-title>
        </v-list>

        <div class="d-flex justify-end ga-2 pa-2">
          <v-btn variant="text" @click="joinSheet = false">{{ t('common.close') }}</v-btn>
        </div>
      </v-card>
    </v-bottom-sheet>

    <!-- 加群方式（QQ 式四选一）+ 入群问题 -->
    <v-dialog v-model="policyDialog" max-width="380">
      <v-card>
        <v-card-title class="text-body-large">{{ t('group.join.policy') }}</v-card-title>
        <v-card-text>
          <v-radio-group v-model="policyDraft" density="compact" hide-details>
            <v-radio :label="t('group.join.open.desc')" value="open" />
            <v-radio :label="t('group.join.question.desc')" value="question" />
            <v-radio :label="t('group.join.approve')" value="approve" />
            <v-radio :label="t('group.join.none2')" value="none" />
          </v-radio-group>
          <template v-if="policyDraft === 'question'">
            <v-text-field
              v-model="policyQuestion"
              class="mt-2"
              counter="64"
              density="compact"
              :label="t('group.join.q.label')"
              :placeholder="t('group.join.q.ph')"
              variant="outlined"
            />
            <v-text-field
              v-model="policyAnswer"
              class="mt-2"
              counter="64"
              density="compact"
              :label="t('group.join.a.label')"
              :placeholder="t('group.join.a.ph')"
              variant="outlined"
            />
            <div class="text-caption text-medium-emphasis">{{ t('group.join.q.hint') }}</div>
          </template>
          <div v-else-if="policyDraft === 'none'" class="text-caption text-medium-emphasis mt-2">{{ t('group.join.none.hint') }}</div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="policyDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" @click="savePolicy">{{ t('common.save') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 禁言时长（QQ 式：限时 / 永久 / 解除） -->
    <v-dialog v-model="muteDialog" max-width="300">
      <v-card>
        <v-card-title class="text-body-large">{{ t('group.mute.pick', { name: muteTarget?.nickname ?? '' }) }}</v-card-title>
        <v-list>
          <v-list-item
            v-for="d in MUTE_DURATIONS"
            :key="d.minutes"
            :title="t(d.key)"
            @click="applyMute('timed', d.minutes)"
          />
          <v-list-item :title="t('group.mute.perm')" @click="applyMute('perm')" />
          <v-list-item v-if="muteTarget && memberMuted(muteTarget)" :title="t('group.unmuteMember')" @click="applyMute('off')" />
        </v-list>
      </v-card>
    </v-dialog>

    <!-- 审批拒绝：可附理由（将通知申请人） -->
    <v-dialog v-model="rejectDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">{{ t('group.join.reject.title') }}</v-card-title>
        <v-card-text>
          <v-textarea
            v-model="rejectReason"
            auto-grow
            counter="64"
            density="compact"
            :label="t('group.join.reject.reason')"
            rows="2"
            variant="outlined"
          />
          <div class="text-caption text-medium-emphasis">{{ t('group.join.reject.hint') }}</div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="rejectDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :loading="joinBusy" @click="confirmReject">{{ t('common.confirm') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 退出群聊确认 -->
    <v-dialog v-model="confirmLeave" max-width="320">
      <v-card class="pa-4">
        <div class="text-title-large">{{ t('group.leave.confirm') }}</div>

        <div class="text-body-medium text-medium-emphasis my-2">
          {{ t('group.leave.text', { name: group?.name ?? '' }) }}
        </div>

        <div class="d-flex justify-end gap-2">
          <v-btn variant="text" @click="confirmLeave = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="error" @click="leave">{{ t('group.leave.btn') }}</v-btn>
        </div>
      </v-card>
    </v-dialog>

    <!-- 移出成员确认 -->
    <v-dialog v-model="confirmRemove" max-width="320">
      <v-card class="pa-4">
        <div class="text-title-large">{{ t('group.remove.confirm') }}</div>

        <div class="text-body-medium text-medium-emphasis my-2">
          {{ t('group.remove.text', { name: pendingRemove?.nickname ?? '' }) }}
        </div>

        <div class="d-flex justify-end gap-2">
          <v-btn variant="text" @click="confirmRemove = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="error" @click="doRemoveMember">{{ t('group.remove.btn') }}</v-btn>
        </div>
      </v-card>
    </v-dialog>

    <!-- 解散确认 -->
    <v-dialog v-model="confirmDissolve" max-width="320">
      <v-card class="pa-4">
        <div class="text-title-large">{{ t('group.dissolve.confirm') }}</div>
        <div class="text-body-medium text-medium-emphasis my-2">{{ t('group.dissolve.text', { name: group?.name ?? '' }) }}</div>
        <div class="d-flex justify-end gap-2">
          <v-btn variant="text" @click="confirmDissolve = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="error" @click="dissolve">{{ t('group.dissolve.btn') }}</v-btn>
        </div>
      </v-card>
    </v-dialog>

    <!-- 设置头衔（群主/管理员为任意成员单独设置，空 = 清除） -->
    <v-dialog v-model="titleDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">{{ t('group.title.set') }}</v-card-title>

        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-2">
            {{ t('group.title.hint', { name: titleTarget?.nickname ?? '' }) }}
          </div>
          <v-text-field
            v-model="titleValue"
            counter
            :label="t('group.title.label')"
            maxlength="12"
            :placeholder="t('group.title.ph')"
            variant="outlined"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="titleDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" @click="saveTitle">{{ t('common.save') }}</v-btn>
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
  import { t } from '@/lib/i18n'
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
      snackMsg.value = t('group.load.fail')
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
      snackMsg.value = t('group.avatar.notImage'); snack.value = true; return
    }
    if (file.size > 5 * 1024 * 1024) {
      snackMsg.value = t('group.avatar.tooBig'); snack.value = true; return
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
      snackMsg.value = err?.response?.data?.error ?? t('group.avatar.uploadFail')
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
      snackMsg.value = t('group.avatar.updated')
      snack.value = true
      await load()
      closeAvatarDialog()
    } catch (err: any) {
      snackMsg.value = err?.response?.data?.error ?? t('group.avatar.saveFail')
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
        snackMsg.value = t('group.remove.owner')
        snack.value = true
        return
      }
      if (m.userId === meId.value) {
        snackMsg.value = t('group.remove.self')
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
      snackMsg.value = role === 'admin' ? t('group.role.adminSet', { name: m.nickname }) : t('group.role.adminUnset', { name: m.nickname })
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('common.op.fail')
      snack.value = true
    }
  }

  /* 旧的单开关禁言已由「禁言时长对话框」（applyMute）取代 */

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
      snackMsg.value = t('group.remove.ok', { name: m.nickname })
      snack.value = true
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('common.op.fail')
      snack.value = true
    }
  }

  /* ---------------- 编辑：群名称 / 公告 / 本群昵称 ---------------- */
  const editDialog = ref(false)
  const editKind = ref<'name' | 'notice' | 'groupNick'>('name')
  const editValue = ref('')
  const editTitle = computed(() =>
    editKind.value === 'name' ? t('group.name') : (editKind.value === 'notice' ? t('group.notice') : t('group.myNick')))

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
      snackMsg.value = t('me.saved')
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('common.save.fail')
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
    const target = titleTarget.value
    if (!target) return
    try {
      await api.put(`/groups/${groupId.value}/members/${target.userId}`, { title: titleValue.value.trim() })
      titleDialog.value = false
      await load()
      snackMsg.value = titleValue.value.trim() ? t('group.title.ok', { name: target.nickname, title: titleValue.value.trim() }) : t('group.title.cleared', { name: target.nickname })
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('group.title.fail')
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
      snackMsg.value = t('group.flag.fail')
      snack.value = true
    }
  }

  /* ---------------- 复制群号 / 入群方式（QQ 式加群：开放 or 需审批） ---------------- */
  async function copyGroupId () {
    try {
      await navigator.clipboard.writeText(groupId.value)
      snackMsg.value = t('group.copy.ok')
      snack.value = true
    } catch {
      // 剪贴板 API 不可用（非安全上下文）时回退到传统方式
      snackMsg.value = t('group.copy.fail')
      snack.value = true
    }
  }

  /* ---------- 加群方式（QQ 式四选一）+ 入群问题 ---------- */
  const policyDialog = ref(false)
  const policyDraft = ref<'open' | 'approve' | 'question' | 'none'>('approve')
  const policyQuestion = ref('')
  const policyAnswer = ref('')

  function joinPolicyLabel (p: string) {
    switch (p) {
      case 'open': return t('group.join.open.desc')
      case 'question': return t('group.join.question.desc')
      case 'none': return t('group.join.none2')
      default: return t('group.join.approve')
    }
  }

  function openPolicyDialog () {
    policyDraft.value = (group.value?.joinPolicy ?? 'approve') as typeof policyDraft.value
    policyQuestion.value = group.value?.joinQuestion ?? ''
    policyAnswer.value = group.value?.joinAnswer ?? ''
    policyDialog.value = true
  }

  async function savePolicy () {
    if (policyDraft.value === 'question' && (!policyQuestion.value.trim() || !policyAnswer.value.trim())) {
      snackMsg.value = t('group.join.needQA')
      snack.value = true
      return
    }
    try {
      await api.put(`/groups/${groupId.value}`, {
        joinPolicy: policyDraft.value,
        joinQuestion: policyQuestion.value,
        joinAnswer: policyAnswer.value,
      })
      if (group.value) {
        group.value.joinPolicy = policyDraft.value
        group.value.joinQuestion = policyQuestion.value
        group.value.joinAnswer = policyAnswer.value
      }
      policyDialog.value = false
      snackMsg.value = t('group.policy.ok')
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('group.set.fail')
      snack.value = true
    }
  }

  /* ---------- 全员禁言（QQ 式） ---------- */
  async function setMuteAll (v: boolean) {
    try {
      await api.put(`/groups/${groupId.value}`, { muteAll: v })
      if (group.value) group.value.muteAll = v
      snackMsg.value = v ? t('group.muteAll.on') : t('group.muteAll.off')
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('group.set.fail')
      snack.value = true
    }
  }

  /* ---------- 成员禁言：永久 / 限时（QQ 式时长） ---------- */
  const muteDialog = ref(false)
  const muteTarget = ref<GroupMember | null>(null)
  const MUTE_DURATIONS = [
    { minutes: 10, key: 'group.mute.10m' },
    { minutes: 60, key: 'group.mute.1h' },
    { minutes: 720, key: 'group.mute.12h' },
    { minutes: 1440, key: 'group.mute.1d' },
  ]

  function memberMuted (m: GroupMember) {
    return !!m.muted || !!(m.mutedUntil && new Date(m.mutedUntil) > new Date())
  }

  function openMuteDuration (m: GroupMember) {
    muteTarget.value = m
    muteDialog.value = true
  }

  async function applyMute (kind: 'timed' | 'perm' | 'off', minutes = 0) {
    const m = muteTarget.value
    if (!m) return
    try {
      if (kind === 'timed') await api.put(`/groups/${groupId.value}/members/${m.userId}`, { mutedMinutes: minutes })
      else if (kind === 'perm') await api.put(`/groups/${groupId.value}/members/${m.userId}`, { muted: true })
      else await api.put(`/groups/${groupId.value}/members/${m.userId}`, { muted: false, mutedMinutes: 0 })
      if (kind === 'timed') {
        m.muted = false
        m.mutedUntil = new Date(Date.now() + minutes * 60_000).toISOString()
      } else if (kind === 'perm') {
        m.muted = true
        m.mutedUntil = undefined
      } else {
        m.muted = false
        m.mutedUntil = undefined
      }
      muteDialog.value = false
      snackMsg.value = kind === 'off' ? t('group.unmuteMember.ok', { name: m.nickname }) : t('group.muteMember.ok', { name: m.nickname })
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('group.flag.fail')
      snack.value = true
    }
  }

  /* ---------- 审批拒绝：可附理由（理由会以系统消息通知申请人，QQ 式） ---------- */
  const rejectDialog = ref(false)
  const rejectUserId = ref('')
  const rejectReason = ref('')

  function openReject (userId: string) {
    rejectUserId.value = userId
    rejectReason.value = ''
    rejectDialog.value = true
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
      snackMsg.value = t('group.join.loadFail')
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
      snackMsg.value = t('group.join.accepted')
      snack.value = true
      // 刷新群成员（新成员进来）
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('common.op.fail')
      snack.value = true
    } finally {
      joinBusy.value = false
    }
  }

  async function confirmReject () {
    joinBusy.value = true
    try {
      await api.post(`/groups/${groupId.value}/join-requests/${encodeURIComponent(rejectUserId.value)}/reject`, { reason: rejectReason.value })
      joinList.value = joinList.value.filter(x => x.userId !== rejectUserId.value)
      if (group.value) group.value.joinPending = joinList.value.length
      rejectDialog.value = false
      snackMsg.value = t('group.join.rejected')
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('common.op.fail')
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
      snackMsg.value = t('group.no.history')
      snack.value = true
      return
    }
    router.push({ name: 'chat-detail', params: { id }, query: { search: '1' } })
  }

  async function invite (c: Contact) {
    if (!c.user) return
    try {
      await api.post(`/groups/${groupId.value}/members`, { userId: c.user.id })
      snackMsg.value = t('group.invite.ok', { name: c.remark || c.user.nickname })
      snack.value = true
      inviteSheet.value = false
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('group.invite.fail')
      snack.value = true
    }
  }

  async function transfer (userId: string) {
    try {
      await api.post(`/groups/${groupId.value}/transfer`, { userId })
      const name = group.value?.members.find(x => x.userId === userId)?.nickname ?? ''
      snackMsg.value = t('group.transfer.ok', { name })
      snack.value = true
      transferSheet.value = false
      await load()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('group.transfer.fail')
      snack.value = true
    }
  }

  async function dissolve () {
    try {
      await api.delete(`/groups/${groupId.value}`)
      confirmDissolve.value = false
      snackMsg.value = t('group.dissolve.ok')
      snack.value = true
      convStore.loadSessions().catch(() => {})
      goBack(true)
    } catch (error: any) {
      confirmDissolve.value = false
      snackMsg.value = error?.response?.data?.error ?? t('group.dissolve.fail')
      snack.value = true
    }
  }

  async function leave () {
    confirmLeave.value = false
    try {
      await api.post(`/groups/${groupId.value}/leave`)
      snackMsg.value = t('group.leave.ok')
      snack.value = true
      convStore.loadSessions().catch(() => {})
      goBack(true)
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('group.leave.fail')
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
