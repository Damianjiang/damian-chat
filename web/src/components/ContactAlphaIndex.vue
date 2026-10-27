<template>
  <div class="wx-alpha-bar" @click="onBarClick">
    <span
      v-for="l in letters"
      :key="l"
      class="wx-alpha-item"
      :class="{ 'is-active': active === l }"
      @click.stop="scrollTo(l)"
    >
      {{ l }}
    </span>

    <span class="wx-alpha-item wx-alpha-top" @click.stop="scrollTop">^</span>
  </div>
</template>

<script setup lang="ts">
  import { computed, ref } from 'vue'

  const props = defineProps<{
    letters?: string[]
    container?: HTMLElement | null
  }>()

  const emit = defineEmits<{ 'scroll-to': [letter: string] }>()

  const active = ref('')
  const defaultLetters = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '#']
  const letters = computed(() => props.letters?.length ? props.letters : defaultLetters)

  function scrollTo (l: string) {
    active.value = l
    emit('scroll-to', l)
  }

  function scrollTop () {
    active.value = ''
    props.container?.scrollTo({ top: 0 })
  }

  function onBarClick () {
    active.value = ''
  }
</script>

<style scoped>
/* 右侧竖排字母索引：窄条贴右边，字号小、颜色淡，点击可跳转 */
.wx-alpha-bar {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1px;
  padding: 2px 1px;
  width: 16px;
  line-height: 1.1;
  font-size: 10px;
  line-height: 1.1;
  color: rgb(var(--v-theme-on-surface), 0.45);
  user-select: none;
  overflow: hidden;
}

.wx-alpha-item {
  cursor: pointer;
  padding: 0 2px;
  border-radius: 2px;
}

.wx-alpha-item.is-active,
.wx-alpha-item:hover {
  color: rgb(var(--v-theme-primary));
  background: rgb(var(--v-theme-on-surface), 0.08);
}

.wx-alpha-top {
  margin-top: 2px;
  color: rgb(var(--v-theme-primary));
}
</style>
