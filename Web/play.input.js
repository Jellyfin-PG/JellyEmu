// JellyEmu Play! Input Manager
// Handles Gamepads (Buttons + Dual Analog Sticks), Keyboard, and Touch Controls for Play! WASM.
(function(window) {
    'use strict';

    const PS2_KEYS = {
        DPAD_UP: 'ArrowUp',
        DPAD_DOWN: 'ArrowDown',
        DPAD_LEFT: 'ArrowLeft',
        DPAD_RIGHT: 'ArrowRight',

        CROSS: 'KeyZ',
        CIRCLE: 'KeyX',
        SQUARE: 'KeyA',
        TRIANGLE: 'KeyS',

        L1: 'Key1',
        R1: 'Key2',
        L2: 'Key3',
        R2: 'Key8',
        L3: 'Key9',
        R3: 'Key0',

        L_STICK_UP: 'KeyT',
        L_STICK_DOWN: 'KeyG',
        L_STICK_LEFT: 'KeyF',
        L_STICK_RIGHT: 'KeyH',

        R_STICK_UP: 'KeyI',
        R_STICK_DOWN: 'KeyK',
        R_STICK_LEFT: 'KeyJ',
        R_STICK_RIGHT: 'KeyL',

        SELECT: 'ShiftRight',
        START: 'Enter'
    };

    const KEY_INFO = {
        'ArrowUp': { keyCode: 38, key: 'ArrowUp' },
        'ArrowDown': { keyCode: 40, key: 'ArrowDown' },
        'ArrowLeft': { keyCode: 37, key: 'ArrowLeft' },
        'ArrowRight': { keyCode: 39, key: 'ArrowRight' },

        'KeyZ': { keyCode: 90, key: 'z' },
        'KeyX': { keyCode: 88, key: 'x' },
        'KeyA': { keyCode: 65, key: 'a' },
        'KeyS': { keyCode: 83, key: 's' },

        'Key1': { keyCode: 49, key: '1' },
        'Key2': { keyCode: 50, key: '2' },
        'Key3': { keyCode: 51, key: '3' },
        'Key8': { keyCode: 56, key: '8' },
        'Key9': { keyCode: 57, key: '9' },
        'Key0': { keyCode: 48, key: '0' },

        'KeyT': { keyCode: 84, key: 't' },
        'KeyG': { keyCode: 71, key: 'g' },
        'KeyF': { keyCode: 70, key: 'f' },
        'KeyH': { keyCode: 72, key: 'h' },

        'KeyI': { keyCode: 73, key: 'i' },
        'KeyK': { keyCode: 75, key: 'k' },
        'KeyJ': { keyCode: 74, key: 'j' },
        'KeyL': { keyCode: 76, key: 'l' },

        'Enter': { keyCode: 13, key: 'Enter' },
        'ShiftRight': { keyCode: 16, key: 'Shift' },
        'Backspace': { keyCode: 8, key: 'Backspace' }
    };

    class PlayInputManager {
        constructor(playModule) {
            this.playModule = playModule;
            this.activeKeys = new Set();
            this.polling = false;
        }

        init(customBindings) {
            this.setupTouchControls();
            this.setupKeyboardPassthrough();
            this.startGamepadPolling();
        }

        simulateKey(code, isPressed) {
            if (!code) return;
            const isCurrentlyDown = this.activeKeys.has(code);
            if (isPressed === isCurrentlyDown) return;

            if (isPressed) {
                this.activeKeys.add(code);
            } else {
                this.activeKeys.delete(code);
            }

            const canvas = document.getElementById('outputCanvas');
            const eventType = isPressed ? 'keydown' : 'keyup';
            const info = KEY_INFO[code] || { keyCode: 0, key: code };

            const evt = new KeyboardEvent(eventType, {
                code: code,
                key: info.key,
                keyCode: info.keyCode,
                which: info.keyCode,
                charCode: info.keyCode,
                bubbles: true,
                cancelable: true
            });

            try {
                Object.defineProperty(evt, 'code', { get: () => code, configurable: true });
                Object.defineProperty(evt, 'keyCode', { get: () => info.keyCode, configurable: true });
                Object.defineProperty(evt, 'which', { get: () => info.keyCode, configurable: true });
                Object.defineProperty(evt, 'charCode', { get: () => info.keyCode, configurable: true });
            } catch (e) {}

            if (canvas) canvas.dispatchEvent(evt);
            window.dispatchEvent(evt);
            document.dispatchEvent(evt);
        }

        setupKeyboardPassthrough() {
            // Physical keyboard mapping to Play! WASM codes
            const keyMap = {
                'KeyQ': PS2_KEYS.L1,       // Q -> L1 ('Key1')
                'KeyE': PS2_KEYS.R1,       // E -> R1 ('Key2')
                'Digit1': PS2_KEYS.L1,     // 1 -> L1 ('Key1')
                'Digit2': PS2_KEYS.R1,     // 2 -> R1 ('Key2')
                'Digit3': PS2_KEYS.L2,     // 3 -> L2 ('Key3')
                'Digit4': PS2_KEYS.R2,     // 4 -> R2 ('Key8')
                'Digit8': PS2_KEYS.R2,     // 8 -> R2 ('Key8')
                'Digit9': PS2_KEYS.L3,     // 9 -> L3 ('Key9')
                'Digit0': PS2_KEYS.R3,     // 0 -> R3 ('Key0')
                'KeyW': PS2_KEYS.L_STICK_UP,
                'KeyD': PS2_KEYS.L_STICK_RIGHT
            };

            window.addEventListener('keydown', (e) => {
                if (e.repeat) return;
                const mapped = keyMap[e.code];
                if (mapped) {
                    this.simulateKey(mapped, true);
                }
            });

            window.addEventListener('keyup', (e) => {
                const mapped = keyMap[e.code];
                if (mapped) {
                    this.simulateKey(mapped, false);
                }
            });
        }

        setupTouchControls() {
            const touchMap = {
                'DPAD_UP': PS2_KEYS.DPAD_UP,
                'DPAD_DOWN': PS2_KEYS.DPAD_DOWN,
                'DPAD_LEFT': PS2_KEYS.DPAD_LEFT,
                'DPAD_RIGHT': PS2_KEYS.DPAD_RIGHT,
                'CROSS': PS2_KEYS.CROSS,
                'CIRCLE': PS2_KEYS.CIRCLE,
                'SQUARE': PS2_KEYS.SQUARE,
                'TRIANGLE': PS2_KEYS.TRIANGLE,
                'L1': PS2_KEYS.L1,
                'R1': PS2_KEYS.R1,
                'L2': PS2_KEYS.L2,
                'R2': PS2_KEYS.R2,
                'SELECT': PS2_KEYS.SELECT,
                'START': PS2_KEYS.START
            };

            const touchButtons = document.querySelectorAll('[data-ps-btn]');
            touchButtons.forEach(btn => {
                const btnName = btn.getAttribute('data-ps-btn');
                const keyCode = touchMap[btnName];
                if (!keyCode) return;

                const press = (e) => {
                    e.preventDefault();
                    btn.classList.add('je-pressed');
                    this.simulateKey(keyCode, true);
                };

                const release = (e) => {
                    e.preventDefault();
                    btn.classList.remove('je-pressed');
                    this.simulateKey(keyCode, false);
                };

                btn.addEventListener('touchstart', press, { passive: false });
                btn.addEventListener('touchend', release, { passive: false });
                btn.addEventListener('touchcancel', release, { passive: false });
                btn.addEventListener('mousedown', press);
                btn.addEventListener('mouseup', release);
                btn.addEventListener('mouseleave', release);
            });
        }

        startGamepadPolling() {
            if (this.polling) return;
            this.polling = true;

            const standardButtons = [
                PS2_KEYS.CROSS,       // 0: A / Cross
                PS2_KEYS.CIRCLE,      // 1: B / Circle
                PS2_KEYS.SQUARE,      // 2: X / Square
                PS2_KEYS.TRIANGLE,    // 3: Y / Triangle
                PS2_KEYS.L1,          // 4: L1 / LB
                PS2_KEYS.R1,          // 5: R1 / RB
                PS2_KEYS.L2,          // 6: L2 / LT
                PS2_KEYS.R2,          // 7: R2 / RT
                PS2_KEYS.SELECT,      // 8: Back / Select
                PS2_KEYS.START,       // 9: Start / Menu
                PS2_KEYS.L3,          // 10: L3 (Left stick click)
                PS2_KEYS.R3,          // 11: R3 (Right stick click)
                PS2_KEYS.DPAD_UP,     // 12: D-Up
                PS2_KEYS.DPAD_DOWN,   // 13: D-Down
                PS2_KEYS.DPAD_LEFT,   // 14: D-Left
                PS2_KEYS.DPAD_RIGHT   // 15: D-Right
            ];

            const poll = () => {
                try {
                    const gamepads = navigator.getGamepads ? navigator.getGamepads() : [];
                    for (let i = 0; i < gamepads.length; i++) {
                        const gp = gamepads[i];
                        if (!gp) continue;

                        // Digital Buttons & Triggers
                        gp.buttons.forEach((btn, idx) => {
                            const code = standardButtons[idx];
                            if (code) {
                                const isDown = btn.pressed || btn.value > 0.45;
                                this.simulateKey(code, isDown);
                            }
                        });

                        // Left Analog Stick (Axes 0, 1)
                        const deadzone = 0.28;
                        const lx = gp.axes[0] || 0;
                        const ly = gp.axes[1] || 0;

                        this.simulateKey(PS2_KEYS.L_STICK_LEFT, lx < -deadzone);
                        this.simulateKey(PS2_KEYS.L_STICK_RIGHT, lx > deadzone);
                        this.simulateKey(PS2_KEYS.L_STICK_UP, ly < -deadzone);
                        this.simulateKey(PS2_KEYS.L_STICK_DOWN, ly > deadzone);

                        // Right Analog Stick (Axes 2, 3)
                        const rx = gp.axes[2] || 0;
                        const ry = gp.axes[3] || 0;

                        this.simulateKey(PS2_KEYS.R_STICK_LEFT, rx < -deadzone);
                        this.simulateKey(PS2_KEYS.R_STICK_RIGHT, rx > deadzone);
                        this.simulateKey(PS2_KEYS.R_STICK_UP, ry < -deadzone);
                        this.simulateKey(PS2_KEYS.R_STICK_DOWN, ry > deadzone);
                    }
                } catch (e) {
                    // Ignore transient gamepad polling errors
                }

                requestAnimationFrame(poll);
            };

            requestAnimationFrame(poll);
        }
    }

    window.PlayInputManager = PlayInputManager;
})(window);
