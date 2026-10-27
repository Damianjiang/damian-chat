import buildConfig from 'eslint-config-vuetify'

export default await buildConfig(
  {
    // Pinia 选项式 store 在 actions 内使用 this 是官方写法，关闭该规则。
    files: ['src/stores/**/*.ts'],
    rules: {
      'unicorn/no-this-outside-of-class': 'off',
    },
  },
  {
    // v-model 的 update:modelValue 事件名无法改为 kebab-case。
    files: ['src/**/*.vue'],
    rules: {
      'vue/custom-event-name-casing': ['error', 'kebab-case', { ignores: ['update:modelValue'] }],
    },
  },
)
