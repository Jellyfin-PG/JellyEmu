import React, { useEffect, useRef, useState } from 'react';

interface PlayerPerformanceOverlayProps {
  initialMode?: string;
}

export const PlayerPerformanceOverlay: React.FC<PlayerPerformanceOverlayProps> = ({ initialMode = '0' }) => {
  const [mode, setMode] = useState<string>(() => {
    return localStorage.getItem('jellyemu:fps-overlay-mode') || initialMode || '0';
  });
  const [fps, setFps] = useState<number>(60);
  const [frameTimeMs, setFrameTimeMs] = useState<number>(16.6);
  const [onePercentLow, setOnePercentLow] = useState<number>(60);
  const [resolution, setResolution] = useState<{ w: number; h: number }>({ w: 0, h: 0 });

  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const frameTimesRef = useRef<number[]>([]);
  const lastFrameRef = useRef<number>(performance.now());
  const rafIdRef = useRef<number | null>(null);

  // Sync mode changes via custom event or prop
  useEffect(() => {
    const handleModeChange = (e: CustomEvent<{ mode: string }>) => {
      if (e.detail?.mode !== undefined) {
        const newMode = String(e.detail.mode);
        setMode(newMode);
        localStorage.setItem('jellyemu:fps-overlay-mode', newMode);
      }
    };
    window.addEventListener('jellyemu:fps-changed' as any, handleModeChange);
    return () => {
      window.removeEventListener('jellyemu:fps-changed' as any, handleModeChange);
    };
  }, []);

  // Sync incoming FPS from RetroKit event
  useEffect(() => {
    const handleFpsUpdate = (e: CustomEvent<{ fps: number }>) => {
      if (typeof e.detail?.fps === 'number') {
        setFps(Math.round(e.detail.fps * 10) / 10);
      }
    };
    window.addEventListener('jellyemu:fps-update' as any, handleFpsUpdate);
    return () => {
      window.removeEventListener('jellyemu:fps-update' as any, handleFpsUpdate);
    };
  }, []);

  // Performance loop for detailed metrics and frame-time graph (Mode 2)
  useEffect(() => {
    if (mode === '0') {
      if (rafIdRef.current) cancelAnimationFrame(rafIdRef.current);
      return;
    }

    let frameCount = 0;
    let lastSecond = performance.now();

    const loop = (now: number) => {
      const delta = now - lastFrameRef.current;
      lastFrameRef.current = now;

      if (delta > 0 && delta < 200) {
        frameTimesRef.current.push(delta);
        if (frameTimesRef.current.length > 120) {
          frameTimesRef.current.shift();
        }
      }

      frameCount++;
      if (now - lastSecond >= 500) {
        if (frameTimesRef.current.length > 0) {
          const avgDelta = frameTimesRef.current.reduce((a, b) => a + b, 0) / frameTimesRef.current.length;
          setFrameTimeMs(Math.round(avgDelta * 10) / 10);

          // Calculate 1% low
          const sorted = [...frameTimesRef.current].sort((a, b) => b - a);
          const p99Index = Math.floor(sorted.length * 0.99);
          const p99Delta = sorted[p99Index] || avgDelta;
          setOnePercentLow(Math.round((1000 / p99Delta) * 10) / 10);
        }

        // Detect core canvas internal resolution
        const emuCanvas = (document.getElementById('retrokit-canvas') ||
          document.querySelector('canvas:not([data-hud-canvas])')) as HTMLCanvasElement | null;
        if (emuCanvas && (emuCanvas.width !== resolution.w || emuCanvas.height !== resolution.h)) {
          setResolution({ w: emuCanvas.width, h: emuCanvas.height });
        }

        frameCount = 0;
        lastSecond = now;
      }

      // Draw canvas graph in detailed mode (Mode 2)
      if (mode === '2' && canvasRef.current) {
        const ctx = canvasRef.current.getContext('2d');
        if (ctx) {
          const w = canvasRef.current.width;
          const h = canvasRef.current.height;
          ctx.clearRect(0, 0, w, h);

          // Background
          ctx.fillStyle = 'rgba(15, 23, 42, 0.75)';
          ctx.fillRect(0, 0, w, h);

          // 16.6ms (60 FPS) and 33.3ms (30 FPS) target guide lines
          const y60 = h - (16.6 / 50) * h;
          const y30 = h - (33.3 / 50) * h;

          ctx.strokeStyle = 'rgba(74, 222, 128, 0.35)'; // Green 60fps line
          ctx.lineWidth = 1;
          ctx.setLineDash([2, 2]);
          ctx.beginPath();
          ctx.moveTo(0, y60);
          ctx.lineTo(w, y60);
          ctx.stroke();

          ctx.strokeStyle = 'rgba(250, 204, 21, 0.35)'; // Amber 30fps line
          ctx.beginPath();
          ctx.moveTo(0, y30);
          ctx.lineTo(w, y30);
          ctx.stroke();
          ctx.setLineDash([]);

          // Plot frame times
          const times = frameTimesRef.current;
          if (times.length > 1) {
            ctx.beginPath();
            ctx.strokeStyle = '#38bdf8'; // Sky blue
            ctx.lineWidth = 1.5;
            for (let i = 0; i < times.length; i++) {
              const x = (i / 120) * w;
              const y = Math.max(0, Math.min(h, h - (times[i] / 50) * h));
              if (i === 0) ctx.moveTo(x, y);
              else ctx.lineTo(x, y);
            }
            ctx.stroke();
          }
        }
      }

      rafIdRef.current = requestAnimationFrame(loop);
    };

    lastFrameRef.current = performance.now();
    rafIdRef.current = requestAnimationFrame(loop);

    return () => {
      if (rafIdRef.current) cancelAnimationFrame(rafIdRef.current);
    };
  }, [mode, resolution]);

  if (mode === '0') return null;

  const fpsColor = fps >= 55 ? 'text-emerald-400' : fps >= 30 ? 'text-amber-400' : 'text-rose-400';

  if (mode === '1') {
    return (
      <div className="fixed top-14 left-4 z-30 pointer-events-none select-none flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-neutral-950/95 backdrop-blur-md border border-white/15 font-mono text-xs font-semibold text-white shadow-lg">
        <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
        <span className={fpsColor}>{fps.toFixed(1)}</span>
        <span className="text-white/80 text-[10px]">FPS</span>
      </div>
    );
  }

  // Mode 2: Detailed Performance HUD
  return (
    <div className="fixed top-14 left-4 z-30 pointer-events-none select-none flex flex-col gap-1.5 p-3 rounded-lg bg-neutral-950/95 backdrop-blur-md border border-white/15 font-mono text-xs text-white shadow-2xl min-w-[200px]">
      <div className="flex items-center justify-between border-b border-white/15 pb-1.5">
        <span className="text-white text-[11px] font-medium tracking-wide">PERFORMANCE</span>
        <span className="text-[10px] text-white/80">{resolution.w > 0 ? `${resolution.w}×${resolution.h}` : 'RetroKit'}</span>
      </div>

      <div className="grid grid-cols-2 gap-2 text-[11px] py-1">
        <div>
          <div className="text-white/70 text-[10px]">AVG FPS</div>
          <div className={`text-base font-bold ${fpsColor}`}>{fps.toFixed(1)}</div>
        </div>
        <div>
          <div className="text-white/70 text-[10px]">1% LOW</div>
          <div className="text-base font-bold text-white">{onePercentLow.toFixed(1)}</div>
        </div>
        <div>
          <div className="text-white/70 text-[10px]">FRAME TIME</div>
          <div className="text-white font-medium">{frameTimeMs.toFixed(1)} ms</div>
        </div>
        <div>
          <div className="text-white/70 text-[10px]">TARGET</div>
          <div className="text-white font-medium">16.6 ms</div>
        </div>
      </div>

      {/* Frame Timing History Graph */}
      <div className="w-full h-12 relative rounded overflow-hidden border border-white/15 mt-1">
        <canvas ref={canvasRef} data-hud-canvas="true" width={192} height={48} className="w-full h-full block" />
        <div className="absolute top-0.5 right-1 text-[9px] text-white/80">50ms</div>
        <div className="absolute bottom-0.5 right-1 text-[9px] text-white/80">0ms</div>
      </div>
    </div>
  );
};
