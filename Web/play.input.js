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

    const BUTTON_ID_TO_PS2_KEY = {
        0: PS2_KEYS.CROSS,          // CROSS
        1: PS2_KEYS.SQUARE,         // SQUARE
        2: PS2_KEYS.SELECT,         // SELECT
        3: PS2_KEYS.START,          // START
        4: PS2_KEYS.DPAD_UP,        // UP
        5: PS2_KEYS.DPAD_DOWN,      // DOWN
        6: PS2_KEYS.DPAD_LEFT,      // LEFT
        7: PS2_KEYS.DPAD_RIGHT,     // RIGHT
        8: PS2_KEYS.CIRCLE,         // CIRCLE
        9: PS2_KEYS.TRIANGLE,       // TRIANGLE
        10: PS2_KEYS.L1,            // L1
        11: PS2_KEYS.R1,            // R1
        12: PS2_KEYS.L2,            // L2
        13: PS2_KEYS.R2,            // R2
        14: PS2_KEYS.L3,            // L3
        15: PS2_KEYS.R3,            // R3
        16: PS2_KEYS.L_STICK_RIGHT, // L STICK RIGHT
        17: PS2_KEYS.L_STICK_LEFT,  // L STICK LEFT
        18: PS2_KEYS.L_STICK_DOWN,  // L STICK DOWN
        19: PS2_KEYS.L_STICK_UP,    // L STICK UP
        20: PS2_KEYS.R_STICK_RIGHT, // R STICK RIGHT
        21: PS2_KEYS.R_STICK_LEFT,  // R STICK LEFT
        22: PS2_KEYS.R_STICK_DOWN,  // R STICK DOWN
        23: PS2_KEYS.R_STICK_UP     // R STICK UP
    };

    const GP_NAME_TO_INDEX = {
        'BUTTON_1': 0, 'BUTTON_A': 0,
        'BUTTON_2': 1, 'BUTTON_B': 1,
        'BUTTON_3': 2, 'BUTTON_X': 2,
        'BUTTON_4': 3, 'BUTTON_Y': 3,
        'LEFT_TOP_SHOULDER': 4, 'L1': 4, 'LB': 4,
        'RIGHT_TOP_SHOULDER': 5, 'R1': 5, 'RB': 5,
        'LEFT_BOTTOM_SHOULDER': 6, 'L2': 6, 'LT': 6,
        'RIGHT_BOTTOM_SHOULDER': 7, 'R2': 7, 'RT': 7,
        'SELECT': 8, 'BACK': 8,
        'START': 9,
        'LEFT_STICK': 10, 'L3': 10,
        'RIGHT_STICK': 11, 'R3': 11,
        'DPAD_UP': 12,
        'DPAD_DOWN': 13,
        'DPAD_LEFT': 14,
        'DPAD_RIGHT': 15
    };

    function isGpDescriptorActive(gp, desc) {
        if (!gp || !desc) return false;
        if (desc.includes('+')) {
            const parts = desc.split('+');
            return parts.every(part => isGpDescriptorActive(gp, part.trim()));
        }

        const trimmed = desc.trim();
        // Check digital buttons
        const btnIdx = GP_NAME_TO_INDEX[trimmed];
        if (btnIdx !== undefined && gp.buttons && gp.buttons[btnIdx]) {
            const btn = gp.buttons[btnIdx];
            if (btn.pressed || btn.value > 0.45) return true;
        }

        // Check analog axes
        const deadzone = 0.28;
        if (trimmed === 'LEFT_STICK_X:-1') return (gp.axes && gp.axes[0] < -deadzone);
        if (trimmed === 'LEFT_STICK_X:+1') return (gp.axes && gp.axes[0] > deadzone);
        if (trimmed === 'LEFT_STICK_Y:-1') return (gp.axes && gp.axes[1] < -deadzone);
        if (trimmed === 'LEFT_STICK_Y:+1') return (gp.axes && gp.axes[1] > deadzone);
        if (trimmed === 'RIGHT_STICK_X:-1') return (gp.axes && gp.axes[2] < -deadzone);
        if (trimmed === 'RIGHT_STICK_X:+1') return (gp.axes && gp.axes[2] > deadzone);
        if (trimmed === 'RIGHT_STICK_Y:-1') return (gp.axes && gp.axes[3] < -deadzone);
        if (trimmed === 'RIGHT_STICK_Y:+1') return (gp.axes && gp.axes[3] > deadzone);

        return false;
    }

    class PlayInputManager {
        constructor(playModule) {
            this.playModule = playModule;
            this.activeKeys = new Set();
            this.polling = false;
            this.customBindings = null;
            window._jePlayInputManager = this;
        }

        init(customBindings) {
            this.customBindings = customBindings || null;
            this.setupKeyboardPassthrough();
            this.startGamepadPolling();
        }

        getActiveBindings() {
            var binds = window._jeBindings || (window.JellyEmuConfig && window.JellyEmuConfig.customBindings) || this.customBindings || null;
            if (binds && typeof binds === 'object') {
                if (binds[0] && typeof binds[0] === 'object' && binds[0].kb1 === undefined && binds[0].gp1 === undefined) {
                    return binds[0];
                }
                return binds;
            }
            return null;
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
            // Default mapping from physical keyboard keys to PS2 target keys
            const defaultKeyMap = {
                'KeyX': PS2_KEYS.CROSS,          // X -> Cross
                'KeyZ': PS2_KEYS.CIRCLE,         // Z -> Circle
                'KeyA': PS2_KEYS.SQUARE,         // A -> Square
                'KeyS': PS2_KEYS.TRIANGLE,       // S -> Triangle
                'KeyQ': PS2_KEYS.L1,             // Q -> L1
                'KeyE': PS2_KEYS.R1,             // E -> R1
                'Tab': PS2_KEYS.L2,              // Tab -> L2
                'KeyR': PS2_KEYS.R2,             // R -> R2
                'Digit1': PS2_KEYS.L1,           // 1 -> L1
                'Digit2': PS2_KEYS.R1,           // 2 -> R1
                'Digit3': PS2_KEYS.L2,           // 3 -> L2
                'Digit4': PS2_KEYS.R2,           // 4 -> R2
                'Digit8': PS2_KEYS.R2,           // 8 -> R2
                'Digit9': PS2_KEYS.L3,           // 9 -> L3
                'Digit0': PS2_KEYS.R3,           // 0 -> R3
                'KeyV': PS2_KEYS.SELECT,         // V -> Select
                'Enter': PS2_KEYS.START,         // Enter -> Start
                'ArrowUp': PS2_KEYS.DPAD_UP,
                'ArrowDown': PS2_KEYS.DPAD_DOWN,
                'ArrowLeft': PS2_KEYS.DPAD_LEFT,
                'ArrowRight': PS2_KEYS.DPAD_RIGHT,
                'KeyT': PS2_KEYS.L_STICK_UP,
                'KeyG': PS2_KEYS.L_STICK_DOWN,
                'KeyF': PS2_KEYS.L_STICK_LEFT,
                'KeyH': PS2_KEYS.L_STICK_RIGHT,
                'KeyI': PS2_KEYS.R_STICK_UP,
                'KeyK': PS2_KEYS.R_STICK_DOWN,
                'KeyJ': PS2_KEYS.R_STICK_LEFT,
                'KeyL': PS2_KEYS.R_STICK_RIGHT
            };

            const handleKeyEvent = (e, isDown) => {
                if (e.repeat) return;
                const activeBindings = this.getActiveBindings();
                let handled = false;

                if (activeBindings && typeof activeBindings === 'object') {
                    for (const [idStr, bind] of Object.entries(activeBindings)) {
                        const btnId = parseInt(idStr, 10);
                        const k1 = bind && (bind.kb1 || bind.Kb1);
                        const k2 = bind && (bind.kb2 || bind.Kb2);

                        if ((k1 && k1 === e.keyCode) || (k2 && k2 === e.keyCode)) {
                            const psKey = BUTTON_ID_TO_PS2_KEY[btnId];
                            if (psKey) {
                                this.simulateKey(psKey, isDown);
                                handled = true;
                            }
                        }
                    }
                }

                if (!handled) {
                    const mapped = defaultKeyMap[e.code] || defaultKeyMap[e.key];
                    if (mapped) {
                        this.simulateKey(mapped, isDown);
                    }
                }
            };

            window.addEventListener('keydown', (e) => handleKeyEvent(e, true));
            window.addEventListener('keyup', (e) => handleKeyEvent(e, false));
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
                    const activeBindings = this.getActiveBindings();

                    for (let i = 0; i < gamepads.length; i++) {
                        const gp = gamepads[i];
                        if (!gp) continue;

                        if (activeBindings && typeof activeBindings === 'object') {
                            // Map buttons via custom bindings
                            for (const [idStr, bind] of Object.entries(activeBindings)) {
                                const btnId = parseInt(idStr, 10);
                                const gp1 = bind && (bind.gp1 || bind.Gp1);
                                const gp2 = bind && (bind.gp2 || bind.Gp2);
                                const isDown = (gp1 && isGpDescriptorActive(gp, gp1)) || (gp2 && isGpDescriptorActive(gp, gp2));

                                const psKey = BUTTON_ID_TO_PS2_KEY[btnId];
                                if (!psKey) continue;
                                this.simulateKey(psKey, Boolean(isDown));
                            }
                        } else {
                            // Digital Buttons & Triggers default mapping
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
