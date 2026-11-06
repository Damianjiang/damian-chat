<template>
  <!--
    全局「发现新版本」弹窗：打开 webapp 检测到服务端有新构建时出现。
    三个选择：立即更新 / 稍后 / 忽略（忽略只针对这一个版本）。
    与设置页共用 lib/updater 的状态，任何入口触发都只有这一个弹窗，不会重复弹。
  -->
  <v-dialog :model-value="showUpdatePrompt" max-width="400" persistent>
    <v-card class="wx-update-card">
      <v-card-title class="text-body-large d-flex align-center">
        <v-icon class="mr-2" color="primary" icon="mdi-update" size="22" />
        {{ t('update.title') }}
      </v-card-title>

      <v-card-text>
        <div class="wx-update-versions">
          <div class="wx-update-row">
            <span class="text-medium-emphasis">{{ t('update.current') }}</span>
            <span class="wx-update-ver">{{ formatBuild(updateState.running) }}</span>
          </div>
          <div class="wx-update-row">
            <span class="text-medium-emphasis">{{ t('update.latest') }}</span>
            <span class="wx-update-ver is-new">{{ formatBuild(updateState.latest) }}</span>
          </div>
        </div>

        <div class="text-body-small text-medium-emphasis mt-3">{{ t('update.hint') }}</div>

        <template v-if="updateState.applying">
          <v-progress-linear class="mt-3" color="primary" height="6" :model-value="updateState.progress" rounded />
          <div class="text-body-small text-primary mt-2">{{ t('update.applying') }}</div>
        </template>
      </v-card-text>

      <v-card-actions>
        <v-btn :disabled="updateState.applying" variant="text" @click="onIgnore">{{ t('update.ignore') }}</v-btn>
        <v-btn :disabled="updateState.applying" variant="text" @click="onSnooze">{{ t('update.later') }}</v-btn>
        <v-spacer />
        <v-btn color="primary" variant="flat" :loading="updateState.applying" @click="onApply">{{ t('update.apply') }}</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
  import { t } from '@/lib/i18n'
  import { applyUpdate, formatBuild, ignoreUpdate, showUpdatePrompt, snoozeUpdate, updateState } from '@/lib/updater'

  function onApply () {
    void applyUpdate()
  }

  function onIgnore () {
    ignoreUpdate()
  }

  function onSnooze () {
    snoozeUpdate()
  }
</script>

<style scoped>
.wx-update-card {
  border-radius: 14px;
}

.wx-update-versions {
  background: rgba(var(--v-theme-on-surface), 0.04);
  border-radius: 10px;
  padding: 10px 12px;
}

.wx-update-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
  line-height: 22px;
}

.wx-update-ver {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  opacity: 0.85;
}

.wx-update-ver.is-new {
  color: rgb(var(--v-theme-primary));
  opacity: 1;
  font-weight: 600;
}
</style>
