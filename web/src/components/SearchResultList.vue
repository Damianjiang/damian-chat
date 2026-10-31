<template>
  <div class="wx-result-list">
    <v-list v-if="items.length > 0" class="pa-2" lines="two">
      <v-list-item
        v-for="it in items"
        :key="it.id"
        class="wx-session-item"
        rounded="lg"
        @click="emit('select', it)"
      >
        <template #prepend>
          <v-avatar :color="avatarColor(it.id)" :size="40">
            <span>{{ (it.title || '?').slice(0, 1) }}</span>
          </v-avatar>
        </template>

        <template #title>
          <div class="d-flex flex-column">
            <span class="text-body-large">{{ it.title }}</span>
            <span class="text-body-small text-medium-emphasis">{{ it.subtitle }}</span>
          </div>
        </template>

        <template #append>
          <v-icon v-if="it.unread" color="wechat-unread" icon="mdi-circle-small" />
        </template>
      </v-list-item>
    </v-list>

    <v-empty-state v-else class="pa-8" icon="mdi-magnify" :title="t('common.no.result')" />
  </div>
</template>

<script setup lang="ts">
  import { t } from '@/lib/i18n'
  import { avatarColor } from '@/lib/utils'

  export interface ResultItem {
    id: string
    title: string
    subtitle?: string
    unread?: boolean
    payload?: any
  }

  defineProps<{ items: ResultItem[] }>()
  const emit = defineEmits<{ select: [item: ResultItem] }>()
</script>
