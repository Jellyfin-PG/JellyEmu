import React, { useEffect, useState, useRef } from 'react';
import {
  Save,
  Download,
  Layers,
  FastForward,
  Rewind,
  Play,
  Pause,
  RotateCcw,
  Volume2,
  VolumeX,
  Camera,
  Maximize2,
  Minimize2,
  LogOut,
  Gamepad2,
  CheckCircle2,
  AlertCircle,
  Info
} from 'lucide-react';

export type NotificationVariant = 'default' | 'success' | 'warning' | 'info' | 'accent' | 'danger';

export interface PlayerNotificationData {
  id?: string;
  message: string;
  subtext?: string;
  icon?: string;
  variant?: NotificationVariant;
  duration?: number;
}

interface PlayerNotificationProps {
  topBarVisible?: boolean;
}

export const PlayerNotification: React.FC<PlayerNotificationProps> = ({ topBarVisible = false }) => {
  const [current, setCurrent] = useState<PlayerNotificationData | null>(null);
  const [visible, setVisible] = useState<boolean>(false);
  const [pulseKey, setPulseKey] = useState<number>(0);
  const hideTimerRef = useRef<any>(null);

  useEffect(() => {
    const handleNotification = (e: any) => {
      const data: PlayerNotificationData = e?.detail;
      if (!data || !data.message) return;

      if (hideTimerRef.current) {
        clearTimeout(hideTimerRef.current);
      }

      setCurrent(data);
      setVisible(true);
      setPulseKey(prev => prev + 1);

      const dur = typeof data.duration === 'number' ? data.duration : 2200;
      hideTimerRef.current = setTimeout(() => {
        setVisible(false);
      }, dur);
    };

    window.addEventListener('jellyemu:notify', handleNotification);
    return () => {
      window.removeEventListener('jellyemu:notify', handleNotification);
      if (hideTimerRef.current) {
        clearTimeout(hideTimerRef.current);
      }
    };
  }, []);

  if (!current && !visible) {
    return null;
  }

  const renderIcon = (iconName?: string) => {
    const iconClass = "w-4 h-4";
    switch (iconName?.toLowerCase()) {
      case 'save':
        return <Save className={iconClass} />;
      case 'load':
        return <Download className={iconClass} />;
      case 'slot':
        return <Layers className={iconClass} />;
      case 'fastforward':
        return <FastForward className={iconClass} />;
      case 'slowmotion':
      case 'rewind':
        return <Rewind className={iconClass} />;
      case 'play':
      case 'resume':
        return <Play className={iconClass} />;
      case 'pause':
        return <Pause className={iconClass} />;
      case 'restart':
        return <RotateCcw className={iconClass} />;
      case 'mute':
        return <VolumeX className={iconClass} />;
      case 'unmute':
      case 'volume':
        return <Volume2 className={iconClass} />;
      case 'screenshot':
        return <Camera className={iconClass} />;
      case 'fullscreen':
        return <Maximize2 className={iconClass} />;
      case 'exitfullscreen':
        return <Minimize2 className={iconClass} />;
      case 'exit':
        return <LogOut className={iconClass} />;
      case 'gamepad':
      case 'hotkey':
        return <Gamepad2 className={iconClass} />;
      case 'success':
        return <CheckCircle2 className={iconClass} />;
      case 'warning':
      case 'alert':
        return <AlertCircle className={iconClass} />;
      default:
        return <Info className={iconClass} />;
    }
  };

  const getVariantStyles = (variant: NotificationVariant = 'default') => {
    switch (variant) {
      case 'success':
        return {
          glow: 'shadow-[0_8px_30px_rgba(16,185,129,0.25)] border-emerald-500/40',
          badge: 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/40'
        };
      case 'warning':
        return {
          glow: 'shadow-[0_8px_30px_rgba(245,158,11,0.25)] border-amber-500/40',
          badge: 'bg-amber-500/20 text-amber-400 border border-amber-500/40'
        };
      case 'accent':
        return {
          glow: 'shadow-[0_8px_30px_rgba(168,85,247,0.25)] border-purple-500/40',
          badge: 'bg-purple-500/20 text-purple-400 border border-purple-500/40'
        };
      case 'info':
        return {
          glow: 'shadow-[0_8px_30px_rgba(14,165,233,0.25)] border-sky-500/40',
          badge: 'bg-sky-500/20 text-sky-400 border border-sky-500/40'
        };
      case 'danger':
        return {
          glow: 'shadow-[0_8px_30px_rgba(244,63,94,0.25)] border-rose-500/40',
          badge: 'bg-rose-500/20 text-rose-400 border border-rose-500/40'
        };
      default:
        return {
          glow: 'shadow-[0_8px_30px_rgba(255,255,255,0.12)] border-white/20',
          badge: 'bg-white/10 text-white border border-white/20'
        };
    }
  };

  const variant = current?.variant || 'default';
  const styles = getVariantStyles(variant);

  return (
    <div
      key={pulseKey}
      className={`fixed left-1/2 -translate-x-1/2 z-50 pointer-events-none transition-all duration-300 ease-out ${
        topBarVisible ? 'top-14' : 'top-4'
      } ${
        visible
          ? 'opacity-100 scale-100 translate-y-0'
          : 'opacity-0 scale-95 -translate-y-3 pointer-events-none'
      }`}
    >
      <div
        className={`flex items-center gap-3 px-4 py-2 rounded-2xl bg-black/90 backdrop-blur-xl border ${styles.glow} text-white transition-all`}
      >
        <div className={`w-7 h-7 rounded-xl flex items-center justify-center shrink-0 ${styles.badge}`}>
          {renderIcon(current?.icon)}
        </div>
        <div className="flex flex-col text-left">
          <span className="text-xs font-semibold text-white tracking-wide leading-tight">
            {current?.message}
          </span>
          {current?.subtext && (
            <span className="text-[10px] text-neutral-400 font-medium leading-tight">
              {current.subtext}
            </span>
          )}
        </div>
      </div>
    </div>
  );
};
