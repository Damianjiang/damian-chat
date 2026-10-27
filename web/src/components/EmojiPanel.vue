<template>
  <v-sheet
    class="wx-emoji-panel"
    color="surface"
    rounded="lg"
    style="max-width: 90vw"
    width="300"
  >
    <div class="d-flex flex-wrap gap-0 p-2 wx-emoji-grid">
      <span
        v-for="s in ALL_EMOJIS"
        :key="s"
        class="wx-sticker-btn"
        @click="emit('pick', s)"
      >
        {{ s }}
      </span>
    </div>

    <div class="d-flex justify-end pa-1">
      <v-btn icon="mdi-close" size="x-small" variant="text" @click="emit('close')" />
    </div>
  </v-sheet>
</template>

<script setup lang="ts">
  import { ALL_EMOJIS } from '@/lib/emoji'

  const emit = defineEmits<{ pick: [emoji: string], close: [] }>()
</script>

<style scoped>
.wx-emoji-panel {
  position: absolute;
  bottom: 100%;
  left: 0;
  margin-bottom: 8px;
  z-index: 3;
}

.wx-sticker-btn {
  font-size: 26px;
  cursor: pointer;
  padding: 6px;
  border-radius: 6px;
}

.wx-sticker-btn:hover {
  background: rgba(0, 0, 0, 0.06);
}
</style>
