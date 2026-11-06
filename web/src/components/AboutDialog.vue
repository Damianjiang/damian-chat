<template>
  <!--
    关于弹窗（登录页版本号 / 设置页「关于」共用）。
    抽成组件的原因：之前只有设置页里的一份内联实现，登录页的版本号只是个死 chip ——
    用户想在登录前确认「这是哪个版本、有没有新构建、证书怎么装」根本点不开。
  -->
  <v-dialog :model-value="modelValue" max-width="400" @update:model-value="emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="text-body-large">{{ t('settings.about') }}</v-card-title>

      <v-card-text>
        <div class="wx-about-brand">
          <img alt="大面聊天" class="wx-about-logo" src="/icon-192.png">
          <div class="min-width-0">
            <div class="text-body-large font-weight-medium">{{ t('about.systemName') }}</div>
            <div class="text-body-small text-medium-emphasis">{{ t('about.desc') }}</div>
          </div>
        </div>

        <div class="wx-about-rows mt-3">
          <div class="wx-about-row">
            <span class="text-medium-emphasis">{{ t('settings.version') }}</span>
            <span class="wx-about-val">{{ appVersion }}</span>
          </div>
          <div class="wx-about-row">
            <span class="text-medium-emphasis">{{ t('about.build') }}</span>
            <span class="wx-about-val">{{ formatBuild(currentBuildVersion) }}</span>
          </div>
          <div class="wx-about-row">
            <span class="text-medium-emphasis">{{ t('about.device') }}</span>
            <span class="wx-about-val">{{ deviceLabel(deviceKind) }}</span>
          </div>
          <div class="wx-about-row">
            <span class="text-medium-emphasis">{{ t('about.stack') }}</span>
            <span class="wx-about-val">{{ t('settings.about.tech') }}</span>
          </div>
        </div>

        <div class="wx-about-links">
          <a class="wx-about-link" href="https://github.com/Damianjiang/damian-chat" rel="noopener noreferrer" target="_blank">
            <v-icon icon="mdi-github" size="16" />{{ t('about.github') }}
          </a>
          <a class="wx-about-link" href="https://github.com/Damianjiang/damian-chat/releases" rel="noopener noreferrer" target="_blank">
            <v-icon icon="mdi-tag-outline" size="16" />{{ t('about.releases') }}
          </a>
          <a class="wx-about-link" href="https://github.com/Damianjiang/damian-chat/issues" rel="noopener noreferrer" target="_blank">
            <v-icon icon="mdi-bug-outline" size="16" />{{ t('about.issues') }}
          </a>
          <!-- 手机/新设备首次访问会弹「连接不安全」：这里给出免警告的安装引导页 -->
          <a class="wx-about-link" href="/ca" rel="noopener noreferrer" target="_blank">
            <v-icon icon="mdi-certificate-outline" size="16" />{{ t('about.cert') }}
          </a>
        </div>

        <div class="text-body-small text-medium-emphasis mt-3">{{ t('about.license') }}</div>
        <div class="text-body-small text-medium-emphasis mt-1" style="word-break: break-all">{{ uaShort }}</div>
      </v-card-text>

      <v-card-actions>
        <v-spacer />
        <v-btn color="primary" variant="text" @click="emit('update:modelValue', false)">{{ t('common.close') }}</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
  import { deviceKind, deviceLabel } from '@/lib/device'
  import { t } from '@/lib/i18n'
  import { formatBuild, RUNNING_VERSION } from '@/lib/updater'
  import { APP_VERSION } from '@/lib/version'

  defineProps<{ modelValue: boolean }>()
  const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void }>()

  const appVersion = APP_VERSION
  const currentBuildVersion = RUNNING_VERSION
  const uaShort = navigator.userAgent
</script>

<style scoped>
.wx-about-brand {
  display: flex;
  align-items: center;
  gap: 10px;
}

.wx-about-logo {
  width: 44px;
  height: 44px;
  border-radius: 11px;
  flex: 0 0 auto;
}

.wx-about-rows {
  background: rgba(0, 0, 0, 0.035);
  border-radius: 10px;
  padding: 6px 12px;
}

html[data-dm-mode='dark'] .wx-about-rows { background: rgba(255, 255, 255, 0.07); }

.wx-about-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
  line-height: 26px;
}

.wx-about-val {
  text-align: right;
  word-break: break-all;
  font-weight: 500;
}

.wx-about-links {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}

.wx-about-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 5px 10px;
  border-radius: 8px;
  font-size: 12px;
  text-decoration: none;
  color: rgb(var(--v-theme-primary));
  background: rgba(var(--v-theme-primary), 0.08);
}

.wx-about-link:hover { background: rgba(var(--v-theme-primary), 0.16); }

.min-width-0 { min-width: 0; }
</style>
