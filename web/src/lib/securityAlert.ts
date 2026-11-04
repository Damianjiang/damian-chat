/**
 * lib/securityAlert.ts
 *
 * 账号安全提醒（防盗号）：新设备登录 / 换绑邮箱时服务端经 SignalR 推送
 * 「AccountSecurity」事件，realtime.ts 调 showSecurityAlert() 挂到这里，
 * App.vue 渲染全局弹窗。被盗号的第一发现人往往是被挤下线的本人。
 */
import { reactive } from 'vue'

export const securityAlertState = reactive({
  show: false,
  type: '' as 'new-device-login' | 'email-changed' | '',
  device: '',
  ip: '',
  email: '',
  time: '',
})

export function showSecurityAlert (payload: { type: string, device?: string, ip?: string, email?: string, time?: string }) {
  securityAlertState.show = true
  securityAlertState.type = (payload.type as typeof securityAlertState.type) ?? ''
  securityAlertState.device = payload.device ?? ''
  securityAlertState.ip = payload.ip ?? ''
  securityAlertState.email = payload.email ?? ''
  securityAlertState.time = payload.time ?? ''
}

export function dismissSecurityAlert () {
  securityAlertState.show = false
}
