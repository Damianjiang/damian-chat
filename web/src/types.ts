export type MessageType = 'text' | 'image' | 'voice' | 'file' | 'sticker' | 'quote' | 'system' | 'pat' | 'poll'

/** 群投票（服务端 Poll 模型镜像；options[].userIds 为投票人 id 列表）。 */
export interface Poll {
  id: string
  title: string
  options: { text: string, userIds: string[] }[]
  multi: boolean
  closed: boolean
  createdBy: string
  createdAt: string
}

export interface User {
  id: string
  phone: string
  nickname: string
  avatar: string
  wxId?: string
  signature: string
  region: string
  gender: 'unknown' | 'male' | 'female'
  remark?: string
  online?: boolean
  privacy?: Record<string, boolean>
  banned?: boolean
  isBot?: boolean
}

export interface Contact {
  id: string
  user: User
  firstLetter: string
  groups: string[]
  remark?: string
  blocked?: boolean
}

export interface Message {
  id: string
  conversationId: string
  senderId: string
  type: MessageType
  content: string
  imageId?: string
  voiceSeconds?: number
  fileName?: string
  fileSize?: number
  stickerEmoji?: string
  quote?: { senderName: string, text: string }
  quoteJson?: string
  /** 是否为「拍一拍」消息（双击头像触发，群聊/对方可见） */
  pat?: boolean
  /** 被拍的人（点头像拍的那位） */
  patTarget?: string
  recalled?: boolean
  createdAt: string
  /**
   * 乐观发送：客户端生成的稳定 key。
   * 服务端确认后这条消息会「就地替换」为真实消息但保留同一个 localKey，
   * v-for 用它做 key 就不会重新挂载 —— 否则气泡会把进场动画再播一遍（观感上"闪一下"）。
   */
  localKey?: string
  /** 发送中（乐观气泡，尚未被服务端确认） */
  pending?: boolean
  /** 发送失败，点击可重试 */
  failed?: boolean
}

export interface Conversation {
  id: string
  kind: 'private' | 'group'
  title: string
  avatar: string
  memberId: string
  lastMessage?: Message
  lastAt?: string
  unread: number
  pinned: boolean
  muted: boolean
  /** 会话是否被折叠（隐藏在主列表，集中在"折叠群聊"里显示，可恢复） */
  folded?: boolean
}

export interface Group {
  id: string
  name: string
  avatar: string
  notice: string
  ownerId: string
  members: GroupMember[]
  createdFromMe?: boolean
  /** 群公告「我」是否已确认（时间戳，0/undefined 表示未确认） */
  noticeReadByMe?: number
  /** 群公告已确认人数 */
  noticeReadCount?: number
  /** 群成员总数（= 已确认进度分母） */
  noticeTotal?: number
  /** 加群策略：open=知道群号可直接进 / approve=需群主审批（默认 approve） */
  joinPolicy?: 'open' | 'approve'
  /** 待审批加群申请数（仅审批群有，群主/管理员据此显示审批入口） */
  joinPending?: number
}

export interface GroupMember {
  userId: string
  nickname: string
  avatar: string
  role: 'owner' | 'admin' | 'member'
  muted?: boolean
  /** 我在本群的昵称（空则显示大面昵称） */
  groupNick?: string
  /** 群头衔（群主/管理员可为每位成员单独设置），空则不显示 */
  title?: string
}

export interface Moment {
  id: string
  author?: User
  text: string
  images: string[]
  scope: 'all' | 'friends' | 'private'
  likes: { userId: string, nickname: string }[]
  comments: MomentComment[]
  createdAt: string
}

export interface MomentComment {
  id: string
  userId: string
  nickname: string
  text: string
  createdAt: string
}

export interface AuthTokens {
  accessToken: string
  refreshToken: string
  expiresIn: number
}

export interface FriendRequest {
  id: string
  from: User
  message: string
  status: 'pending' | 'accepted' | 'rejected'
  createdAt: string
}
