import { useEffect, useRef, type PointerEvent } from 'react';
import type { InputButtonDefinition } from '../../types/player';
import { PlayerBridge } from '../services/playerBridge';

const directions: Record<number, [string, string]> = {
  4: ['↑', 'col-start-2 row-start-1'],
  5: ['↓', 'col-start-2 row-start-3'],
  6: ['←', 'col-start-1 row-start-2'],
  7: ['→', 'col-start-3 row-start-2'],
};

export function VirtualGamepad({ buttons, lefty }: {
  buttons: InputButtonDefinition[];
  lefty: boolean;
}) {
  const active = useRef(new Map<number, number>());

  useEffect(() => {
    const releaseAll = () => {
      const held = new Set(active.current.values());
      active.current.clear();
      for (const id of held) PlayerBridge.sendVirtualInput(id, false, new Set());
    };
    const onVisibilityChange = () => {
      if (document.hidden) releaseAll();
    };
    window.addEventListener('blur', releaseAll);
    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => {
      window.removeEventListener('blur', releaseAll);
      document.removeEventListener('visibilitychange', onVisibilityChange);
      releaseAll();
    };
  }, []);

  const press = (event: PointerEvent<HTMLButtonElement>, id: number) => {
    if (event.button !== 0 || active.current.has(event.pointerId)) return;
    event.preventDefault();
    event.currentTarget.setPointerCapture(event.pointerId);
    const held = new Set(active.current.values());
    active.current.set(event.pointerId, id);
    if (!held.has(id)) {
      held.add(id);
      PlayerBridge.sendVirtualInput(id, true, held);
    }
  };

  const release = (event: PointerEvent<HTMLButtonElement>) => {
    const id = active.current.get(event.pointerId);
    if (id === undefined) return;
    active.current.delete(event.pointerId);
    const held = new Set(active.current.values());
    if (!held.has(id)) PlayerBridge.sendVirtualInput(id, false, held);
  };

  const renderButton = (button: InputButtonDefinition) => {
    const direction = directions[button.id];
    return (
      <button
        key={button.id}
        type="button"
        aria-label={button.label}
        tabIndex={-1}
        onPointerDown={(event) => press(event, button.id)}
        onPointerUp={release}
        onPointerCancel={release}
        onLostPointerCapture={release}
        className={`pointer-events-auto touch-none min-h-11 min-w-11 rounded-lg border border-white/40
          bg-black/70 px-1 text-xs text-white active:bg-white/30 ${direction?.[1] ?? ''}`}
      >
        {direction?.[0] ?? button.label}
      </button>
    );
  };

  const dpad = buttons.filter((button) => button.id in directions);
  const actions = buttons.filter((button) => button.id >= 0 && button.id < 24 && !(button.id in directions));

  return (
    <div
      role="group"
      aria-label="Virtual gamepad"
      className={`fixed inset-x-2 bottom-[max(5rem,env(safe-area-inset-bottom))] z-30 flex
        items-end justify-between gap-2 ${lefty ? 'flex-row-reverse' : ''}`}
    >
      <div className="grid grid-cols-3 grid-rows-3 gap-0.5">
        {dpad.map(renderButton)}
      </div>
      <div className="grid max-w-[60%] grid-cols-3 gap-1">
        {actions.map(renderButton)}
      </div>
    </div>
  );
}
