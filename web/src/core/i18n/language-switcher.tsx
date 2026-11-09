import { Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { SUPPORTED_LANGUAGES, LANGUAGE_LABELS } from './i18n-config';
import type { SupportedLanguage } from './i18n-config';

const OPTIONS = SUPPORTED_LANGUAGES.map((lang) => ({
  value: lang,
  label: LANGUAGE_LABELS[lang],
}));

// Ant Design Select that switches the active i18next language
export function LanguageSwitcher() {
  const { i18n } = useTranslation();

  const handleChange = (lang: SupportedLanguage) => {
    void i18n.changeLanguage(lang);
  };

  return (
    <Select<SupportedLanguage>
      value={i18n.language as SupportedLanguage}
      options={OPTIONS}
      onChange={handleChange}
      size="small"
      style={{ width: 120 }}
      aria-label="Select language"
    />
  );
}
