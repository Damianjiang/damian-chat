<template>
  <div class="wx-solitaire-card">
    <div class="d-flex align-center gap-2">
      <v-icon class="flex-shrink-0" color="primary" icon="mdi-format-list-numbered" size="18" />
      <span class="wx-solitaire-title">{{ solitaire?.title || t('solitaire.title') }}</span>
      <v-chip v-if="solitaire?.closed" density="comfortable" size="x-small" variant="tonal">{{ t('solitaire.closed') }}</v-chip>
    </div>

    <div v-if="!solitaire" class="py-2 text-body-small text-medium-emphasis">{{ t('common.loading') }}</div>
    <template v-else>
      <div v-if="solitaire.note" class="wx-solitaire-note">{{ solitaire.note }}</div>

      <div class="wx-solitaire-count">
        {{ t('solitaire.count', { n: solitaire.entries.length }) }}
        <span v-if="solitaire.closed" class="ml-2">· {{ t('solitaire.closedHint') }}</span>
      </div>

      <ol class="wx-solitaire-list">
        <li v-for="(e, i) in solitaire.entries" :key="e.userId" class="wx-solitaire-item">
          <span class="wx-solitaire-no">{{ i + 1 }}.</span>
          <span class="wx-solitaire-name" :class="{ 'is-me': e.userId === auth.user?.id }">{{ e.nickname }}</span>
          <span v-if="e.note" class="wx-solitaire-entry-note">{{ e.note }}</span>
        </li>
        <li v-if="!solitaire.entries.length" class="wx-solitaire-empty">{{ t('solitaire.empty') }}</li>
      </ol>

      <template v-if="!solitaire.closed">
        <div class="wx-solitaire-join">
          <input
            v-model="note"
            class="wx-solitaire-input"
            :maxlength="80"
            :placeholder="t('solitaire.note.ph')"
            type="text"
          >
          <button class="wx-solitaire-btn" type="button" @click="submit">
            {{ joined ? t('solitaire.update') : t('solitaire.join') }}
          </button>
        </div>

        <div class="wx-solitaire-foot">
          <button v-if="joined" class="wx-solitaire-link" type="button" @click="cancel">{{ t('solitaire.leave') }}</button>
          <span v-if="flashText" class="text-caption" :class="flashOk ? 'text-success' : 'text-error'">{{ flashText }}</span>
          <v-spacer />
          <button v-if="canClose" class="wx-solitaire-link" type="button" @click="close">{{ t('solitaire.close') }}</button>
        </div>
      </template>
    </template>
  </div>
</template>

<script setup lang="ts">
  import { computed, ref, watch } from 'vue'

  import { t } from '@/lib/i18n'
  import { useAuthStore } from '@/stores/auth'
  import { useSolitairesStore } from '@/stores/solitaires'

  const props = defineProps<{ conversationId: string, solitaireId: string }>()
  const store = useSolitairesStore()
  const auth = useAuthStore()

  const solitaire = computed(() => store.solitaireOf(props.conversationId, props.solitaireId))
  const joined = computed(() => !!solitaire.value?.entries.some(e => e.userId === auth.user?.id))
  const canClose = computed(() => {
    const s = solitaire.value
    if (!s || s.closed) return false
    return s.createdBy === auth.user?.id
  })

  const note = ref('')
  // 接龙数据到达 / 自己条目变化时，把已有备注回填到输入框（方便直接改）
  watch(solitaire, s => {
    const mine = s?.entries.find(e => e.userId === auth.user?.id)
    if (mine) note.value = mine.note
  }, { immediate: true })

  const flashText = ref('')
  const flashOk = ref(false)
  let flashTimer: ReturnType<typeof setTimeout> | null = null
  function flash (text: string, ok = false) {
    flashText.value = text
    flashOk.value = ok
    if (flashTimer) clearTimeout(flashTimer)
    flashTimer = setTimeout(() => { flashText.value = '' }, 2600)
  }

  async function submit () {
    if (!solitaire.value || solitaire.value.closed) return
    try {
      await store.join(props.conversationId, solitaire.value.id, note.value.trim())
      flash(joined.value ? t('solitaire.updated') : t('solitaire.joined'), true)
    } catch (error: any) {
      flash(error?.response?.data?.error ?? t('solitaire.fail'))
    }
  }

  async function cancel () {
    if (!solitaire.value) return
    try {
      await store.leave(props.conversationId, solitaire.value.id)
      note.value = ''
      flash(t('solitaire.left'), true)
    } catch (error: any) {
      flash(error?.response?.data?.error ?? t('solitaire.fail'))
    }
  }

  async function close () {
    if (!solitaire.value) return
    try {
      await store.close(props.conversationId, solitaire.value.id)
      flash(t('solitaire.closedDone'), true)
    } catch (error: any) {
      flash(error?.response?.data?.error ?? t('solitaire.fail'))
    }
  }
</script>

<style scoped>
.wx-solitaire-card {
  width: min(78vw, 340px);
  padding: 12px 14px;
  border-radius: 10px;
  background: var(--v-theme-surface);
  border: 1px solid rgba(0, 0, 0, 0.06);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

html[data-dm-mode='dark'] .wx-solitaire-card {
  border-color: rgba(255, 255, 255, 0.1);
  box-shadow: none;
}

.wx-solitaire-title {
  font-size: 15px;
  font-weight: 600;
  line-height: 1.35;
  word-break: break-word;
}

.wx-solitaire-note {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.5;
  color: rgba(0, 0, 0, 0.55);
  white-space: pre-wrap;
  word-break: break-word;
}

html[data-dm-mode='dark'] .wx-solitaire-note { color: rgba(255, 255, 255, 0.6); }

.wx-solitaire-count {
  margin-top: 6px;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.45);
}

html[data-dm-mode='dark'] .wx-solitaire-count { color: rgba(255, 255, 255, 0.5); }

.wx-solitaire-list {
  margin: 6px 0 0;
  padding: 0;
  list-style: none;
  max-height: 220px;
  overflow-y: auto;
}

.wx-solitaire-item {
  display: flex;
  align-items: baseline;
  gap: 6px;
  padding: 4px 0;
  font-size: 13px;
  line-height: 1.45;
  border-bottom: 1px solid rgba(0, 0, 0, 0.04);
}

html[data-dm-mode='dark'] .wx-solitaire-item { border-bottom-color: rgba(255, 255, 255, 0.06); }

.wx-solitaire-no {
  flex: 0 0 auto;
  min-width: 20px;
  color: rgba(0, 0, 0, 0.4);
  font-variant-numeric: tabular-nums;
}

html[data-dm-mode='dark'] .wx-solitaire-no { color: rgba(255, 255, 255, 0.45); }

.wx-solitaire-name { font-weight: 500; }
.wx-solitaire-name.is-me { color: rgb(var(--v-theme-primary)); }

.wx-solitaire-entry-note {
  color: rgba(0, 0, 0, 0.5);
  font-size: 12px;
  word-break: break-word;
}

html[data-dm-mode='dark'] .wx-solitaire-entry-note { color: rgba(255, 255, 255, 0.55); }

.wx-solitaire-empty {
  font-size: 12px;
  color: rgba(0, 0, 0, 0.4);
  padding: 6px 0;
}

.wx-solitaire-join {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 10px;
}

.wx-solitaire-input {
  flex: 1 1 auto;
  min-width: 0;
  height: 32px;
  padding: 0 10px;
  font-size: 13px;
  color: inherit;
  background: rgba(0, 0, 0, 0.04);
  border: 1px solid transparent;
  border-radius: 7px;
  outline: none;
}

html[data-dm-mode='dark'] .wx-solitaire-input { background: rgba(255, 255, 255, 0.08); }

.wx-solitaire-input:focus { border-color: rgb(var(--v-theme-primary)); }

.wx-solitaire-btn {
  flex: 0 0 auto;
  height: 32px;
  padding: 0 14px;
  font-size: 13px;
  font-weight: 500;
  color: #fff;
  background: rgb(var(--v-theme-primary));
  border: none;
  border-radius: 7px;
  cursor: pointer;
}

.wx-solitaire-btn:active { opacity: 0.85; }

.wx-solitaire-foot {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 6px;
  min-height: 20px;
}

.wx-solitaire-link {
  padding: 0;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.45);
  background: none;
  border: none;
  cursor: pointer;
}

.wx-solitaire-link:hover { color: rgb(var(--v-theme-primary)); }

html[data-dm-mode='dark'] .wx-solitaire-link { color: rgba(255, 255, 255, 0.5); }
</style>
