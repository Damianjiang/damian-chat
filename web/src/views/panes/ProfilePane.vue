<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <div class="wx-pane-bar wx-chat-bar">
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="smartBack({ name: 'me' })" />
      <v-toolbar-title class="text-body-large font-weight-medium">{{ t('settings.profile') }}</v-toolbar-title>
    </div>

    <div class="wx-profile-bg flex-grow-1 overflow-y-auto wx-scroll">
      <!-- 头像 + 昵称 + 大面号 -->
      <div class="wx-profile-head">
        <button class="wx-avatar-upload" :title="t('profile.avatar.upload.tip')" type="button" @click="openAvatarDialog">
          <user-avatar :nickname="auth.user?.nickname" :size="72" :src="auth.user?.avatar" />

          <span class="wx-avatar-mask">
            <v-icon icon="mdi-link-variant" size="20" />
          </span>
        </button>

        <div class="flex-grow-1" style="min-width: 0">
          <div class="text-headline-small font-weight-medium text-truncate">{{ auth.user?.nickname }}</div>
          <div class="text-body-small text-medium-emphasis">{{ t('me.wxid') }}：{{ auth.user?.wxId || '-' }}</div>

          <div v-if="auth.user?.signature" class="text-body-small text-medium-emphasis text-truncate">
            {{ auth.user.signature }}
          </div>
        </div>

        <v-btn
          icon="mdi-qrcode"
          size="small"
          :title="t('profile.myQr')"
          variant="text"
          @click="qrDialog = true"
        />
      </div>

      <!-- 资料项 -->
      <v-list bg-color="surface" class="wx-profile-list">
        <v-list-item @click="nameDialog = true">
          <v-list-item-title>{{ t('profile.name') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ auth.user?.nickname }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="genderDialog = true">
          <v-list-item-title>{{ t('me.gender') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ genderText }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="regionDialog = true">
          <v-list-item-title>{{ t('me.region') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ auth.user?.region || t('profile.unset') }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <!-- 账号：注册账号（本系统没有手机号概念），不可修改，去掉箭头避免误导 -->
        <v-list-item>
          <v-list-item-title>{{ t('me.phone') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ maskedPhone }}</span>
          </template>
        </v-list-item>

        <!-- 邮箱：绑定/换绑在「设置 → 账号」里做 -->
        <v-list-item v-if="auth.user?.email">
          <v-list-item-title>{{ t('account.email') }}</v-list-item-title>
          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1 text-truncate" style="max-width: 60vw">{{ auth.user.email }}</span>
          </template>
        </v-list-item>

        <v-list-item @click="qrDialog = true">
          <v-list-item-title>{{ t('profile.myQr') }}</v-list-item-title>

          <template #append>
            <v-icon icon="mdi-qrcode" size="small" />
            <v-icon class="ml-1" icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="signatureDialog = true">
          <v-list-item-title>{{ t('profile.signature') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1 text-truncate" style="max-width: 220px">
              {{ auth.user?.signature || t('profile.unset') }}
            </span>

            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

      </v-list>

      <div class="pa-4" />
    </div>

    <!-- 改名 -->
    <v-dialog v-model="nameDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">{{ t('profile.name.edit') }}</v-card-title>

        <v-card-text>
          <v-text-field
            v-model="nameDraft"
            autofocus
            counter="20"
            hide-details="auto"
            maxlength="20"
            :placeholder="t('profile.name.hint')"
            variant="plain"
          />

          <div class="text-body-small text-medium-emphasis">{{ t('profile.name.hint') }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" :disabled="!nameValid" variant="text" @click="saveName">{{ t('common.save') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 设置性别 -->
    <v-dialog v-model="genderDialog" max-width="320">
      <v-card>
        <v-card-title class="text-body-large">{{ t('profile.gender.set') }}</v-card-title>

        <v-list density="compact">
          <v-list-item v-for="g in genderOptions" :key="g.value" @click="pickGender(g.value)">
            <v-list-item-title>{{ g.title }}</v-list-item-title>

            <template #append>
              <v-icon v-if="auth.user?.gender === g.value" color="primary" icon="mdi-check" />
            </template>
          </v-list-item>
        </v-list>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="genderDialog = false">{{ t('profile.done') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 地区 -->
    <v-dialog v-model="regionDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">{{ t('profile.region.set') }}</v-card-title>

        <v-card-text>
          <v-select
            v-model="regionDraft"
            :items="regions"
            :label="t('profile.region.label')"
            variant="plain"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="saveRegion">{{ t('profile.done') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 个性签名 -->
    <v-dialog v-model="signatureDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">{{ t('me.signature') }}</v-card-title>

        <v-card-text>
          <v-textarea
            v-model="signatureDraft"
            autofocus
            counter="30"
            hide-details="auto"
            maxlength="30"
            :placeholder="t('profile.signature')"
            rows="2"
            variant="plain"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="saveSignature">{{ t('profile.done') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 我的二维码 -->
    <v-dialog v-model="qrDialog" max-width="340">
      <v-card class="text-center pa-4">
        <div class="text-title-large mb-3">{{ t('profile.myQr') }}</div>

        <user-avatar :nickname="auth.user?.nickname" :size="64" :src="auth.user?.avatar" />

        <div class="font-weight-medium mb-3">{{ auth.user?.nickname }}</div>

        <div class="d-inline-block pa-3 rounded-lg" style="background: #fff">
          <img v-if="myQr" :alt="t('profile.myQr')" :src="myQr" style="width: 200px; height: 200px">
          <v-progress-circular v-else color="primary" indeterminate />
        </div>

        <div class="text-body-small text-medium-emphasis mt-3">
          {{ t('profile.qr.hint') }}
        </div>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="qrDialog = false">{{ t('common.close') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 头像：选图片上传（服务端存 fileId，渲染走带鉴权 blob；不再接受外部链接） -->
    <v-dialog v-model="avatarDialog" max-width="400">
      <v-card>
        <v-card-title class="text-body-large">{{ t('profile.avatar.edit') }}</v-card-title>

        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-3">
            {{ t('profile.avatar.hint') }}
          </div>

          <div class="d-flex align-center gap-4">
            <user-avatar
              v-if="avatarPreview"
              :nickname="auth.user?.nickname"
              :size="72"
              :src="avatarPreview"
            />

            <div v-else class="text-body-small text-medium-emphasis">
              {{ avatarBusy ? t('profile.avatar.uploading') : t('profile.avatar.pick') }}
            </div>
          </div>

          <v-btn
            block
            class="mt-4"
            color="primary"
            :loading="avatarBusy"
            prepend-icon="mdi-camera"
            variant="flat"
            @click="pickAvatarFile"
          >
            {{ t('profile.avatar.choose') }}
          </v-btn>

          <input
            ref="avatarInput"
            accept="image/*"
            class="d-none"
            type="file"
            @change="onAvatarFile"
          >
        </v-card-text>

        <v-card-actions>
          <v-btn v-if="!!auth.user?.avatar" variant="text" @click="clearAvatar">{{ t('profile.avatar.clear') }}</v-btn>
          <v-spacer />
          <v-btn variant="text" @click="avatarDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!avatarUploaded || avatarBusy" @click="saveAvatar">{{ t('common.save') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar v-model="snack" :timeout="1800">{{ snackMsg }}</v-snackbar>

    <!-- 人机验证：改昵称/签名/地区/性别/头像等身份字段时，服务端要求一次性 passToken -->
    <slider-captcha ref="sliderRef" @success="onSliderPass" @close="onSliderClose" />
  </v-sheet>
</template>

<script setup lang="ts">
  import QRCode from 'qrcode'
  import { computed, onBeforeUnmount, ref, watch } from 'vue'
  import { useRouter } from 'vue-router'
  import type { User } from '@/types'
  import UserAvatar from '@/components/UserAvatar.vue'
  import SliderCaptcha from '@/components/SliderCaptcha.vue'
  import { api } from '@/lib/api'
  import { t } from '@/lib/i18n'
  import { isPageMode } from '@/lib/device'
  import { useSmartBack } from '@/lib/nav'
  import { fileUrl } from '@/lib/files'
  import { compressImage } from '@/lib/imgCompress'
  import { avatarColor } from '@/lib/utils'
  import { useAuthStore } from '@/stores/auth'

  const router = useRouter()
  const smartBack = useSmartBack()
  const auth = useAuthStore()

  /** 滑块组件句柄（open() 调起） */
  const sliderRef = ref<{ open: () => void } | null>(null)
  /** 等服务端通过人机验证后重放的资料写入（payload + 成功提示） */
  let pendingProfileWrite: { payload: Record<string, unknown>, msg: string } | null = null

  const isMobile = isPageMode
  const meId = computed(() => auth.user?.id ?? 'me')

  const genderText = computed(() => {
    const g = auth.user?.gender
    return g === 'male' ? t('profile.gender.male') : (g === 'female' ? t('profile.gender.female') : t('profile.unset'))
  })

  const maskedPhone = computed(() => {
    const p = auth.user?.phone ?? ''
    return p.length >= 11 ? `${p.slice(0, 3)}****${p.slice(7)}` : p
  })

  const snack = ref(false)
  const snackMsg = ref('')
  function toast (msg: string) {
    snackMsg.value = msg
    snack.value = true
  }

  const nameDialog = ref(false)
  const genderDialog = ref(false)
  const regionDialog = ref(false)
  const signatureDialog = ref(false)
  const qrDialog = ref(false)

  const nameDraft = ref('')
  const regionDraft = ref('')
  const signatureDraft = ref('')

  const nameValid = computed(() => nameDraft.value.trim().length > 0 && nameDraft.value.trim() !== auth.user?.nickname)

  const genderOptions = computed(() => [
    { title: t('profile.gender.male'), value: 'male' as const },
    { title: t('profile.gender.female'), value: 'female' as const },
  ])

  const regions = computed(() => [
    t('profile.region.cn'), t('profile.region.hk'), t('profile.region.mo'), t('profile.region.tw'),
    t('profile.region.sg'), t('profile.region.us'), t('profile.region.jp'),
  ])

  watch(nameDialog, v => {
    if (v) nameDraft.value = auth.user?.nickname ?? ''
  })
  watch(regionDialog, v => {
    if (v) regionDraft.value = auth.user?.region || t('profile.region.cn')
  })
  watch(signatureDialog, v => {
    if (v) signatureDraft.value = auth.user?.signature ?? ''
  })
  /* ---------------- 头像（选图片上传，服务端存 fileId，渲染走带鉴权 blob） ---------------- */
  const avatarDialog = ref(false)
  const avatarPreview = ref('')
  const avatarBusy = ref(false)
  const avatarInput = ref<HTMLInputElement | null>(null)
  /** 已上传成功的 fileId（保存时提交的就是它，不是本地 objectURL） */
  const avatarUploaded = ref('')
  let avatarObjectUrl = ''

  /** 选图触发隐藏 <input type=file>（模板里 ref） */
  function pickAvatarFile () {
    avatarInput.value?.click()
  }

  /**
   * 选图后立即上传（走 /files/upload?kind=avatar，服务端魔数 + 杀毒 + 5MB 上限），
   * 拿回 fileId 作为待提交的 avatar 值；本地 objectURL 做预览（不入 IDB，卸载即 revoke）。
   * 上传成功 → 预览切成「服务端 fileId」(带鉴权 blob)；失败 → 清掉待提交值并提示，阻止保存。
   */
  async function onAvatarFile (e: Event) {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = ''
    if (!file) return

    // 本地预判：类型 + 大小（与后端默认 10MB 上限一致，避免白传大文件；
    // 后端 files-config.json 的 MaxAvatarBytes 管理端可调，前端这里是上传前的快筛）
    if (!file.type.startsWith('image/')) {
      toast(t('profile.avatar.notImage'))
      return
    }
    if (file.size > 10 * 1024 * 1024) {
      toast(t('profile.avatar.tooBig'))
      return
    }

    // 头像本地压缩：长边 512 / 质量 0.85 —— 比 256 更清晰（看得清，不糊），
    // 又仍足够小（512px JPEG 通常 60~120KB），加载最快。相机 12MP 原图不再全尺寸入库。
    const upload = await compressImage(file, 512, 0.85)
    // 先清理上一份预览
    if (avatarObjectUrl) {
      URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = ''
    }
    avatarObjectUrl = URL.createObjectURL(upload)
    avatarPreview.value = avatarObjectUrl

    avatarBusy.value = true
    try {
      const form = new FormData()
      form.append('file', upload)
      form.append('kind', 'avatar')
      const res = await api.post('/files/upload', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      const id = (res.data as { id?: string })?.id
      if (!id) throw new Error('上传未返回 fileId')
      // 上传成功：预览切成「服务端 fileId」（走带鉴权 blob，离线/刷新后可用）
      avatarUploaded.value = id
      const blob = await fileUrl(id).catch(() => '')
      if (blob) {
        if (avatarObjectUrl) {
          URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = ''
        }
        avatarPreview.value = blob
      }
    } catch (error: any) {
      avatarUploaded.value = ''
      toast(error?.response?.data?.error ?? t('profile.avatar.fail'))
    } finally {
      avatarBusy.value = false
    }
  }

  function openAvatarDialog () {
    avatarPreview.value = ''
    avatarUploaded.value = ''
    if (avatarObjectUrl) {
      URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = ''
    }
    avatarDialog.value = true
  }

  async function saveAvatar () {
    if (!avatarUploaded.value || avatarBusy.value) return
    await patch({ avatar: avatarUploaded.value }, t('profile.avatar.updated'))
    avatarDialog.value = false
    resetAvatarPicker()
  }

  async function clearAvatar () {
    await patch({ avatar: '' }, t('profile.avatar.cleared'))
    avatarDialog.value = false
    resetAvatarPicker()
  }

  function resetAvatarPicker () {
    if (avatarObjectUrl) {
      URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = ''
    }
    avatarUploaded.value = ''
    avatarPreview.value = ''
  }

  onBeforeUnmount(() => {
    if (avatarObjectUrl) URL.revokeObjectURL(avatarObjectUrl)
  })

  async function patch (payload: Record<string, unknown>, msg: string) {
    try {
      const res = await api.put('/users/me', payload)
      applyProfile(res.data as Partial<User>, msg)
    } catch (error: any) {
      // 服务端要求人机验证（改昵称/签名/头像等身份字段）：弹滑块，通过后带一次性 passToken 重试。
      // 安全由服务端强制 —— 这里只是把验证界面呈现出来，删掉这段 JS 也绕不过去。
      if (error?.response?.data?.needCaptcha) {
        pendingProfileWrite = { payload, msg }
        sliderRef.value?.open()
        return
      }
      toast(error?.response?.data?.error ?? t('profile.save.fail'))
    }
  }

  /** 滑块通过 → 带着 passToken 重放刚才那次资料写入（passToken 一次性、绑 IP、90 秒有效）。 */
  function onSliderPass (passToken: string) {
    const job = pendingProfileWrite
    pendingProfileWrite = null
    if (!job) return
    void (async () => {
      try {
        const res = await api.put('/users/me', { ...job.payload, passToken })
        applyProfile(res.data as Partial<User>, job.msg)
      } catch (error: any) {
        toast(error?.response?.data?.error ?? t('profile.save.fail'))
      }
    })()
  }

  function onSliderClose () {
    pendingProfileWrite = null
  }

  /** 写入成功后合并本地登录态（不再整体替换，避免丢 id 等身份字段）。 */
  function applyProfile (data: Partial<User>, msg: string) {
    if (auth.user) {
      auth.user = {
        ...auth.user,
        ...data,
        id: (auth.user as User).id, // 身份恒定，绝不被响应覆盖丢失
      } as User
    } else {
      auth.user = data as User
    }
    toast(msg)
  }

  function saveName () {
    const name = nameDraft.value.trim()
    if (!name) return
    nameDialog.value = false
    void patch({ nickname: name }, t('profile.name.updated'))
  }

  function pickGender (value: 'male' | 'female') {
    void patch({ gender: value }, t('profile.gender.updated'))
  }

  function saveRegion () {
    regionDialog.value = false
    void patch({ region: regionDraft.value }, t('profile.region.updated'))
  }

  function saveSignature () {
    signatureDialog.value = false
    void patch({ signature: signatureDraft.value.trim() }, t('profile.signature.updated'))
  }

  /* 二维码名片：内容 wxapp://user/{id}，扫一扫页据此拉取资料并加好友 */
  const myQr = ref('')
  watch(qrDialog, v => {
    if (!v) return
    myQr.value = ''
    QRCode.toDataURL(`wxapp://user/${auth.user?.id}`, { width: 200, margin: 1 })
      .then(url => {
        myQr.value = url
      })
      .catch(() => { /* 保持 loading */ })
  })
</script>

<style scoped>
.wx-profile-bg {
  /* 跟随主题：亮色浅灰 / 暗色深底（此前写死 #ededed，暗色下整块刺眼） */
  background: rgb(var(--v-theme-wechat-chat-bg));
}

/* 顶部资料卡：白色圆角块 */
.wx-profile-head {
  display: flex;
  align-items: center;
  gap: 16px;
  margin: 12px;
  padding: 18px 16px;
  background: rgb(var(--v-theme-surface));
  border-radius: 10px;
  min-width: 0;
}

.wx-avatar-upload {
  position: relative;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  border-radius: 8px;
}

.wx-avatar-mask {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  background: rgba(0, 0, 0, 0.42);
  color: #fff;
  opacity: 0;
  transition: opacity 0.15s;
}

.wx-avatar-upload:hover .wx-avatar-mask {
  opacity: 1;
}

.wx-upload-bar {
  margin: 0 12px 8px;
  padding: 8px 12px;
  background: rgb(var(--v-theme-surface));
  border-radius: 8px;
}

.wx-profile-list {
  margin: 0 12px;
  border-radius: 10px;
  overflow: hidden;
}
</style>
