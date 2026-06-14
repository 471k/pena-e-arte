import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // Fast-refresh wants component-only modules. These files legitimately mix
    // components with non-component exports: shadcn/ui primitives (variants,
    // context), the router (route table + guards), and shared field helpers.
    // HMR still works — at worst these modules full-reload during dev.
    files: [
      'src/shared/components/ui/**/*.tsx',
      'src/app/router.tsx',
      'src/features/clients/components/BodyMap.tsx',
      'src/shared/components/FileUploadField.tsx',
    ],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
])
