import { useState, useEffect, useCallback } from 'react';

export function useGamepadListener(isListening: boolean, onButtonDetected?: (buttonIndex: number) => void) {
  const [gamepads, setGamepads] = useState<string[]>([]);

  const pollGamepads = useCallback(() => {
    const list = navigator.getGamepads ? navigator.getGamepads() : [];
    const names: string[] = [];
    for (let i = 0; i < list.length; i++) {
      const gp = list[i];
      if (gp) {
        names.push(gp.id || `Gamepad ${i + 1}`);
        if (isListening && onButtonDetected) {
          gp.buttons.forEach((btn, idx) => {
            if (btn.pressed || btn.value > 0.5) {
              onButtonDetected(idx);
            }
          });
        }
      }
    }
    setGamepads(names);
  }, [isListening, onButtonDetected]);

  useEffect(() => {
    let animId: number;
    const loop = () => {
      pollGamepads();
      animId = requestAnimationFrame(loop);
    };
    animId = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(animId);
  }, [pollGamepads]);

  return {
    gamepads,
    hasGamepad: gamepads.length > 0
  };
}
