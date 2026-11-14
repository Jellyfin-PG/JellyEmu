import React, { useState } from 'react';
import { HeroUIProvider, Tabs, Tab, Spinner } from '@heroui/react';
import { BarChart3, Settings as SettingsIcon, ShoppingBag, MessageSquare, Code, Info } from 'lucide-react';
import { usePluginConfig } from './hooks/usePluginConfig';
import { ConfigHeader } from './components/ConfigHeader';
import { SettingsTab } from './tabs/settings/SettingsTab';
import { StatisticsTab } from './tabs/statistics/StatisticsTab';
import { MarketplaceTab } from './tabs/marketplace/MarketplaceTab';
import { CommunityTab } from './tabs/community/CommunityTab';
import { ApiDocTab } from './tabs/api/ApiDocTab';
import { AboutTab } from './tabs/about/AboutTab';

export const ConfigApp: React.FC = () => {
  const [selectedTab, setSelectedTab] = useState<string>('statistics');
  const { config, loading, saving, saveSuccess, updateField, updateConfig } = usePluginConfig();

  if (loading) {
    return (
      <HeroUIProvider>
        <div className="dark text-foreground bg-background p-12 flex items-center justify-center min-h-[400px]">
          <Spinner label="Loading JellyEmu Dashboard..." color="primary" size="lg" />
        </div>
      </HeroUIProvider>
    );
  }

  return (
    <HeroUIProvider>
      <div className="dark text-foreground bg-background min-h-screen w-full px-4 sm:px-6 lg:px-8 py-6 space-y-6">
        {/* Header */}
        <ConfigHeader version="0.9.2.4" />

        {/* HeroUI Modern Solid Full-Width Segmented Navigation */}
        <div className="w-full">
          <Tabs
            selectedKey={selectedTab}
            onSelectionChange={(key) => setSelectedTab(key as string)}
            aria-label="JellyEmu Navigation Tabs"
            color="primary"
            variant="solid"
            radius="lg"
            size="lg"
            classNames={{
              base: "w-full overflow-x-auto scrollbar-none pb-1",
              tabList: "w-full flex items-center gap-1.5 p-1.5 bg-[#141416]/90 border border-white/10 backdrop-blur-2xl rounded-2xl shadow-xl justify-between",
              cursor: "bg-gradient-to-r from-primary to-primary-600 shadow-lg shadow-primary/30 rounded-xl",
              tab: "flex-1 min-w-[120px] sm:min-w-[140px] h-11 px-3 text-slate-400 hover:text-white data-[selected=true]:text-white font-semibold text-sm transition-all duration-200 rounded-xl",
              tabContent: "font-bold tracking-wide flex items-center justify-center gap-2 text-sm",
              panel: "pt-6 w-full max-w-full"
            }}
          >
            <Tab
              key="statistics"
              title={
                <div className="flex items-center gap-2">
                  <BarChart3 className="w-4 h-4 text-cyan-400 group-data-[selected=true]:text-white transition-colors" />
                  <span>Statistics</span>
                </div>
              }
            >
              <StatisticsTab />
            </Tab>

            <Tab
              key="settings"
              title={
                <div className="flex items-center gap-2">
                  <SettingsIcon className="w-4 h-4 text-blue-400 group-data-[selected=true]:text-white transition-colors" />
                  <span>Settings</span>
                </div>
              }
            >
              <SettingsTab
                config={config}
                onUpdate={updateField}
                onSave={() => updateConfig(config)}
                saving={saving}
                saveSuccess={saveSuccess}
              />
            </Tab>

            <Tab
              key="marketplace"
              title={
                <div className="flex items-center gap-2">
                  <ShoppingBag className="w-4 h-4 text-emerald-400 group-data-[selected=true]:text-white transition-colors" />
                  <span>Marketplace</span>
                </div>
              }
            >
              <MarketplaceTab />
            </Tab>

            <Tab
              key="community"
              title={
                <div className="flex items-center gap-2">
                  <MessageSquare className="w-4 h-4 text-purple-400 group-data-[selected=true]:text-white transition-colors" />
                  <span>Community</span>
                </div>
              }
            >
              <CommunityTab />
            </Tab>

            <Tab
              key="api"
              title={
                <div className="flex items-center gap-2">
                  <Code className="w-4 h-4 text-amber-400 group-data-[selected=true]:text-white transition-colors" />
                  <span>API Docs</span>
                </div>
              }
            >
              <ApiDocTab />
            </Tab>

            <Tab
              key="about"
              title={
                <div className="flex items-center gap-2">
                  <Info className="w-4 h-4 text-sky-400 group-data-[selected=true]:text-white transition-colors" />
                  <span>About</span>
                </div>
              }
            >
              <AboutTab />
            </Tab>
          </Tabs>
        </div>
      </div>
    </HeroUIProvider>
  );
};
