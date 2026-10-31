<template>
  <div class="wx-gate">
    <div class="wx-gate-inner">
      <!-- 上半部分：Vuetify 组件。浏览器太旧渲染不出这些组件时，这里是空白/错版 -->
      <v-card class="wx-gate-card" flat>
        <div class="d-flex align-center gap-4 pa-4">
          <v-avatar color="primary" size="64">
            <v-icon color="white" icon="mdi-chat" size="34" />
          </v-avatar>

          <div class="d-flex flex-column">
            <span class="text-headline-small font-weight-medium">{{ t('login.brand') }}</span>
            <span class="text-body-small text-medium-emphasis">{{ t('bg.brand.sub') }}</span>
          </div>
        </div>

        <v-divider />

        <div class="pa-4 d-flex flex-wrap gap-2">
          <v-chip color="primary" variant="flat">{{ t('nav.chat') }}</v-chip>
          <v-chip color="success" variant="flat">{{ t('nav.contacts') }}</v-chip>
          <v-chip color="info" variant="flat">{{ t('nav.moments') }}</v-chip>
          <v-chip color="warning" variant="flat">{{ t('nav.discovery') }}</v-chip>
        </div>

        <v-divider />

        <div class="pa-4">
          <v-btn block color="primary" variant="flat">{{ t('gate.demo.btn') }}</v-btn>
        </div>
      </v-card>

      <!-- 下半部分：两个纯 HTML 按钮，旧版浏览器也能显示 -->
      <div class="wx-gate-question">{{ t('gate.question') }}</div>

      <div class="wx-gate-actions">
        <button class="wx-gate-btn wx-gate-btn--primary" type="button" @click="onSeen">
          {{ t('gate.seen') }}
        </button>

        <button class="wx-gate-btn" type="button" @click="onNotSeen">
          {{ t('gate.not') }}
        </button>
      </div>

      <!-- 看不到：版本过低（固定文案 + 四个浏览器下载） -->
      <div v-if="blocked" class="wx-gate-blocked">
        <h1>{{ t('gate.blocked') }}</h1>
        <p>{{ t('gate.upgrade') }}</p>
        <a href="https://www.google.com/chrome/" rel="noopener noreferrer" target="_blank">{{ t('gate.dl.chrome') }}</a>
        <a href="https://www.microsoft.com/edge" rel="noopener noreferrer" target="_blank">{{ t('gate.dl.edge') }}</a>
        <a href="https://www.mozilla.org/firefox/" rel="noopener noreferrer" target="_blank">{{ t('gate.dl.firefox') }}</a>
        <a href="https://www.apple.com/safari/" rel="noopener noreferrer" target="_blank">{{ t('gate.dl.safari') }}</a>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
  import { ref } from 'vue'
  import { t } from '@/lib/i18n'

  const emit = defineEmits<{ confirmed: [] }>()
  const blocked = ref(false)

  function onSeen () {
    try {
      localStorage.setItem('wx_browser_confirmed', '1')
    } catch {
      document.cookie = 'wx_browser_confirmed=1;path=/;max-age=31536000'
    }
    emit('confirmed')
  }

  function onNotSeen () {
    blocked.value = true
  }
</script>

<style scoped>
.wx-gate {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgb(var(--v-theme-background, #f5f5f5));
  padding: 16px;
}

.wx-gate-inner {
  width: 100%;
  max-width: 420px;
  text-align: center;
}

.wx-gate-card {
  text-align: left;
  border: 1px solid var(--v-theme-border, #e5e5e5);
}

.wx-gate-question {
  margin: 24px 0 16px;
  font-size: 15px;
  color: rgb(var(--v-theme-on-background, #181818));
}

.wx-gate-actions {
  display: flex;
  gap: 12px;
  justify-content: center;
  flex-wrap: wrap;
}

/* 纯 CSS 按钮：不依赖 Vuetify，旧版浏览器也能渲染 */
.wx-gate-btn {
  min-width: 160px;
  padding: 10px 18px;
  font-size: 15px;
  border-radius: 8px;
  border: 1px solid #e5e5e5;
  background: #ffffff;
  color: #181818;
  cursor: pointer;
}

.wx-gate-btn--primary {
  background: var(--dm-primary, #07C160);
  border-color: var(--dm-primary, #07C160);
  color: #ffffff;
}

.wx-gate-btn:hover {
  opacity: 0.9;
}

.wx-gate-blocked {
  margin-top: 28px;
  padding: 20px;
  border: 1px solid #e5e5e5;
  border-radius: 12px;
  background: #ffffff;
}

.wx-gate-blocked h1 {
  font-size: 17px;
  margin: 0 0 8px;
  color: #181818;
}

.wx-gate-blocked p {
  font-size: 14px;
  color: #888888;
  margin: 0 0 16px;
}

.wx-gate-blocked a {
  display: block;
  width: 220px;
  margin: 8px auto;
  padding: 9px 0;
  font-size: 14px;
  color: var(--dm-primary, #07C160);
  background: #f5f5f5;
  border-radius: 8px;
  text-decoration: none;
}
</style>
