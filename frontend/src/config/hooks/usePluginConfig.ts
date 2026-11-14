import { useState, useEffect, useCallback } from 'react';
import { PluginConfiguration } from '../../types/config';
import { jellyfinApi } from '../../shared/apiClient';

export function usePluginConfig() {
  const [config, setConfig] = useState<PluginConfiguration>({});
  const [loading, setLoading] = useState<boolean>(true);
  const [saving, setSaving] = useState<boolean>(false);
  const [saveSuccess, setSaveSuccess] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const fetchConfig = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await jellyfinApi.getPluginConfig();
      setConfig(data || {});
    } catch (err: any) {
      console.error('[JellyEmu] Failed loading config:', err);
      setError(err?.message || 'Failed to load configuration');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchConfig();
  }, [fetchConfig]);

  const updateConfig = async (newConfig: PluginConfiguration) => {
    try {
      setSaving(true);
      setError(null);
      await jellyfinApi.updatePluginConfig(newConfig);
      setConfig(newConfig);
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 3000);
      return true;
    } catch (err: any) {
      console.error('[JellyEmu] Failed saving config:', err);
      setError(err?.message || 'Failed to save configuration');
      return false;
    } finally {
      setSaving(false);
    }
  };

  const updateField = <K extends keyof PluginConfiguration>(field: K, value: PluginConfiguration[K]) => {
    setConfig(prev => ({
      ...prev,
      [field]: value
    }));
  };

  return {
    config,
    loading,
    saving,
    saveSuccess,
    error,
    updateField,
    updateConfig,
    reload: fetchConfig
  };
}
