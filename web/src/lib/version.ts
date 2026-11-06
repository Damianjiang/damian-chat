/**
 * 产品语义版本号（展示给终端用户：登录页徽标 / 设置「关于」对话框）。
 * 与构建指纹 __BUILD_VERSION__（vite 每次构建注入、用于「检查更新」比对）不同：
 *   ・APP_VERSION   —— 人读的版本号，发版时手动递增；
 *   ・__BUILD_VERSION__ —— 机器读的时间戳指纹，构建即变，用于服务端 version.json 比对。
 * 发版时只改这一处，登录页与「关于」对话框自动同步。
 */
export const APP_VERSION = '1.10.4'
