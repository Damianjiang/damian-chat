<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="router.back()" />
      <v-toolbar-title class="text-body-large font-weight-medium">个人资料</v-toolbar-title>
    </v-app-bar>

    <div class="wx-profile-bg flex-grow-1 overflow-y-auto wx-scroll">
      <!-- 头像 + 昵称 + 大面号 -->
      <div class="wx-profile-head">
        <button class="wx-avatar-upload" title="点击上传头像" type="button" @click="openAvatarDialog">
          <user-avatar :nickname="auth.user?.nickname" :size="72" :src="auth.user?.avatar" />

          <span class="wx-avatar-mask">
            <v-icon icon="mdi-link-variant" size="20" />
          </span>
        </button>

        <div class="flex-grow-1">
          <div class="text-headline-small font-weight-medium">{{ auth.user?.nickname }}</div>
          <div class="text-body-small text-medium-emphasis">大面号：{{ auth.user?.wxId || '-' }}</div>

          <div v-if="auth.user?.signature" class="text-body-small text-medium-emphasis text-truncate">
            {{ auth.user.signature }}
          </div>
        </div>

        <v-btn
          icon="mdi-qrcode"
          size="small"
          title="我的二维码"
          variant="text"
          @click="qrDialog = true"
        />
      </div>

      <!-- 资料项 -->
      <v-list bg-color="surface" class="wx-profile-list">
        <v-list-item @click="nameDialog = true">
          <v-list-item-title>名字</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ auth.user?.nickname }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="genderDialog = true">
          <v-list-item-title>性别</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ genderText }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="regionDialog = true">
          <v-list-item-title>地区</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ auth.user?.region || '未设置' }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <!-- 手机号：不可修改（账号安全绑定），去掉箭头避免「看起来能点其实不能」的误导 -->
        <v-list-item>
          <v-list-item-title>手机号</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ maskedPhone }}</span>
          </template>
        </v-list-item>

        <v-list-item @click="qrDialog = true">
          <v-list-item-title>我的二维码</v-list-item-title>

          <template #append>
            <v-icon icon="mdi-qrcode" size="small" />
            <v-icon class="ml-1" icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="signatureDialog = true">
          <v-list-item-title>签名</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1 text-truncate" style="max-width: 220px">
              {{ auth.user?.signature || '未设置' }}
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
        <v-card-title class="text-body-large">更改名字</v-card-title>

        <v-card-text>
          <v-text-field
            v-model="nameDraft"
            autofocus
            counter="20"
            hide-details="auto"
            maxlength="20"
            placeholder="好名字可以让你的朋友更容易记住你"
            variant="plain"
          />

          <div class="text-body-small text-medium-emphasis">好名字可以让你的朋友更容易记住你。</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" :disabled="!nameValid" variant="text" @click="saveName">保存</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 设置性别 -->
    <v-dialog v-model="genderDialog" max-width="320">
      <v-card>
        <v-card-title class="text-body-large">设置性别</v-card-title>

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
          <v-btn color="primary" variant="text" @click="genderDialog = false">完成</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 地区 -->
    <v-dialog v-model="regionDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">设置地区</v-card-title>

        <v-card-text>
          <v-select
            v-model="regionDraft"
            :items="regions"
            label="国家/地区"
            variant="plain"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="saveRegion">完成</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 个性签名 -->
    <v-dialog v-model="signatureDialog" max-width="340">
      <v-card>
        <v-card-title class="text-body-large">个性签名</v-card-title>

        <v-card-text>
          <v-textarea
            v-model="signatureDraft"
            autofocus
            counter="30"
            hide-details="auto"
            maxlength="30"
            placeholder="签名"
            rows="2"
            variant="plain"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="saveSignature">完成</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 我的二维码 -->
    <v-dialog v-model="qrDialog" max-width="340">
      <v-card class="text-center pa-4">
        <div class="text-title-large mb-3">我的二维码</div>

        <v-avatar class="mb-3" :color="avatarColor(meId)" size="64">
          <img v-if="auth.user?.avatar" alt="" :src="auth.user.avatar">
          <span v-else>{{ (auth.user?.nickname ?? '我').slice(0, 1) }}</span>
        </v-avatar>

        <div class="font-weight-medium mb-3">{{ auth.user?.nickname }}</div>

        <div class="d-inline-block pa-3 rounded-lg" style="background: #fff">
          <img v-if="myQr" alt="我的二维码" :src="myQr" style="width: 200px; height: 200px">
          <v-progress-circular v-else color="primary" indeterminate />
        </div>

        <div class="text-body-small text-medium-emphasis mt-3">
          扫一扫上面的二维码，打开名片并添加好友
        </div>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="qrDialog = false">关闭</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 头像：选图片上传（服务端存 fileId，渲染走带鉴权 blob；不再接受外部链接） -->
    <v-dialog v-model="avatarDialog" max-width="400">
      <v-card>
        <v-card-title class="text-body-large">修改头像</v-card-title>

        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-3">
            从本地选择一张图片（JPG/PNG/GIF/WEBP，≤5MB）。图片经服务端校验后保存，
            不再支持填写外部链接。
          </div>

          <div class="d-flex align-center gap-4">
            <user-avatar
              v-if="avatarPreview"
              :nickname="auth.user?.nickname"
              :size="72"
              :src="avatarPreview"
            />

            <div v-else class="text-body-small text-medium-emphasis">
              {{ avatarBusy ? '正在上传…' : '请选择图片' }}
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
            选择图片
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
          <v-btn v-if="!!auth.user?.avatar" variant="text" @click="clearAvatar">清空</v-btn>
          <v-spacer />
          <v-btn variant="text" @click="avatarDialog = false">取消</v-btn>
          <v-btn color="primary" :disabled="!avatarUploaded || avatarBusy" @click="saveAvatar">保存</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar v-model="snack" :timeout="1800">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import QRCode from 'qrcode'
  import { computed, onBeforeUnmount, ref, watch } from 'vue'
  import { useRouter } from 'vue-router'
  import type { User } from '@/types'
  import UserAvatar from '@/components/UserAvatar.vue'
  import { api } from '@/lib/api'
  import { isPageMode } from '@/lib/device'
  import { fileUrl } from '@/lib/files'
  import { avatarColor } from '@/lib/utils'
  import { useAuthStore } from '@/stores/auth'

  const router = useRouter()
  const auth = useAuthStore()

  const isMobile = isPageMode
  const meId = computed(() => auth.user?.id ?? 'me')

  const genderText = computed(() => {
    const g = auth.user?.gender
    return g === 'male' ? '男' : (g === 'female' ? '女' : '未设置')
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

  const genderOptions = [
    { title: '男', value: 'male' as const },
    { title: '女', value: 'female' as const },
  ]

  const regions = ['中国', '中国香港', '中国澳门', '中国台湾', '新加坡', '美国', '日本']

  watch(nameDialog, v => {
    if (v) nameDraft.value = auth.user?.nickname ?? ''
  })
  watch(regionDialog, v => {
    if (v) regionDraft.value = auth.user?.region || '中国'
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

    // 本地预判：类型 + 大小（与后端 5MB 上限一致，避免白传大文件）
    if (!file.type.startsWith('image/')) {
      toast('请选择图片文件')
      return
    }
    if (file.size > 5 * 1024 * 1024) {
      toast('头像图片不能超过 5MB')
      return
    }

    // 先清理上一份预览
    if (avatarObjectUrl) {
      URL.revokeObjectURL(avatarObjectUrl); avatarObjectUrl = ''
    }
    avatarObjectUrl = URL.createObjectURL(file)
    avatarPreview.value = avatarObjectUrl

    avatarBusy.value = true
    try {
      const form = new FormData()
      form.append('file', file)
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
      toast(error?.response?.data?.error ?? '头像上传失败，请重试')
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
    await patch({ avatar: avatarUploaded.value }, '头像已更新')
    avatarDialog.value = false
    resetAvatarPicker()
  }

  async function clearAvatar () {
    await patch({ avatar: '' }, '已清空头像')
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
      const data = res.data as Partial<User>
      // 合并更新而非整体替换：保留登录态的关键身份字段（尤其 id / 手机号），
      // 避免替换后某个字段缺失导致「我」的身份被界面层丢失、看起来像被登出。
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
    } catch {
      toast('保存失败，请重试')
    }
  }

  function saveName () {
    const name = nameDraft.value.trim()
    if (!name) return
    nameDialog.value = false
    void patch({ nickname: name }, '名字已更新')
  }

  function pickGender (value: 'male' | 'female') {
    void patch({ gender: value }, '性别已更新')
  }

  function saveRegion () {
    regionDialog.value = false
    void patch({ region: regionDraft.value }, '地区已更新')
  }

  function saveSignature () {
    signatureDialog.value = false
    void patch({ signature: signatureDraft.value.trim() }, '签名已更新')
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
