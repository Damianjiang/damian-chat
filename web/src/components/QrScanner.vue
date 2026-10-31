<template>
  <v-dialog max-width="420" :model-value="modelValue" persistent @update:model-value="emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="d-flex align-center">
        {{ t('scan.title') }}
        <v-spacer />
        <v-btn icon="mdi-close" size="small" variant="text" @click="close" />
      </v-card-title>

      <v-card-text>
        <div class="wx-scanner rounded-lg overflow-hidden">
          <video ref="videoEl" autoplay muted playsinline />
          <div class="wx-scanner-frame" />
        </div>

        <div class="text-body-small text-medium-emphasis mt-2 text-center">{{ hint }}</div>

        <!-- 扫到用户名片 -->
        <v-sheet v-if="scannedUser" border class="mt-3 pa-3 rounded">
          <div class="d-flex align-center">
            <v-avatar class="mr-3" color="primary" size="44">
              <v-icon v-if="!scannedUser.avatar" icon="mdi-account" />
              <v-img v-else :src="scannedUser.avatar" />
            </v-avatar>

            <div class="flex-grow-1">
              <div class="font-weight-medium">{{ scannedUser.nickname }}</div>
              <div class="text-body-small text-medium-emphasis">{{ t('scan.account', { s: scannedUser.phone }) }}</div>
            </div>

            <v-btn
              v-if="!isFriend && scannedUser.id !== auth.user?.id"
              color="primary"
              :loading="adding"
              size="small"
              @click="addScannedFriend"
            >
              {{ t('scan.add') }}
            </v-btn>

            <span v-else-if="scannedUser.id === auth.user?.id" class="text-body-small text-medium-emphasis">{{ t('scan.you') }}</span>
            <span v-else class="text-body-small text-medium-emphasis">{{ t('scan.friend') }}</span>
          </div>
        </v-sheet>
      </v-card-text>

      <v-card-actions>
        <v-spacer />
        <v-btn variant="text" @click="close">{{ t('scan.close') }}</v-btn>
      </v-card-actions>
    </v-card>

    <v-snackbar v-model="snack" :timeout="2200">{{ snackMsg }}</v-snackbar>
  </v-dialog>
</template>

<script setup lang="ts">
  import jsQR from 'jsqr'
  import { computed, onBeforeUnmount, ref, watch } from 'vue'

  import { api } from '@/lib/api'
  import { t } from '@/lib/i18n'
  import { useAuthStore } from '@/stores/auth'
  import { useContactsStore } from '@/stores/contacts'

  interface ScannedUser {
    id: string
    phone: string
    nickname: string
    avatar: string
    signature?: string
  }

  const props = defineProps<{ modelValue: boolean }>()
  const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

  const auth = useAuthStore()
  const contacts = useContactsStore()

  const videoEl = ref<HTMLVideoElement | null>(null)
  const hint = ref(t('scan.hint'))
  const snack = ref(false)
  const snackMsg = ref('')
  const scannedUser = ref<ScannedUser | null>(null)
  const adding = ref(false)

  const isFriend = computed(() =>
    !!scannedUser.value && contacts.items.some(c => c.user?.id === scannedUser.value?.id),
  )

  let stream: MediaStream | null = null
  let raf = 0
  let canvas: HTMLCanvasElement | null = null
  let busy = false

  function toast (msg: string) {
    snackMsg.value = msg
    snack.value = true
  }

  async function start () {
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment', width: { ideal: 1280 } },
        audio: false,
      })
      if (videoEl.value) {
        videoEl.value.srcObject = stream
        await videoEl.value.play().catch(() => {})
      }
      canvas = document.createElement('canvas')
      loop()
    } catch {
      hint.value = t('scan.cam.fail')
    }
  }

  function loop () {
    if (!stream) return
    const video = videoEl.value
    if (video && canvas && video.readyState === video.HAVE_ENOUGH_DATA && !busy) {
      busy = true
      try {
        canvas.width = video.videoWidth
        canvas.height = video.videoHeight
        const ctx = canvas.getContext('2d', { willReadFrequently: true })
        if (ctx) {
          ctx.drawImage(video, 0, 0, canvas.width, canvas.height)
          const img = ctx.getImageData(0, 0, canvas.width, canvas.height)
          const code = jsQR(img.data, img.width, img.height, { inversionAttempts: 'dontInvert' })
          if (code?.data) handleResult(code.data)
        }
      } finally {
        busy = false
      }
    }
    raf = requestAnimationFrame(loop)
  }

  async function handleResult (text: string) {
    // 识别成功即停：若重启扫描，同一张码仍在摄像头前会立即再次命中，
    // 造成 stop→api→start 死循环反复请求
    stop()
    // 用户名片
    const userMatch = /^wxapp:\/\/user\/(.+)$/.exec(text)
    if (userMatch) {
      try {
        const res = await api.get(`/users/${userMatch[1]}`)
        scannedUser.value = res.data
        hint.value = t('scan.card')
        // 保持停止状态展示资料卡；关闭/添加后由 close 收尾
      } catch {
        toast(t('scan.unknown'))
        close()
      }
      return
    }
    toast(t('scan.bad', { s: text.slice(0, 60) }))
    close()
  }

  async function addScannedFriend () {
    if (!scannedUser.value) return
    adding.value = true
    try {
      const res = await api.post('/contacts', {
        userId: scannedUser.value.id,
        message: t('scan.add.msg'),
      })
      toast(res.data.status === 'accepted' ? t('scan.added') : t('scan.pending'))
      await contacts.load()
      close()
    } catch (error: any) {
      toast(error?.response?.data?.error ?? t('scan.fail'))
    } finally {
      adding.value = false
    }
  }

  function stop () {
    cancelAnimationFrame(raf)
    raf = 0
    if (stream) {
      for (const t of stream.getTracks()) t.stop()
    }
    stream = null
    canvas = null
    busy = false
  }

  function close () {
    stop()
    scannedUser.value = null
    hint.value = t('scan.hint')
    emit('update:modelValue', false)
  }

  watch(() => props.modelValue, open => {
    if (open) start()
    else stop()
  })

  onBeforeUnmount(stop)
</script>

<style scoped>
.wx-scanner {
  position: relative;
  background: #000;
  aspect-ratio: 4 / 3;
}

.wx-scanner video {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.wx-scanner-frame {
  position: absolute;
  inset: 12%;
  border: 2px solid var(--dm-primary, #07C160);
  border-radius: 8px;
  box-shadow: 0 0 0 999px rgba(0, 0, 0, 0.35);
  pointer-events: none;
}
</style>
