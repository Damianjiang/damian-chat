<template>
  <v-sheet class="wx-panes" flat>
    <div class="wx-pane-header pa-2">
      <v-text-field
        v-model="q"
        autofocus
        clearable
        density="compact"
        :placeholder="t('common.search')"
        prepend-inner-icon="mdi-magnify"
        variant="solo"
        @keyup.enter="doSearch"
      />
    </div>

    <div class="flex-grow-1 overflow-y-auto wx-scroll">
      <!-- 站内搜索：联系人 / 会话 / 朋友圈 -->
      <template v-if="searched">
        <v-subheader class="pa-2">{{ t('search.contacts') }}</v-subheader>
        <search-result-list :items="localContacts" @select="onSelectContact" />
        <v-subheader class="pa-2">{{ t('search.sessions') }}</v-subheader>
        <search-result-list :items="localConvs" @select="onSelectConv" />
        <v-subheader class="pa-2">{{ t('nav.moments') }}</v-subheader>
        <search-result-list :items="localMoments" @select="onSelectMoment" />
      </template>

      <v-empty-state v-else class="pa-8" icon="mdi-magnify" :title="t('search.hint')" />
    </div>
  </v-sheet>
</template>

<script setup lang="ts">
  import { computed, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import SearchResultList, { type ResultItem } from '@/components/SearchResultList.vue'
  import { t } from '@/lib/i18n'
  import { useContactsStore } from '@/stores/contacts'
  import { useConversationsStore } from '@/stores/conversations'
  import { useMomentsStore } from '@/stores/moments'

  const router = useRouter()
  const contacts = useContactsStore()
  const moments = useMomentsStore()
  const convs = useConversationsStore()

  const q = ref('')
  const searched = ref(false)

  const localContacts = computed<ResultItem[]>(() =>
    contacts.items
      .filter(c => match(c.user?.nickname, c.user?.phone ?? '', c.remark ?? ''))
      .map(c => ({ id: c.id, title: c.remark || c.user?.nickname || '', subtitle: c.user?.phone })),
  )

  const localMoments = computed<ResultItem[]>(() =>
    moments.items
      .filter(m => match(m.text, (m.author?.nickname ?? '')))
      .map(m => ({ id: m.id, title: m.text || t('search.momentImage'), subtitle: m.author?.nickname })),
  )

  const localConvs = computed<ResultItem[]>(() =>
    convs.sessions
      .filter(s => match(s.title))
      .map(s => ({ id: s.id, title: s.title, subtitle: s.kind === 'group' ? t('search.kindGroup') : t('search.sessions') })),
  )

  function match (...fields: (string | undefined)[]) {
    const k = q.value.trim().toLowerCase()
    if (!k) return false
    return fields.some(f => f?.toLowerCase().includes(k))
  }

  async function doSearch () {
    searched.value = true
    if (convs.sessions.length === 0) await convs.loadSessions().catch(() => {})
    // 只做「补齐」，绝不带关键词 load：contacts.load(kw) 会把 store.items 替换成
    // 搜索结果，退出搜索后通讯录页只剩上次搜过的子集（ContactsPane 的已知坑）。
    await Promise.allSettled([contacts.load(), moments.load(false)])
  }

  function onSelectConv (item: ResultItem) {
    router.push({ name: 'chat-detail', params: { id: item.id } })
  }

  function onSelectMoment (item: ResultItem) {
    router.push({ name: 'moment-detail', params: { id: item.id } })
  }

  function onSelectContact (item: ResultItem) {
    const c = contacts.items.find(x => x.id === item.id)
    if (c?.user?.id) router.push({ name: 'contact-profile', params: { id: c.user.id } })
  }

</script>
