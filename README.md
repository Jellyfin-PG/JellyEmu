<div align="center">
  <img src="assets/jellyemu.svg" alt="JellyEmu Logo" width="120" />
  <h1>JellyEmu</h1>
  <p>A plugin for jellyfin 10.11+ to import, play and share your roms with users.</p>
</div>

---

## Screenshots

<p align="center">
  <a href="assets/screen01.png">
    <img src="assets/screen01.png" width="45%" alt="Jellyemu details page" />
  </a>
   
  <a href="assets/screen02.png">
    <img src="assets/screen02.png" width="45%" alt="Jellyemu playing" />
  </a>

  <a href="assets/screen03.png">
    <img src="assets/screen03.png" width="45%" alt="Jellyemu home page" />
  </a>
</p>
<p align="center">
  <em>Click on an image to view it full size.</em>
</p>

---

# JellyEmu Plugin Setup Guide

This guide details the process for integrating an emulation collection into Jellyfin for metadata management and direct playback.

---

## 1. Plugin Configuration
Before adding your media, you must configure the plugin to communicate with external game databases.

* Navigate to **Dashboard** and select **Plugins**.
* Locate and select the **Emulator Library** configuration page.
* Input your **IGDB API Key** (Client ID and Client Secret).
* Input your **RAWG API Key**.
* Save your changes. This ensures the metadata providers are authenticated and ready to fetch game data.

---

## 2. Library Creation
With the plugin configured, you can now create the library to house your ROMs.

* Go to **Dashboard** and select **Libraries**.
* Click **Add Media Library**.
* Select **Books** as the Content Type. Note: This specific type is required for the plugin to map game data correctly within the Jellyfin database.
* Assign a name such as **Video Games** and add the folder path where your ROMs are stored.
* Under the **Metadata Downloaders** section, ensure that **IGDB** and **RAWG** are checked/enabled.
* Finalize the library creation by clicking **OK**.

---

## 3. Supported Platforms and Extensions
Ensure your files are placed in the library folder using the supported extensions for each platform.

| Platform | Common Extensions |
| :--- | :--- |
| **Nintendo (NES)** | .nes |
| **Super Nintendo (SNES)** | .sfc, .smc |
| **Game Boy (GB)** | .gb |
| **Game Boy Color (GBC)** | .gbc |
| **Game Boy Advance (GBA)** | .gba |
| **Virtual Boy (VB)** | .vb |
| **Nintendo DS (NDS)** | .nds |
| **Nintendo 64 (N64)** | .n64, .z64 |
| **PlayStation (PSX)** | .iso, .bin, .cue, .chd |
| **PlayStation Portable (PSP)** | .iso, .cso |
| **Sega Genesis / Mega Drive** | .md, .gen, .bin |
| **Sega CD** | .iso, .bin, .cue, .chd |
| **Sega Game Gear** | .gg |
| **Sega Saturn** | .iso, .bin, .cue |
| **Sega 32X** | .32x |
| **Sega Master System** | .sms |
| **Atari 2600** | .a26, .bin |
| **Atari 5200** | .a52, .bin |
| **Atari 7800** | .a78, .bin |
| **Atari Jaguar** | .j64, .jag |
| **Atari Lynx** | .lnx |
| **MAME / Arcade / CPS** | .zip, .7z |
| **3DO** | .iso, .bin, .cue |
| **Commodore 64** | .d64, .g64, .t64 |
| **Commodore 128** | .d64, .d81 |
| **Commodore PET** | .d64, .t64 |
| **Commodore Plus/4** | .d64 |
| **Commodore VIC-20** | .d64 |
| **Amiga** | .adf, .ipf, .lha |
| **ColecoVision** | .col, .rom |
| **PC Engine / TurboGrafx-16** | .pce, .bin, .cue |
| **PC-FX** | .bin, .cue |
| **Neo Geo Pocket** | .ngp, .ngc |
| **WonderSwan** | .ws, .wsc |
| **DOS** | .exe, .com, .conf, .zip |

---

## 4. Scanning and Playback
Once the library is established and the providers are enabled:

* Go to **Dashboard** and select **Libraries**.
* Click the three dots on your game library and select **Scan Library Files**.
* Jellyfin will begin matching your files against IGDB and RAWG to download box art and descriptions.
* Once the scan finishes, your games will appear on the home screen, ready to be browsed and played.
