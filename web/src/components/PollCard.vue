<template>
  <div class="wx-poll-card">
    <div class="d-flex align-center gap-2">
      <span class="wx-poll-title">{{ poll?.title }}</span>
      <v-chip
        v-if="poll"
        density="comfortable"
        size="x-small"
        :color="poll.closed ? undefined : 'primary'"
        variant="tonal"
      >
        {{ poll.closed ? t('poll.closed') : poll.multi ? t('poll.multiTag') : t('poll.single') }}
      </v-chip>
    </div>

    <div v-if="!poll" class="py-2 text-body-small text-medium-emphasis">{{ t('common.loading') }}</div>
    <template v-else>
      <div class="mt-1 text-body-small text-medium-emphasis">{{ t('poll.votes', { n: totalVoters }) }}</div>

      <!-- 选项：整行可点投票；票数占比以绿色进度条呈现（再点一次 = 多选取消该票，服务端裁决） -->
      <div class="mt-2 wx-poll-options">
        <button
          v-for="(opt, i) in poll.options"
          :key="i"
          class="wx-poll-option"
          :class="{ 'is-mine': isMine(i) }"
          :disabled="poll.closed"
          type="button"
          @click="vote(i)"
        >
          <span class="wx-poll-bar" :style="{ width: pct(i) + '%' }" />
          <span class="wx-poll-opt-text">
            <v-icon v-if="isMine(i)" class="mr-1" color="primary" icon="mdi-check-circle" size="14" />
            {{ opt.text }}
          </span>
          <span class="wx-poll-opt-count">{{ opt.userIds.length }}</span>
        </button>
      </div>

      <div class="mt-2 d-flex align-center" style="min-height: 24px; gap: 8px">
        <span v-if="msg" class="text-caption" :class="msgOk ? 'text-success' : 'text-error'">{{ msg }}</span>
        <v-spacer />
        <v-btn
          v-if="canClose"
          color="error"
          density="compact"
          size="small"
          variant="text"
          @click="closePoll"
        >{{ t('poll.close') }}</v-btn>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
  import { computed, ref } from 'vue'

  import { t } from '@/lib/i18n'
  import { useAuthStore } from '@/stores/auth'
  import { usePollsStore } from '@/stores/polls'

  const props = defineProps<{ conversationId: string, pollId: string }>()
  const polls = usePollsStore()
  const auth = useAuthStore()

  const poll = computed(() => polls.pollOf(props.conversationId, props.pollId))
  const totalVoters = computed(() => {
    const p = poll.value
    if (!p) return 0
    const set = new Set<string>()
    p.options.forEach(o => o.userIds.forEach(u => set.add(u)))
    return set.size
  })
  const votesOf = (i: number) => poll.value?.options[i]?.userIds.length ?? 0
  const pct = (i: number) => {
    const total = poll.value?.options.reduce((s, o) => s + o.userIds.length, 0) ?? 0
    return total > 0 ? Math.round((votesOf(i) * 100) / total) : 0
  }
  const isMine = (i: number) => !!auth.user?.id && !!poll.value?.options[i]?.userIds.includes(auth.user.id)
  // 截止按钮只给发起人看（服务端对群主同样放行，这里保持界面简单）
  const canClose = computed(() => !!poll.value && !poll.value.closed && poll.value.createdBy === auth.user?.id)

  const msg = ref('')
  const msgOk = ref(false)
  let msgTimer: ReturnType<typeof setTimeout> | null = null
  function flash (text: string, ok = false) {
    msg.value = text
    msgOk.value = ok
    if (msgTimer) clearTimeout(msgTimer)
    msgTimer = setTimeout(() => { msg.value = '' }, 2500)
  }

  async function vote (i: number) {
    if (!poll.value || poll.value.closed) return
    try {
      await polls.vote(props.conversationId, poll.value.id, i)
    } catch {
      flash(t('poll.vote.fail'))
    }
  }

  async function closePoll () {
    if (!poll.value) return
    try {
      await polls.close(props.conversationId, poll.value.id)
      flash(t('poll.close.ok'), true)
    } catch {
      flash(t('poll.close.fail'))
    }
  }
</script>

<style scoped>
.wx-poll-card {
  width: min(280px, 72vw);
  padding: 10px 12px;
  border-radius: 12px;
  background: rgba(127, 127, 127, 0.1);
}

.wx-poll-title {
  font-weight: 600;
  font-size: 14px;
  word-break: break-all;
}

.wx-poll-options {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.wx-poll-option {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 7px 10px;
  border: none;
  border-radius: 8px;
  background: rgba(127, 127, 127, 0.12);
  color: inherit;
  font-size: 13px;
  text-align: left;
  overflow: hidden;
  cursor: pointer;
}

.wx-poll-option:disabled {
  cursor: default;
}

.wx-poll-bar {
  position: absolute;
  top: 0;
  bottom: 0;
  left: 0;
  background: rgba(7, 193, 96, 0.22);
  transition: width 0.3s ease;
}

.wx-poll-opt-text {
  position: relative;
  display: flex;
  align-items: center;
  word-break: break-all;
}

.wx-poll-option.is-mine .wx-poll-opt-text {
  color: rgb(var(--v-theme-primary));
  font-weight: 600;
}

.wx-poll-opt-count {
  position: relative;
  font-size: 12px;
  opacity: 0.75;
}
</style>
