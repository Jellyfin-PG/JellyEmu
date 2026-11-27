# JellyEmu & RetroKit — Complete Console Input & Core Mapping Reference

This document is the authoritative reference for every input action, numeric ID, string identifier, RetroPad / `retroarch.cfg` key, EmulatorJS `simulateInput` slot, and adapter-level key/bitmask mapping across **JellyEmu** and **RetroKit**.

---

## 1. End-to-End Input Pipeline

```
Physical Keyboard / W3C Gamepad
  │
  ▼
JellyEmu (JellyEmuInputService.cs → PlayerBridge.ts)
  • Numeric Action IDs: 0–15 (Digital Buttons), 16–23 (Analog Stick Directions), 24–35 (Hotkeys)
  • Maps IDs via JE_ID_TO_BUTTON / JE_ID_TO_AXIS & GP_LABEL_TO_INDEX
  │
  ▼
RetroKit (InputManager.ts → PlayerInputChannel.ts)
  • StandardButton ('ACTION_DOWN', 'DPAD_UP', ...) → STANDARD_BUTTON_MAP (0–23)
  • StandardAxis ('L_STICK_X', 'L_STICK_Y', 'R_STICK_X', 'R_STICK_Y') → STANDARD_AXIS_MAP (0–3)
  │
  ├──► LibretroAdapter (web.libretro WASM)
  │      • Dispatches synthetic KeyboardEvents (bubbles: true) to canvas/document
  │      • Backed by retroarch.cfg (input_player1_* + input_libretro_device_p1)
  │
  ├──► EmulatorJSAdapter (EmulatorJS .data WASM)
  │      • Calls gameManager.simulateInput(player, buttonId, value)
  │      • Axes 0–3 map to IDs 16–23 (analog 0–32767)
  │
  ├──► PlayAdapter (Play! PS2 WASM)
  │      • Calls sendGamepadButton/Axis or simulates PS2_KEY_MAP on canvas
  │
  ├──► Pico8Adapter (PICO-8 / Retro8)
  │      • Writes bitmask to window.pico8_buttons[player] (players 0–7)
  │
  ├──► LoveAdapter (love.js)
  │      • Dispatches RETROPAD_TO_KEY KeyboardEvents on canvas/window
  │
  └──► RuffleAdapter (Flash)
         • Dispatches Flash arrow/action KeyboardEvents on window
```

---

## 2. Master Action ID & Cross-Engine Mapping Table (`0–35`)

### 2.1 Digital Buttons (`0–15`)

| ID | `StandardButton` (RetroKit) | RetroPad / `retroarch.cfg` Key | Libretro Fallback Key (`key` / `code` / `keyCode`) | EmulatorJS `simulateInput` | Play! PS2 (`PS2_KEY_MAP`) | PICO-8 Bitmask | LÖVE Key | Ruffle Key |
|---:|---|---|---|---|---|---|---|---|
| **0** | `ACTION_DOWN` | `input_player1_b` | `z` / `KeyZ` / `90` | `0` (B / Cross / A on N64) | `KeyZ` (`90`) | `0x10` (O) | `KeyX` (`88`) | `Space` |
| **1** | `ACTION_LEFT` | `input_player1_y` | `a` / `KeyA` / `65` | `1` (Y / Square / B on N64) | `KeyA` (`65`) | `0x20` (X) | `KeyS` (`83`) | `KeyX` |
| **2** | `SELECT` | `input_player1_select` | `Shift` / `ShiftRight` / `16` | `2` (Select / Coin / Mode) | `Backspace` (`8`) | `0x40` (Menu) | `Space` (`32`) | — |
| **3** | `START` | `input_player1_start` | `Enter` / `Enter` / `13` | `3` (Start / Run / Pause) | `Enter` (`13`) | `0x40` (Menu) | `Enter` (`13`) | `Enter` |
| **4** | `DPAD_UP` | `input_player1_up` | `ArrowUp` / `ArrowUp` / `38` | `4` (D-Pad Up) | `ArrowUp` (`38`) | `0x04` (Up) | `ArrowUp` (`38`) | `ArrowUp` |
| **5** | `DPAD_DOWN` | `input_player1_down` | `ArrowDown` / `ArrowDown` / `40` | `5` (D-Pad Down) | `ArrowDown` (`40`) | `0x08` (Down) | `ArrowDown` (`40`) | `ArrowDown` |
| **6** | `DPAD_LEFT` | `input_player1_left` | `ArrowLeft` / `ArrowLeft` / `37` | `6` (D-Pad Left) | `ArrowLeft` (`37`) | `0x01` (Left) | `ArrowLeft` (`37`) | `ArrowLeft` |
| **7** | `DPAD_RIGHT` | `input_player1_right` | `ArrowRight` / `ArrowRight` / `39` | `7` (D-Pad Right) | `ArrowRight` (`39`) | `0x02` (Right) | `ArrowRight` (`39`) | `ArrowRight` |
| **8** | `ACTION_RIGHT` | `input_player1_a` | `x` / `KeyX` / `88` | `8` (A / Circle) | `KeyX` (`88`) | `0x20` (X) | `KeyZ` (`90`) | `KeyZ` |
| **9** | `ACTION_UP` | `input_player1_x` | `s` / `KeyS` / `83` | `9` (X / Triangle) | `KeyS` (`83`) | `0x10` (O) | `KeyA` (`65`) | — |
| **10** | `L1` | `input_player1_l` | `q` / `KeyQ` / `81` | `10` (L / L1) | `Key1` (`49`) | — | — | — |
| **11** | `R1` | `input_player1_r` | `w` / `KeyW` / `87` | `11` (R / R1) | `Key8` (`56`) | — | — | — |
| **12** | `L2` | `input_player1_l2` | `1` / `Digit1` / `49` | `12` (L2 / Z) | `Key2` (`50`) | — | — | — |
| **13** | `R2` | `input_player1_r2` | `2` / `Digit2` / `50` | `13` (R2) | `Key9` (`57`) | — | — | — |
| **14** | `L3` | `input_player1_l3` | `3` / `Digit3` / `51` | `14` (L3 / Mic) | `Key3` (`51`) | — | — | — |
| **15** | `R3` | `input_player1_r3` | `4` / `Digit4` / `52` | `15` (R3) | `Key0` (`48`) | — | — | — |

### 2.2 Analog Stick Directions (`16–23`)

| ID | `StandardAxis` & Dir | RetroPad / `retroarch.cfg` Key | Libretro Fallback Key (`key` / `code` / `keyCode`) | EmulatorJS `simulateInput` | Play! PS2 (`PS2_KEY_MAP`) |
|---:|---|---|---|---|---|
| **16** | `L_STICK_X` (`+1`) | `input_player1_l_x_plus` | `l` / `KeyL` / `76` *(2D cores: `ArrowRight`)* | `16` (`L STICK RIGHT`, 0–32767) | `KeyH` (`72`) |
| **17** | `L_STICK_X` (`-1`) | `input_player1_l_x_minus` | `j` / `KeyJ` / `74` *(2D cores: `ArrowLeft`)* | `17` (`L STICK LEFT`, 0–32767) | `KeyF` (`70`) |
| **18** | `L_STICK_Y` (`+1`) | `input_player1_l_y_plus` | `k` / `KeyK` / `75` *(2D cores: `ArrowDown`)* | `18` (`L STICK DOWN`, 0–32767) | `KeyG` (`71`) |
| **19** | `L_STICK_Y` (`-1`) | `input_player1_l_y_minus` | `i` / `KeyI` / `73` *(2D cores: `ArrowUp`)* | `19` (`L STICK UP`, 0–32767) | `KeyT` (`84`) |
| **20** | `R_STICK_X` (`+1`) | `input_player1_r_x_plus` | `g` / `KeyG` / `71` | `20` (`R STICK RIGHT`, 0–32767) | `KeyL` (`76`) |
| **21** | `R_STICK_X` (`-1`) | `input_player1_r_x_minus` | `f` / `KeyF` / `70` | `21` (`R STICK LEFT`, 0–32767) | `KeyJ` (`74`) |
| **22** | `R_STICK_Y` (`+1`) | `input_player1_r_y_plus` | `b` / `KeyB` / `66` | `22` (`R STICK DOWN`, 0–32767) | `KeyK` (`75`) |
| **23** | `R_STICK_Y` (`-1`) | `input_player1_r_y_minus` | `t` / `KeyT` / `84` | `23` (`R STICK UP`, 0–32767) | `KeyI` (`73`) |

### 2.3 EmulatorJS & JellyEmu Hotkeys (`24–35`)

| ID | JellyEmu Label | EmulatorJS ID | Default KB1 (KeyCode) | Default KB2 (KeyCode) | RetroKit `StandardHotkey` |
|---:|---|---|---:|---:|---|
| **24** | `QUICK SAVE` | `24` (Quick Save State) | `49` (`1`) | `0` | `QUICK_SAVE` |
| **25** | `QUICK LOAD` | `25` (Quick Load State) | `50` (`2`) | `0` | `QUICK_LOAD` |
| **26** | `CHANGE SLOT` | `26` (Change State Slot) | `51` (`3`) | `0` | — |
| **27** | `FAST FORWARD` | `27` (Fast Forward) | `107` (Numpad `+`) | `9` (`Tab`) | `FAST_FORWARD` |
| **28** | `REWIND` | `28` (Rewind) | `8` (`Backspace`) | `0` | `REWIND` |
| **29** | `SLOW MOTION` | `29` (Slow Motion) | `109` (Numpad `-`) | `0` | `SLOW_MOTION` |
| **30** | `EXIT GAME` | — | `27` (`Escape`) | `0` | — |
| **31** | `PAUSE / RESUME` | — | `32` (`Space`) | `80` (`P`) | `PAUSE` |
| **32** | `RESTART GAME` | — | `115` (`F4`) | `0` | `RESTART` |
| **33** | `MUTE AUDIO` | — | `77` (`M`) | `0` | — |
| **34** | `SCREENSHOT` | — | `123` (`F12`) | `0` | `SCREENSHOT` |
| **35** | `FULLSCREEN` | — | `70` (`F`) | `122` (`F11`) | `FULLSCREEN` |

---

## 3. W3C Standard Gamepad Index Mapping (`GP_LABEL_TO_INDEX`)

| W3C Index | JellyEmu Gamepad Label | Physical Position (Xbox / PlayStation) |
|---:|---|---|
| `0` | `BUTTON_1` | Bottom face button (Xbox **A** / PS **Cross ⨯**) |
| `1` | `BUTTON_2` | Right face button (Xbox **B** / PS **Circle ○**) |
| `2` | `BUTTON_3` | Left face button (Xbox **X** / PS **Square □**) |
| `3` | `BUTTON_4` | Top face button (Xbox **Y** / PS **Triangle △**) |
| `4` | `LEFT_TOP_SHOULDER` | **LB** / **L1** |
| `5` | `RIGHT_TOP_SHOULDER` | **RB** / **R1** |
| `6` | `LEFT_BOTTOM_SHOULDER` | **LT** / **L2** |
| `7` | `RIGHT_BOTTOM_SHOULDER` | **RT** / **R2** |
| `8` | `SELECT` | **View / Back / Share / Select** |
| `9` | `START` | **Menu / Start / Options** |
| `10` | `LEFT_STICK` | **L3** (Left Stick Click) |
| `11` | `RIGHT_STICK` | **R3** (Right Stick Click) |
| `12` | `DPAD_UP` | D-Pad Up |
| `13` | `DPAD_DOWN` | D-Pad Down |
| `14` | `DPAD_LEFT` | D-Pad Left |
| `15` | `DPAD_RIGHT` | D-Pad Right |
| `16` | `HOME` | Xbox Guide / PS Button |

**Analog Axes (`AXIS_NAMES`):**
- Axis `0`: `LEFT_STICK_X` (`-1` Left, `+1` Right)
- Axis `1`: `LEFT_STICK_Y` (`-1` Up, `+1` Down)
- Axis `2`: `RIGHT_STICK_X` (`-1` Left, `+1` Right)
- Axis `3`: `RIGHT_STICK_Y` (`-1` Up, `+1` Down)

---

## 4. Per-Console Controller & Action Mappings

### 4.1 Nintendo Systems

#### NES / Famicom (`nes`) — Cores: `nestopia`, `fceumm`, `quicknes` (`libretro`)
- **Real Controller:** D-Pad, A, B, Select, Start
- **Action IDs:** `4` UP, `5` DOWN, `6` LEFT, `7` RIGHT, `8` A, `0` B, `2` SELECT, `3` START

#### Famicom Disk System (`fds`) — Cores: `nestopia`, `fceumm` (`libretro`)
- **Real Controller:** NES + Disk Side Swap & Eject/Insert
- **Action IDs:** `4–7` D-Pad, `8` A, `0` B, `2` SELECT, `3` START, `10` SWAP DISKS, `11` EJECT/INSERT DISK

#### Super Nintendo (`snes`) — Cores: `snes9x`, `snes9x2010`, `snes9x2005`, `snes9x2002` (`libretro`)
- **Real Controller:** D-Pad, A (Right), B (Bottom), X (Top), Y (Left), L, R, Select, Start
- **Action IDs:** `4` UP, `5` DOWN, `6` LEFT, `7` RIGHT, `8` A, `0` B, `9` X, `1` Y, `10` L, `11` R, `2` SELECT, `3` START

#### Nintendo 64 (`n64`) — Cores: `mupen64plus_next`, `parallel_n64` (`emulatorjs`)
- **Real Controller:** Analog Stick, D-Pad, A, B, C-Up/Down/Left/Right, L, R, Z, Start
- **Action IDs:**
  - `4` D-PAD UP, `5` D-PAD DOWN, `6` D-PAD LEFT, `7` D-PAD RIGHT
  - `0` A, `1` B, `3` START, `10` L, `11` R, `12` Z
  - `19` STICK UP, `18` STICK DOWN, `17` STICK LEFT, `16` STICK RIGHT
  - `23` C-PAD UP, `22` C-PAD DOWN, `21` C-PAD LEFT, `20` C-PAD RIGHT
- **Notes:** N64 maps **A** to ID `0` and **B** to ID `1` in EmulatorJS (`mupen64plus_next`).

#### GameCube (`gamecube`) — Scheme in `JellyEmuInputService`
- **Real Controller:** Control Stick, C-Stick, D-Pad, A, B, X, Y, Z, L, R, Start
- **Action IDs:**
  - `0` A, `1` B, `8` X, `9` Y, `3` START, `11` Z, `12` L, `13` R
  - `4–7` D-Pad, `19/18/17/16` MAIN STICK, `23/22/21/20` C-STICK

#### Game Boy / Game Boy Color (`gb`, `gbc`) — Cores: `gambatte`, `gearboy`, `tgbdual`, `DoubleCherryGB` (`libretro`)
- **Real Controller:** D-Pad, A, B, Select, Start
- **Action IDs:** `4` UP, `5` DOWN, `6` LEFT, `7` RIGHT, `8` A, `0` B, `2` SELECT, `3` START

#### Game Boy Advance (`gba`) — Cores: `mgba`, `vba_next` (`libretro`)
- **Real Controller:** D-Pad, A, B, L, R, Select, Start
- **Action IDs:** `4` UP, `5` DOWN, `6` LEFT, `7` RIGHT, `8` A, `0` B, `10` L, `11` R, `2` SELECT, `3` START

#### Nintendo DS (`nds`) — Cores: `desmume`, `melonds` (`emulatorjs`)
- **Real Controller:** D-Pad, A, B, X, Y, L, R, Select, Start, Touchscreen (Pointer), Microphone
- **Action IDs:** `4–7` D-Pad, `8` A, `0` B, `9` X, `1` Y, `10` L, `11` R, `2` SELECT, `3` START, `14` MICROPHONE

#### Nintendo 3DS (`3ds`) — Core: `azahar` (`emulatorjs`)
- **Real Controller:** Circle Pad, C-Stick, D-Pad, A, B, X, Y, L, R, ZL, ZR, Select, Start, Touchscreen
- **Action IDs:** `4–7` D-Pad, `8` A, `0` B, `9` X, `1` Y, `10` L, `11` R, `12` ZL, `13` ZR, `2` SELECT, `3` START, `19/18/17/16` CIRCLE PAD, `23/22/21/20` C-STICK

#### Virtual Boy (`virtualboy`) — Core: `mednafen_vb` (`libretro`)
- **Real Controller:** Left D-Pad, Right D-Pad, A, B, L, R, Select, Start
- **Action IDs:** `4–7` LEFT D-PAD, `19/18/17/16` RIGHT D-PAD, `8` A, `0` B, `10` L, `11` R, `2` SELECT, `3` START

---

### 4.2 Sony Systems

#### PlayStation 1 (`psx` / `ps1`) — Core: `pcsx_rearmed` (`libretro`)
- **Real Controller (DualShock):** D-Pad, Cross, Circle, Square, Triangle, L1, R1, L2, R2, L3, R3, Left Stick, Right Stick, Select, Start
- **Controller Device IDs (`input_libretro_device_p1`, extracted from `pcsx_rearmed_libretro.wasm`):**
  - `1` (`0x1`): `standard` (Digital PlayStation Controller)
  - `517` (`0x205`): `dualshock` (DualShock Analog + L3/R3 + Rumble)
  - `258` (`0x102`): `mouse` (PlayStation Mouse)
  - `260` (`0x104`): `guncon` (Namco GunCon)
  - `773` (`0x305`): `negcon` (Namco neGcon)
- **Action IDs (`dev=1` Joypad + `dev=5` Analog):**
  - `0` ⨯ CROSS, `8` ○ CIRCLE, `1` □ SQUARE, `9` △ TRIANGLE
  - `2` SELECT, `3` START, `4–7` D-Pad (`6` Left, `4` Up, `5` Down, `7` Right)
  - `10` L1, `11` R1, `12` L2, `13` R2, `14` L3, `15` R3
  - `dev=5, idx=0, id=0/1` Left Analog X/Y (`17/16` Left/Right, `19/18` Up/Down)
  - `dev=5, idx=1, id=0/1` Right Analog X/Y (`21/20` Left/Right, `23/22` Up/Down)
  - `dev=4, idx=0` Lightgun: `2` Gun Trigger, `16` Gun Reload, `3` Gun Aux A, `4` Gun Aux B, `6` Gun Start

#### PlayStation 2 (`ps2`) — Core: `play` (`play` / Play! WASM)
- **Real Controller (DualShock 2):** Same layout as DualShock (`0–23`), mapped via `PlayAdapter.PS2_KEY_MAP` or `sendGamepadButton`/`sendGamepadAxis`.

#### PlayStation Portable (`psp`) — Core: `ppsspp` (`emulatorjs`)
- **Real Controller:** D-Pad, Analog Nub, Cross, Circle, Square, Triangle, L, R, Select, Start
- **Action IDs:** `0` ⨯ CROSS, `8` ○ CIRCLE, `1` □ SQUARE, `9` △ TRIANGLE, `2` SELECT, `3` START, `4–7` D-Pad, `10` L, `11` R, `19/18/17/16` STICK

---

### 4.3 Sega Systems

#### Sega Master System / Game Gear (`mastersystem`, `gamegear`) — Core: `gearsystem` (`libretro`)
- **Real Controller:** D-Pad, Button 1, Button 2, Start/Pause
- **Action IDs:** `4–7` D-Pad, `0` BUTTON 1 / START, `8` BUTTON 2 (`gamegear` also has `3` START)

#### Sega Genesis / Mega Drive / Sega CD / 32X (`genesis`, `segacd`, `32x`) — Cores: `genesis_plus_gx`, `genesis_plus_gx_wide`, `picodrive`, `clownmdemu` (`libretro`)
- **Real Controller (6-Button):** D-Pad, A, B, C, X, Y, Z, Mode, Start
- **Action IDs:** `4–7` D-Pad, `1` A, `0` B, `8` C, `10` X, `9` Y, `11` Z, `2` MODE, `3` START

#### Sega Saturn (`saturn`) — Core: `yabause` (`emulatorjs`)
- **Real Controller:** D-Pad, A, B, C, X, Y, Z, L, R, Start
- **Action IDs:** `4–7` D-Pad, `1` A, `0` B, `8` C, `9` X, `10` Y, `11` Z, `12` L, `13` R, `3` START

#### Sega Dreamcast (`dreamcast`) — Scheme in `JellyEmuInputService`
- **Real Controller:** Analog Stick, D-Pad, A, B, X, Y, L Trigger, R Trigger, Start
- **Action IDs:** `4–7` D-Pad, `0` A, `8` B, `1` X, `9` Y, `3` START, `12` L TRIGGER, `13` R TRIGGER, `19/18/17/16` ANALOG

---

### 4.4 Atari Systems

| System | Core(s) | Engine | Action IDs |
|---|---|---|---|
| **Atari 2600** (`atari2600`) | `tia` | `libretro` | `4–7` D-Pad, `0` FIRE, `2` SELECT, `3` RESET, `10` L DIFF A, `12` L DIFF B, `11` R DIFF A, `13` R DIFF B, `14` COLOR, `15` B/W |
| **Atari 5200** (`atari5200`) | `a5200` | `emulatorjs` | `4–7` D-Pad, `19/18/17/16` STICK, `0` FIRE (BOTTOM), `8` FIRE 2 (TOP), `3` START, `2` PAUSE, `9` RESET |
| **Atari 7800** (`atari7800`) | `prosystem` | `emulatorjs` | `4–7` D-Pad, `0` BUTTON 1, `8` BUTTON 2, `2` SELECT, `3` PAUSE, `9` RESET, `10` LEFT DIFF, `11` RIGHT DIFF |
| **Atari Lynx** (`lynx`) | `handy`, `mednafen_lynx` | `libretro` | `4–7` D-Pad, `8` A, `0` B, `10` OPTION 1, `11` OPTION 2, `3` START |
| **Atari Jaguar** (`jaguar`) | `virtualjaguar` | `emulatorjs` | `4–7` D-Pad, `8` A, `0` B, `1` C, `2` PAUSE, `3` OPTION |

---

### 4.5 NEC, SNK, Bandai, Arcade, Computers & Fantasy Consoles

| System | Core(s) | Engine | Action IDs |
|---|---|---|---|
| **PC Engine / TG-16** (`pce`) | `mednafen_pce_fast`, `geargrafx` | `libretro` | `4–7` D-Pad, `8` I, `0` II, `2` SELECT, `3` RUN |
| **PC-FX** (`pcfx`) | `mednafen_pcfx` | `emulatorjs` | `4–7` D-Pad, `8` I, `0` II, `9` III, `1` IV, `10` V, `11` VI, `3` RUN, `2` SELECT, `12` MODE1, `13` MODE2 |
| **Neo Geo Pocket / Color** (`ngp`) | `mednafen_ngp` | `libretro` | `4–7` D-Pad, `0` A, `8` B, `3` OPTION |
| **Neo Geo CD** (`neocd`) | `neocd` | `libretro` | Standard RetroPad (`0–11`) |
| **WonderSwan / Color** (`wswan`) | `mednafen_wswan` | `libretro` | `4–7` X1–X4 (X UP/DOWN/LEFT/RIGHT), `13/12/10/11` Y1–Y4 (Y UP/DOWN/LEFT/RIGHT), `8` A, `0` B, `3` START |
| **3DO** (`3do`) | `opera` | `emulatorjs` | `4–7` D-Pad, `1` A, `0` B, `8` C, `10` L, `11` R, `2` X (Stop), `3` P (Play/Pause) |
| **ColecoVision** (`colecovision`) | `gearcoleco` | `emulatorjs` | `4–7` D-Pad, `8` LEFT BUTTON, `0` RIGHT BUTTON, `9,1,11,10,13,12,15,14` Keypad `1–8`, `2` `*`, `3` `#` |
| **Intellivision** (`intellivision`) | `freeintv` | `emulatorjs` | Standard RetroPad (`0–11`) |
| **Vectrex** (`vectrex`) | `vecx` | `libretro` | Standard RetroPad (`0–9`) |
| **Arcade** (`arcade`) | `fbneo`, `mame2003_plus`, `mame2000`, `mame2003`, `fbalpha2012*` | `libretro` / `emulatorjs` | `4–7` Stick/D-Pad, `0` B, `1` Y, `8` A, `9` X, `10` L, `11` R, `12` L2, `13` R2, `14` L3, `15` R3, `2` INSERT COIN, `3` START, `16–23` Sticks |
| **DOS / Amiga / C64** (`dos`, `amiga`, `c64`) | `dosbox_pure`, `puae`, `vice_x64sc`, `vice_*` | `emulatorjs` / `libretro` | Full keyboard passthrough + mouse pointer + RetroPad (`0–23`) |
| **PICO-8** (`pico8`) | `pico8` / `retro8` | `pico8` / `libretro` | `4–7` D-Pad (`0x04,0x08,0x01,0x02`), `0` O (`0x10`), `8` X (`0x20`), `3` PAUSE/MENU (`0x40`) |
| **LÖVE** (`love`) | `love` | `love` | `4–7` Arrow keys, `8` `Z`, `0` `X`, `9` `A`, `1` `S`, `2` `Space`, `3` `Enter` |
| **Adobe Flash** (`flash`) | `ruffle` | `ruffle` | `4–7` Arrow keys, `0` `Space`, `8` `Z`, `1` `X`, `3` `Enter` |

---

## 5. WASM-Verified Libretro `retro_input_descriptor` & `retro_controller_description`

Extracted directly from the compiled `.wasm` data segments (`gambatte_libretro.wasm`, `mgba_libretro.wasm`, `pcsx_rearmed_libretro.wasm`, `snes9x_libretro.wasm`):

### `gambatte_libretro.wasm` (GB / GBC)
- `port=0 dev=1 idx=0`: `id=6` `"D-Pad Left"`, `id=4` `"D-Pad Up"`, `id=5` `"D-Pad Down"`, `id=7` `"D-Pad Right"`, `id=0` `"B"`, `id=8` `"A"`, `id=1` `"Turbo B"`, `id=9` `"Turbo A"`, `id=2` `"Select"`, `id=3` `"Start"`

### `mgba_libretro.wasm` (GBA)
- `port=0 dev=1 idx=0`: `id=8` `"A"`, `id=0` `"B"`, `id=9` `"Turbo A"`, `id=1` `"Turbo B"`, `id=2` `"Select"`, `id=3` `"Start"`, `id=7` `"Right"`, `id=6` `"Left"`, `id=4` `"Up"`, `id=5` `"Down"`, `id=11` `"R"`, `id=10` `"L"`, `id=13` `"Turbo R"`, `id=12` `"Turbo L"`, `id=15` `"Brighten Solar Sensor"`, `id=14` `"Darken Solar Sensor"`

### `snes9x_libretro.wasm` (SNES)
- **Controller Types:** `1` (`SNES Joypad`), `2` (`SNES Mouse`), `257` (`0x101`, `Multitap`)
- `port=0 dev=1 idx=0`: `id=6` `"D-Pad Left"`, `id=4` `"D-Pad Up"`, `id=5` `"D-Pad Down"`, `id=7` `"D-Pad Right"`, `id=0` `"B"`, `id=8` `"A"`, `id=9` `"X"`, `id=1` `"Y"`, `id=10` `"L"`, `id=11` `"R"`, `id=2` `"Select"`, `id=3` `"Start"`

### `pcsx_rearmed_libretro.wasm` (PS1)
- **Controller Types:** `1` (`standard`), `517` (`0x205`, `dualshock`), `258` (`0x102`, `mouse`), `260` (`0x104`, `guncon`), `773` (`0x305`, `negcon`)
- `port=0 dev=1 idx=0`: `id=6` `"D-Pad Left"`, `id=4` `"D-Pad Up"`, `id=5` `"D-Pad Down"`, `id=7` `"D-Pad Right"`, `id=0` `"Cross"`, `id=8` `"Circle"`, `id=9` `"Triangle"`, `id=1` `"Square"`, `id=10` `"L1"`, `id=12` `"L2"`, `id=14` `"L3"`, `id=11` `"R1"`, `id=13` `"R2"`, `id=15` `"R3"`, `id=2` `"Select"`, `id=3` `"Start"`
- `port=0 dev=5`: `idx=0 id=0` `"Left Analog X"`, `idx=0 id=1` `"Left Analog Y"`, `idx=1 id=0` `"Right Analog X"`, `idx=1 id=1` `"Right Analog Y"`
- `port=0 dev=4 idx=0`: `id=2` `"Gun Trigger"`, `id=16` `"Gun Reload"`, `id=3` `"Gun Aux A"`, `id=4` `"Gun Aux B"`, `id=6` `"Gun Start"`
