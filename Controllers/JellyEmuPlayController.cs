using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Entities;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves the EmulatorJS HTML play page, ROM files, and core resolution.
    /// Routes: /jellyemu/play/*, /jellyemu/rom/*, /jellyemu/core/*
    /// </summary>
    public class JellyEmuPlayController : JellyEmuBaseController
    {
        public JellyEmuPlayController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuPlayController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Returns a standalone EmulatorJS HTML page for the given item.
        /// No authentication required — the ROM is fetched via /jellyemu/rom/{itemId}.
        /// 
        /// Path: GET /jellyemu/play/{itemId}
        /// Parameters: 
        ///   - itemId (string, path): The unique ID of the library item.
        ///   - userId (string, query, optional): Allows wire up of per-user save states.
        /// Returns Example: `200 OK` (Content-Type: text/html)
        /// </summary>
        [HttpGet("/jellyemu/play/{itemId}")]
        [Produces(MediaTypeNames.Text.Html)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Play(string itemId, [FromQuery] string? userId, [FromQuery] int? slot,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
            {
                Logger.LogWarning("[JellyEmu] Play: item {ItemId} not found", itemId);
                return NotFound();
            }

            var core = ResolveCore(item);
            var ext = !string.IsNullOrEmpty(item.Path) ? Path.GetExtension(item.Path) : ".zip";
            var romUrl = $"/jellyemu/rom/{itemId}/{itemId}{ext}";

            var hasSaves = !string.IsNullOrEmpty(userId);
            var userPrefs = hasSaves ? ReadUserPrefs(userId!) : new UserPrefs(1, string.Empty, 0);
            var fullPrefs = hasSaves ? ReadFullPrefs(userId!) : DefaultFullPrefs;
            var activeSlot = (slot.HasValue && slot.Value > 0) ? slot.Value : userPrefs.Slot;
            var activeShader = userPrefs.Shader;
            var videoRotation = userPrefs.VideoRotation;
            var savedControls = fullPrefs.Controls;           // keyboard bindings JSON
            var savedControllerControls = fullPrefs.ControllerControls; // gamepad bindings JSON
            var saveGetUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";
            var savePostUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";

            var saveExists = hasSaves && System.IO.File.Exists(GetSavePath(userId!, itemId, activeSlot));

            var igdbId = item.GetProviderId("IGDB");
            var netplayServer = Plugin.Instance?.Configuration.NetplayServer ?? string.Empty;
            var hasNetplay = !string.IsNullOrWhiteSpace(netplayServer);

            var gameName = HtmlEncoder.Default.Encode(item.Name);
            var ejsBase = EjsManager.IsReady
                ? $"/jellyemu/ejs"
                : JellyEmuEjsManager.CdnBase;

            // Cheats are now loaded lazily client-side when the user opens the cheats popup.
            // Do not fetch server-side — injecting the full list into EJS_cheats on startup causes freezes.

            // Handle direct loading of a save state
            var directLoadScript = (slot.HasValue && slot.Value > 0 && hasSaves) ? $@"
                setTimeout(function() {{
                    fetch('/jellyemu/save/{itemId}/{userId}?slot={slot.Value}')
                        .then(function(r) {{ if (r.ok) return r.arrayBuffer(); throw new Error('No save data'); }})
                        .then(function(buf) {{
                            var g = gm(); if (g) g.loadState(new Uint8Array(buf));
                            console.log('[JellyEmu] Pipeline STAGE 4 (Client Direct Receive): Downloaded bytes ->', buf.byteLength);
                        }}).catch(function(e) {{ console.warn('[JellyEmu] Direct load failed:', e); }});
                }}, 500);" : "";

            var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{gameName}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{ width: 100%; height: 100%; background: #000; overflow: hidden; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #fff; }}
        #game {{ width: 100%; height: 100%; }}

        /* ── Hide native EJS UI (specific selectors only — preserve ejs_parent for keyboard focus) ── */
        /* Keep the elements in the DOM but hide them from view */
        .ejs_menu_bar, 
        .ejs_parent > div:not(.ejs_canvas_parent),
        #je-loader + .ejs_parent .ejs_menu_bar {{
            display: none !important;
            visibility: hidden !important;
            opacity: 0 !important;
            pointer-events: none !important;
        }}
        .ejs_bottom_bar_area, .ejs_loading_text, .ejs_start_button,
        .ejs_settings_parent, .ejs_cheat_parent, .ejs_menu_bar_area,
        .ejs_control_bar, .ejs_menu_bar, .ejs_menu_button, .ejs_bar_top {{ display: none !important; }}

        /* ── Loading Screen ── */
        #je-loader {{ position: fixed; inset: 0; z-index: 99999; background: #000; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 24px; transition: opacity .4s ease, transform .4s ease; }}
        #je-loader.je-dismiss {{ opacity: 0; transform: scale(1.03); pointer-events: none; }}
        #je-loader-title {{ font-size: 28px; font-weight: 700; text-align: center; padding: 0 20px; }}
        #je-loader-system {{ display: inline-block; padding: 4px 16px; border-radius: 20px; background: rgba(255,255,255,.12); font-size: 13px; text-transform: uppercase; letter-spacing: 2px; }}
        #je-loader-status {{ font-size: 14px; opacity: .6; }}
        .je-spinner {{ width: 56px; height: 56px; border: 3px solid rgba(255,255,255,.15); border-top-color: #fff; border-radius: 50%; animation: je-spin 1s linear infinite; }}
        @keyframes je-spin {{ to {{ transform: rotate(360deg); }} }}
        .je-pulse-ring {{ position: absolute; width: 90px; height: 90px; border-radius: 50%; border: 2px solid rgba(255,255,255,.08); animation: je-pulse 2s ease-out infinite; }}
        .je-pulse-ring:nth-child(2) {{ animation-delay: .6s; }}
        .je-pulse-ring:nth-child(3) {{ animation-delay: 1.2s; }}
        @keyframes je-pulse {{ 0% {{ transform: scale(.8); opacity: 1; }} 100% {{ transform: scale(2); opacity: 0; }} }}
        .je-loader-anim {{ position: relative; display: flex; align-items: center; justify-content: center; width: 90px; height: 90px; }}

        /* ── Shared Dock Styles ── */
        .je-bar {{ position: fixed; z-index: 90000; transition: opacity .3s ease, transform .3s ease; }}
        .je-bar.je-hidden {{ opacity: 0; pointer-events: none; }}

        /* ── Top Bar ── */
        #je-topbar {{ top: 0; left: 0; right: 0; height: 48px; display: none; align-items: center; justify-content: space-between; padding: 0 16px; background: rgba(0,0,0,.78); backdrop-filter: blur(14px); -webkit-backdrop-filter: blur(14px); border-bottom: 1px solid rgba(255,255,255,.08); }}
        #je-topbar.je-active {{ display: flex; }}
        #je-topbar-title {{ font-size: 15px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 60%; }}
        .je-topbtn {{ background: none; border: none; color: #fff; cursor: pointer; padding: 8px; border-radius: 8px; display: flex; align-items: center; gap: 6px; font-size: 13px; transition: background .15s; }}
        .je-topbtn:hover {{ background: rgba(255,255,255,.1); }}
        .je-topbtn:active {{ transform: scale(.93); }}
        .je-topbtn svg {{ width: 20px; height: 20px; fill: currentColor; }}

        /* ── Bottom Dock ── */
        #je-dock {{ bottom: 16px; left: 50%; transform: translateX(-50%); display: none; align-items: center; gap: 4px; padding: 6px 10px; border-radius: 28px; background: rgba(0,0,0,.78); backdrop-filter: blur(14px); -webkit-backdrop-filter: blur(14px); border: 1px solid rgba(255,255,255,.1); }}
        #je-dock.je-active {{ display: flex; }}
        #je-dock.je-hidden {{ transform: translateX(-50%) translateY(20px); }}
        .je-dockbtn {{ background: none; border: none; color: #fff; cursor: pointer; width: 42px; height: 42px; border-radius: 50%; display: flex; align-items: center; justify-content: center; transition: background .15s, transform .1s; position: relative; }}
        .je-dockbtn:hover {{ background: rgba(255,255,255,.12); }}
        .je-dockbtn:active {{ transform: scale(.88); }}
        .je-dockbtn:disabled {{ opacity: 0.4; cursor: not-allowed; }}
        .je-dockbtn:disabled:hover {{ background: none; }}
        .je-dockbtn:disabled:active {{ transform: none; }}
        .je-dockbtn svg {{ width: 22px; height: 22px; fill: currentColor; }}
        .je-dockbtn.je-active {{ background: rgba(255,255,255,.2); }}
        .je-dock-sep {{ width: 1px; height: 24px; background: rgba(255,255,255,.15); margin: 0 2px; flex-shrink: 0; }}

        /* ── Popup / Modal ── */
        .je-overlay {{ position: fixed; inset: 0; z-index: 95000; background: rgba(0,0,0,.6); backdrop-filter: blur(4px); display: none; align-items: center; justify-content: center; }}
        .je-overlay.je-open {{ display: flex; }}
        .je-popup {{ background: rgba(20,20,20,.95); backdrop-filter: blur(20px); border: 1px solid rgba(255,255,255,.1); border-radius: 16px; width: 90%; max-width: 480px; max-height: 80vh; display: flex; flex-direction: column; animation: je-pop-in .2s ease; }}
        .je-popup-lg {{ max-width: 680px; }}
        @keyframes je-pop-in {{ from {{ opacity: 0; transform: scale(.95); }} to {{ opacity: 1; transform: scale(1); }} }}
        .je-popup-hdr {{ display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid rgba(255,255,255,.08); flex-shrink: 0; }}
        .je-popup-hdr h3 {{ font-size: 16px; font-weight: 600; }}
        .je-closebtn {{ background: none; border: none; color: #fff; font-size: 22px; cursor: pointer; padding: 4px 8px; border-radius: 8px; line-height: 1; }}
        .je-closebtn:hover {{ background: rgba(255,255,255,.1); }}
        .je-popup-body {{ padding: 16px 20px; overflow-y: auto; flex: 1; }}

        /* ── Save Slots ── */
        .je-slot {{ display: flex; align-items: center; gap: 12px; padding: 10px 12px; border-radius: 10px; border: 1px solid rgba(255,255,255,.08); margin-bottom: 8px; transition: border-color .2s; }}
        .je-slot.je-slot-active {{ border-color: rgba(255,255,255,.35); }}
        .je-slot-num {{ width: 32px; height: 32px; border-radius: 8px; background: rgba(255,255,255,.08); display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 14px; flex-shrink: 0; }}
        .je-slot-thumb {{ width: 64px; height: 48px; border-radius: 6px; background: rgba(255,255,255,.05); overflow: hidden; flex-shrink: 0; display: flex; align-items: center; justify-content: center; font-size: 10px; opacity: .5; }}
        .je-slot-thumb img {{ width: 100%; height: 100%; object-fit: cover; }}
        .je-slot-info {{ flex: 1; min-width: 0; }}
        .je-slot-info small {{ opacity: .5; font-size: 11px; }}
        .je-slot-actions {{ display: flex; gap: 6px; flex-shrink: 0; }}
        .je-btn {{ padding: 6px 14px; border-radius: 8px; border: 1px solid rgba(255,255,255,.15); background: rgba(255,255,255,.06); color: #fff; cursor: pointer; font-size: 12px; transition: background .15s; }}
        .je-btn:hover {{ background: rgba(255,255,255,.14); }}
        .je-btn-primary {{ background: rgba(255,255,255,.18); border-color: rgba(255,255,255,.25); }}

        /* ── Volume ── */
        .je-vol-wrap {{ display: flex; flex-direction: column; gap: 16px; align-items: center; }}
        .je-vol-pct {{ font-size: 36px; font-weight: 700; }}
        .je-vol-slider {{ width: 100%; -webkit-appearance: none; appearance: none; height: 6px; border-radius: 3px; background: rgba(255,255,255,.15); outline: none; }}
        .je-vol-slider::-webkit-slider-thumb {{ -webkit-appearance: none; width: 20px; height: 20px; border-radius: 50%; background: #fff; cursor: pointer; }}

        /* ── Cheats ── */
        .je-cheat-row {{ display: flex; align-items: center; gap: 10px; padding: 8px 0; border-bottom: 1px solid rgba(255,255,255,.06); }}
        .je-cheat-row:last-child {{ border-bottom: none; }}
        .je-cheat-name {{ flex: 1; font-size: 13px; }}
        .je-cheat-del {{ background: none; border: none; color: rgba(255,255,255,.4); cursor: pointer; font-size: 18px; padding: 2px 6px; }}
        .je-cheat-del:hover {{ color: #f44; }}
        .je-cheat-add {{ display: flex; gap: 8px; margin-top: 12px; }}
        .je-cheat-add input, .je-cheat-add textarea {{ flex: 1; background: rgba(255,255,255,.08); border: 1px solid rgba(255,255,255,.12); border-radius: 8px; padding: 8px 10px; color: #fff; font-size: 13px; resize: none; }}

        /* ── Toggle Switch ── */
        .je-toggle {{ position: relative; width: 40px; height: 22px; flex-shrink: 0; }}
        .je-toggle input {{ opacity: 0; width: 0; height: 0; }}
        .je-toggle-track {{ position: absolute; inset: 0; border-radius: 11px; background: rgba(255,255,255,.15); transition: background .2s; cursor: pointer; }}
        .je-toggle-track::after {{ content: ''; position: absolute; top: 3px; left: 3px; width: 16px; height: 16px; border-radius: 50%; background: #fff; transition: transform .2s; }}
        .je-toggle input:checked + .je-toggle-track {{ background: rgba(100,200,255,.6); }}
        .je-toggle input:checked + .je-toggle-track::after {{ transform: translateX(18px); }}

        /* ── Settings Rows ── */
        .je-setting {{ display: flex; align-items: center; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid rgba(255,255,255,.06); }}
        .je-setting:last-child {{ border-bottom: none; }}
        .je-setting-label {{ font-size: 13px; }}
        .je-setting select {{ background: rgba(30,30,30,.95); border: 1px solid rgba(255,255,255,.2); border-radius: 8px; padding: 6px 10px; color: #fff; font-size: 13px; -webkit-appearance: none; appearance: none; background-image: url(""data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' fill='white' viewBox='0 0 24 24'%3E%3Cpath d='M7 10l5 5 5-5z'/%3E%3C/svg%3E""); background-repeat: no-repeat; background-position: right 8px center; padding-right: 28px; cursor: pointer; }}
        .je-setting select option {{ background: #1a1a1a; color: #fff; padding: 6px; }}
        .je-section-title {{ font-size: 11px; text-transform: uppercase; letter-spacing: 1.5px; opacity: .4; margin: 16px 0 8px; }}
        .je-section-title:first-child {{ margin-top: 0; }}

        /* ── Input Mapping ── */
        .je-tabs {{ display: flex; gap: 0; border-bottom: 1px solid rgba(255,255,255,.1); margin-bottom: 12px; }}
        .je-tab {{ padding: 8px 16px; font-size: 13px; cursor: pointer; border-bottom: 2px solid transparent; opacity: .5; transition: opacity .2s; background: none; border-top: none; border-left: none; border-right: none; color: #fff; }}
        .je-tab.je-tab-active {{ opacity: 1; border-bottom-color: #fff; }}
        .je-tab-panel {{ display: none; }}
        .je-tab-panel.je-tab-active {{ display: block; }}
        /* ── Input Mapping Grid ── */
        .je-bind-headers {{ display: grid; grid-template-columns: 1fr 90px 90px; gap: 10px; padding-bottom: 8px; border-bottom: 1px solid rgba(255,255,255,.2); font-size: 11px; text-transform: uppercase; opacity: .6; margin-bottom: 8px; text-align: center; }}
        .je-bind-headers span:first-child {{ text-align: left; }}
        .je-bind-row {{ display: grid; grid-template-columns: 1fr 90px 90px; gap: 10px; align-items: center; padding: 8px 0; border-bottom: 1px solid rgba(255,255,255,.06); font-size: 13px; }}
        .je-bind-label {{ overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
        .je-bind-key {{ padding: 4px 6px; border-radius: 6px; background: rgba(255,255,255,.08); border: 1px solid rgba(255,255,255,.12); cursor: pointer; text-align: center; font-size: 11px; transition: background .2s; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
        .je-bind-key:hover {{ background: rgba(255,255,255,.15); }}
        .je-bind-key.je-listening {{ background: rgba(100,200,255,.2); border-color: rgba(100,200,255,.5); animation: je-pulse-bind 1s ease infinite; }}
        @keyframes je-pulse-bind {{ 0%,100% {{ opacity: 1; }} 50% {{ opacity: .6; }} }}
        .je-gp-status {{ font-size: 12px; opacity: .5; margin-bottom: 12px; }}

        /* ── FPS Counter ── */
        #je-fps {{ position: fixed; top: 56px; left: 16px; z-index: 89999; font-size: 13px; font-weight: 700; font-family: 'Courier New', monospace; color: #0f0; background: rgba(0,0,0,.6); padding: 2px 8px; border-radius: 4px; pointer-events: none; display: none; text-shadow: 0 0 4px rgba(0,255,0,.5); }}
        #je-fps.je-active {{ display: block; }}

        /* ── Dock Minimize ── */
        #je-dock-min {{ position: fixed; bottom: 16px; right: 16px; z-index: 90000; width: 42px; height: 42px; border-radius: 50%; background: rgba(0,0,0,.78); backdrop-filter: blur(14px); -webkit-backdrop-filter: blur(14px); border: 1px solid rgba(255,255,255,.1); color: #fff; cursor: pointer; display: none; align-items: center; justify-content: center; transition: opacity .3s ease, transform .3s ease; }}
        #je-dock-min.je-active {{ display: flex; }}
        #je-dock-min.je-hidden {{ opacity: 0; pointer-events: none; }}
        #je-dock-min:hover {{ background: rgba(255,255,255,.12); }}
        #je-dock-min:active {{ transform: scale(.88); }}
        #je-dock-min svg {{ width: 22px; height: 22px; fill: currentColor; }}
        #je-dock.je-minimized {{ display: none !important; }}

        /* ── Mobile ── */
        @media (max-width: 768px) {{
            #je-dock {{ gap: 2px; padding: 5px 8px; bottom: 8px; }}
            .je-dockbtn {{ width: 38px; height: 38px; }}
            .je-dockbtn svg {{ width: 20px; height: 20px; }}
            .je-popup {{ width: 96%; max-height: 85vh; border-radius: 12px; }}
            #je-topbar {{ height: 42px; padding: 0 10px; }}
            #je-topbar-title {{ font-size: 13px; }}
            #je-loader-title {{ font-size: 22px; }}
            #je-dock-min {{ bottom: 8px; right: 8px; width: 38px; height: 38px; }}
        }}
    </style>
</head>
<body>
    <!-- Loading Screen -->
    <div id=""je-loader"">
        <div class=""je-loader-anim"">
            <div class=""je-pulse-ring""></div>
            <div class=""je-pulse-ring""></div>
            <div class=""je-pulse-ring""></div>
            <div class=""je-spinner""></div>
        </div>
        <div id=""je-loader-title"">{gameName}</div>
        <div id=""je-loader-system""></div>
        <div id=""je-loader-status"">Loading ROM…</div>
    </div>

    <!-- Top Bar -->
    <div id=""je-topbar"" class=""je-bar"">
        <span id=""je-topbar-title"">{gameName}</span>
        <button class=""je-topbtn"" id=""je-exit-btn"" title=""Exit"">
            <svg viewBox=""0 0 24 24""><path d=""M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z""/></svg>
            Exit
        </button>
    </div>

    <!-- Bottom Dock -->
    <div id=""je-dock"" class=""je-bar"">
        <button class=""je-dockbtn"" id=""je-btn-pause"" title=""Pause""><svg viewBox=""0 0 24 24""><path d=""M6 19h4V5H6v14zm8-14v14h4V5h-4z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-play"" title=""Play"" style=""display:none""><svg viewBox=""0 0 24 24""><path d=""M8 5v14l11-7z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-restart"" title=""Restart""><svg viewBox=""0 0 24 24""><path d=""M17.65 6.35A7.96 7.96 0 0 0 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08A5.99 5.99 0 0 1 12 18c-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z""/></svg></button>
        <div class=""je-dock-sep""></div>
        <button class=""je-dockbtn"" id=""je-btn-ff"" title=""Fast Forward""><svg viewBox=""0 0 24 24""><path d=""M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-slow"" title=""Slow Motion""><svg viewBox=""0 0 24 24""><path d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-4h2V8h-2v8zm-3 0h2V8H8v8z""/></svg></button>
        <div class=""je-dock-sep""></div>
        <button class=""je-dockbtn"" id=""je-btn-saves"" title=""Save States""><svg viewBox=""0 0 24 24""><path d=""M17 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-vol"" title=""Volume""><svg viewBox=""0 0 24 24""><path d=""M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0 0 14 7.97v8.05c1.48-.73 2.5-2.25 2.5-3.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-cheats"" title=""Cheats""><svg viewBox=""0 0 24 24""><path d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-inputmap"" title=""Input Mapping""><svg viewBox=""0 0 24 24""><path d=""M15 7.5V2H9v5.5l3 3 3-3zM7.5 9H2v6h5.5l3-3-3-3zM9 16.5V22h6v-5.5l-3-3-3 3zM16.5 9l-3 3 3 3H22V9h-5.5z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-netplay"" title=""Netplay"" disabled><svg viewBox=""0 0 24 24""><path d=""M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zm6.93 6h-2.95c-.32-1.25-.78-2.45-1.38-3.56 1.84.63 3.37 1.91 4.33 3.56zM12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96zM4.26 14C4.1 13.36 4 12.69 4 12s.1-1.36.26-2h3.38c-.08.66-.14 1.32-.14 2s.06 1.34.14 2H4.26zm.82 2h2.95c.32 1.25.78 2.45 1.38 3.56-1.84-.63-3.37-1.9-4.33-3.56zm2.95-8H5.08c.96-1.66 2.49-2.93 4.33-3.56C8.81 5.55 8.35 6.75 8.03 8zM12 19.96c-.83-1.2-1.48-2.53-1.91-3.96h3.82c-.43 1.43-1.08 2.76-1.91 3.96zM14.34 14H9.66c-.09-.66-.16-1.32-.16-2s.07-1.35.16-2h4.68c.09.65.16 1.32.16 2s-.07 1.34-.16 2zm.25 5.56c.6-1.11 1.06-2.31 1.38-3.56h2.95c-.96 1.65-2.49 2.93-4.33 3.56zM16.36 14c.08-.66.14-1.32.14-2s-.06-1.34-.14-2h3.38c.16.64.26 1.31.26 2s-.1 1.36-.26 2h-3.38z""/></svg></button>
        <div class=""je-dock-sep""></div>
        <button class=""je-dockbtn"" id=""je-btn-screenshot"" title=""Screenshot""><svg viewBox=""0 0 24 24""><path d=""M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-settings"" title=""Settings""><svg viewBox=""0 0 24 24""><path d=""M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.484.484 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96a.49.49 0 0 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.07.62-.07.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6A3.6 3.6 0 1 1 12 8.4a3.6 3.6 0 0 1 0 7.2z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-coreopts"" title=""Core Options""><svg viewBox=""0 0 24 24""><path d=""M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z""/></svg></button>
        <div class=""je-dock-sep""></div>
        <button class=""je-dockbtn"" id=""je-btn-fullscreen"" title=""Fullscreen"">
            <svg id=""je-fs-enter"" viewBox=""0 0 24 24""><path d=""M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z""/></svg>
            <svg id=""je-fs-exit"" viewBox=""0 0 24 24"" style=""display:none""><path d=""M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z""/></svg>
        </button>
    </div>

    <!-- Dock Minimize FAB -->
    <button id=""je-dock-min"" title=""Expand Controls"">
        <svg viewBox=""0 0 24 24""><path d=""M12 8l-6 6 1.41 1.41L12 10.83l4.59 4.58L18 14z""/></svg>
    </button>

    <!-- Popup: Core Options -->
    <div class=""je-overlay"" id=""je-pop-coreopts"">
        <div class=""je-popup"">
            <div class=""je-popup-hdr""><h3>Core Options</h3><button class=""je-closebtn"" data-close=""je-pop-coreopts"">&times;</button></div>
            <div class=""je-popup-body"" id=""je-coreopts-body"">
                </div>
        </div>
    </div>

    <!-- Popup: Save States -->
    <div class=""je-overlay"" id=""je-pop-saves"">
        <div class=""je-popup"">
            <div class=""je-popup-hdr""><h3>Save States</h3><button class=""je-closebtn"" data-close=""je-pop-saves"">&times;</button></div>
            <div class=""je-popup-body"" id=""je-saves-body""></div>
        </div>
    </div>

    <!-- Popup: Volume -->
    <div class=""je-overlay"" id=""je-pop-vol"">
        <div class=""je-popup"" style=""max-width:340px"">
            <div class=""je-popup-hdr""><h3>Volume</h3><button class=""je-closebtn"" data-close=""je-pop-vol"">&times;</button></div>
            <div class=""je-popup-body"">
                <div class=""je-vol-wrap"">
                    <div id=""je-vol-pct"" class=""je-vol-pct"">50%</div>
                    <input type=""range"" min=""0"" max=""1"" step=""0.01"" value=""1.0"" class=""je-vol-slider"" id=""je-vol-slider"">
                    <button class=""je-btn"" id=""je-vol-mute"">Mute</button>
                </div>
            </div>
        </div>
    </div>

    <!-- Popup: Cheats -->
    <div class=""je-overlay"" id=""je-pop-cheats"">
        <div class=""je-popup"">
            <div class=""je-popup-hdr""><h3>Cheats</h3><button class=""je-closebtn"" data-close=""je-pop-cheats"">&times;</button></div>
            <div class=""je-popup-body"">
                <div id=""je-cheat-list""></div>
                <div class=""je-section-title"" style=""margin-top:16px"">Add Cheat</div>
                <div style=""display:flex;flex-direction:column;gap:8px"">
                    <input id=""je-cheat-name"" placeholder=""Cheat name"" style=""background:rgba(255,255,255,.08);border:1px solid rgba(255,255,255,.12);border-radius:8px;padding:8px 10px;color:#fff;font-size:13px"">
                    <textarea id=""je-cheat-code"" placeholder=""Cheat code"" rows=""2"" style=""background:rgba(255,255,255,.08);border:1px solid rgba(255,255,255,.12);border-radius:8px;padding:8px 10px;color:#fff;font-size:13px;resize:none""></textarea>
                    <button class=""je-btn je-btn-primary"" id=""je-cheat-add"">Add Cheat</button>
                </div>
            </div>
        </div>
    </div>

    <!-- Popup: Input Mapping -->
    <div class=""je-overlay"" id=""je-pop-inputmap"">
        <div class=""je-popup je-popup-lg"">
            <div class=""je-popup-hdr""><h3>Input Mapping</h3><button class=""je-closebtn"" data-close=""je-pop-inputmap"">&times;</button></div>
            <div class=""je-popup-body"">
                <div class=""je-tabs"">
                    <button class=""je-tab je-tab-active"" data-tab=""kb"">Keyboard</button>
                    <button class=""je-tab"" data-tab=""gp"">Gamepad</button>
                    <button class=""je-tab"" data-tab=""vg"">Virtual Controls</button>
                </div>
                <div class=""je-tab-panel je-tab-active"" id=""je-tab-kb""></div>
                <div class=""je-tab-panel"" id=""je-tab-gp"">
                    <div class=""je-gp-status"" id=""je-gp-status"">No gamepad detected</div>
                    <div id=""je-gp-binds""></div>
                </div>
                <div class=""je-tab-panel"" id=""je-tab-vg"">
                    <div class=""je-setting"">
                        <span class=""je-setting-label"">Enable Virtual Controls</span>
                        <label class=""je-toggle""><input type=""checkbox"" id=""je-vg-toggle""><span class=""je-toggle-track""></span></label>
                    </div>
                    <div class=""je-setting"">
                        <span class=""je-setting-label"">Left-Handed Mode</span>
                        <label class=""je-toggle""><input type=""checkbox"" id=""je-vg-lefty""><span class=""je-toggle-track""></span></label>
                    </div>
                </div>
                <div style=""margin-top:12px""><button class=""je-btn"" id=""je-input-reset"">Reset to Defaults</button></div>
            </div>
        </div>
    </div>

    <!-- Popup: Settings -->
    <div class=""je-overlay"" id=""je-pop-settings"">
        <div class=""je-popup"">
            <div class=""je-popup-hdr""><h3>Settings</h3><button class=""je-closebtn"" data-close=""je-pop-settings"">&times;</button></div>
            <div class=""je-popup-body"">
                <div class=""je-section-title"">Graphics</div>
                <div class=""je-setting""><span class=""je-setting-label"">Shader</span><select id=""je-set-shader""><option value=""disabled"">None</option><option value=""2xScaleHQ.glslp"">2x ScaleHQ</option><option value=""4xScaleHQ.glslp"">4x ScaleHQ</option><option value=""sabr"">SABR</option><option value=""crt-aperture.glslp"">CRT Aperture</option><option value=""crt-easymode.glslp"">CRT Easymode</option><option value=""crt-geom.glslp"">CRT Geom</option><option value=""crt-mattias.glslp"">CRT Mattias</option><option value=""crt-beam"">CRT Beam</option><option value=""crt-caligari"">CRT Caligari</option><option value=""crt-lottes"">CRT Lottes</option><option value=""crt-zfast"">CRT ZFast</option><option value=""crt-yeetron"">CRT Yeetron</option><option value=""bicubic"">Bicubic</option><option value=""mix-frames"">Mix Frames</option></select></div>
                <div class=""je-setting""><span class=""je-setting-label"">VSync</span><label class=""je-toggle""><input type=""checkbox"" id=""je-set-vsync"" checked><span class=""je-toggle-track""></span></label></div>
                <div class=""je-setting""><span class=""je-setting-label"">Video Rotation</span><select id=""je-set-rotation""><option value=""0"">0°</option><option value=""1"">90°</option><option value=""2"">180°</option><option value=""3"">270°</option></select></div>
                <div class=""je-section-title"">Performance</div>
                <div class=""je-setting""><span class=""je-setting-label"">Fast Forward Rate</span><select id=""je-set-ffrate""><option value=""2"">2x</option><option value=""3"" selected>3x</option><option value=""4"">4x</option><option value=""5"">5x</option><option value=""8"">8x</option><option value=""10"">10x</option><option value=""unlimited"">Unlimited</option></select></div>
                <div class=""je-setting""><span class=""je-setting-label"">Slow Motion Rate</span><select id=""je-set-smrate""><option value=""2"">2x</option><option value=""3"" selected>3x</option><option value=""4"">4x</option><option value=""5"">5x</option></select></div>
                <div class=""je-section-title"">Display</div>
                <div class=""je-setting""><span class=""je-setting-label"">Screen Size</span><select id=""je-set-screensize""><option value=""fit"" selected>Fit to Screen</option><option value=""native"">Native</option><option value=""2x"">2x</option><option value=""3x"">3x</option><option value=""4x"">4x</option></select></div>
                <div class=""je-setting""><span class=""je-setting-label"">Show FPS</span><label class=""je-toggle""><input type=""checkbox"" id=""je-set-fps""><span class=""je-toggle-track""></span></label></div>
            </div>
        </div>
    </div>
    <div id=""je-fps""></div>
    <div id=""game""></div>
    <script>
        // Patch getContext BEFORE loader.js so EJS gets a WebGL context with
        // preserveDrawingBuffer:true — without this, toDataURL always returns black
        // because the buffer is cleared after each frame is composited to screen.
        (function() {{
            var _origGetContext = HTMLCanvasElement.prototype.getContext;
            HTMLCanvasElement.prototype.getContext = function(type, attrs) {{
                if (type === 'webgl' || type === 'webgl2' || type === 'experimental-webgl') {{
                    attrs = Object.assign({{}}, attrs || {{}}, {{ preserveDrawingBuffer: true }});
                }}
                return _origGetContext.call(this, type, attrs);
            }};
        }})();
    </script>
    <script>
        (function() {{
            // Core name map for loading screen badge
            var coreNames = {{nes:'NES',snes:'SNES',n64:'N64',gba:'Game Boy',gbc:'Game Boy Color',gba:'Game Boy Advance',nds:'Nintendo DS',
                vb:'Virtual Boy',segaMD:'Sega Genesis',segaGG:'Game Gear',segaMS:'Master System',segaCD:'Sega CD',
                sega32x:'Sega 32X',psx:'PlayStation',psp:'PSP',a2600:'Atari 2600',a7800:'Atari 7800',lynx:'Atari Lynx',
                pce:'TurboGrafx-16',coleco:'ColecoVision',ngp:'Neo Geo Pocket',arcade:'Arcade',dos:'DOS',
                '3do':'3DO',jaguar:'Atari Jaguar',mame2003:'MAME',ws:'WonderSwan', ss: 'Sega Saturn'}};
            var sysEl = document.getElementById('je-loader-system');
            if (sysEl) {{ var cn = coreNames[window.EJS_core] || window.EJS_core || ''; sysEl.textContent = cn; }}

            // Loading screen lifecycle
            var loader = document.getElementById('je-loader');
            var topbar = document.getElementById('je-topbar');
            var dock   = document.getElementById('je-dock');

            function dismissLoader() {{
                if (!loader || loader.classList.contains('je-dismiss')) return;
                loader.classList.add('je-dismiss');
                setTimeout(function() {{ loader.style.display = 'none'; }}, 450);
                topbar.classList.add('je-active');
                dock.classList.add('je-active');
                startAutoHide();
            }}
            // Fallback: auto-dismiss after 30s
            setTimeout(dismissLoader, 30000);

            window.EJS_onLoad = function() {{
                dismissLoader();
            }};

            // Hook into EJS start event
            window.EJS_onGameStart = function() {{
                // Load percentage observer
                if (_jeEjsObserver) _jeEjsObserver.disconnect();
                clearInterval(_jeFindLoader);
                // Dismiss loading screen
                dismissLoader();
                setTimeout(refocusGame, 500);
{directLoadScript}
                // CRITICAL: EJS checks settingsMenu.style.display !== 'none' in keyChange()
                // to decide whether to block keyboard input. Our CSS class-based hiding
                // doesn't set inline style, so EJS thinks settings menu is open and blocks
                // ALL keyboard input. Force inline style to 'none'.
                setTimeout(function() {{
                    var e = emu();
                    if (e) {{
                        if (e.settingsMenu) e.settingsMenu.style.display = 'none';
                        if (e.controlMenu) e.controlMenu.style.display = 'none';
                    }}
                }}, 200);
            }};

            // Loading Status Observer
            var myStatusEl = document.getElementById('je-loader-status');
            var _jeEjsObserver = null;

            var _jeFindLoader = setInterval(function() {{
                var ejsTextEl = document.querySelector('.ejs_loading_text');
                if (ejsTextEl) {{
                    clearInterval(_jeFindLoader);

                    _jeEjsObserver = new MutationObserver(function() {{
                        var text = ejsTextEl.textContent || ejsTextEl.innerText;
                        if (text && text.trim() !== '') {{
                            myStatusEl.textContent = text;
                        }}
                    }});

                    _jeEjsObserver.observe(ejsTextEl, {{ 
                        childList: true, 
                        characterData: true, 
                        subtree: true 
                    }});
                }}
            }}, 200);

            // Auto-hide docks
            var hideTimer = null;
            var HIDE_MS = 3000;
            var popupOpen = false;

            function showDocks() {{
                if (!topbar.classList.contains('je-active')) return;
                topbar.classList.remove('je-hidden');
                dock.classList.remove('je-hidden');
                clearTimeout(hideTimer);
                if (!popupOpen) {{
                    hideTimer = setTimeout(function() {{
                        topbar.classList.add('je-hidden');
                        dock.classList.add('je-hidden');
                    }}, HIDE_MS);
                }}
            }}
            function startAutoHide() {{
                ['mousemove','mousedown','touchstart','touchmove','keydown'].forEach(function(evt) {{
                    document.addEventListener(evt, showDocks, {{ passive: true }});
                }});
                [topbar, dock].forEach(function(el) {{
                    el.addEventListener('mouseenter', function() {{ clearTimeout(hideTimer); topbar.classList.remove('je-hidden'); dock.classList.remove('je-hidden'); }});
                    el.addEventListener('mouseleave', showDocks);
                }});
                showDocks();
            }}

            // Refocus game on any click on the game area or after dock button press
            document.getElementById('game').addEventListener('mousedown', function() {{
                setTimeout(refocusGame, 50);
            }});
            // Dock buttons: refocus game after each click (unless a popup opened)
            document.addEventListener('click', function(ev) {{
                var btn = ev.target.closest && ev.target.closest('.je-dockbtn');
                if (btn && !popupOpen) {{ setTimeout(refocusGame, 50); }}
            }}, true);

            // Popup management
            function openPopup(id) {{
                closeAllPopups();
                var el = document.getElementById(id);
                if (el) {{ el.classList.add('je-open'); popupOpen = true; clearTimeout(hideTimer); }}
            }}
            function closePopup(id) {{
                var el = document.getElementById(id);
                if (el) el.classList.remove('je-open');
                popupOpen = false;
                showDocks();
                refocusGame();
            }}
            function closeAllPopups() {{
                document.querySelectorAll('.je-overlay.je-open').forEach(function(el) {{ el.classList.remove('je-open'); }});
                popupOpen = false;
                refocusGame();
            }}
            // Close buttons
            document.querySelectorAll('[data-close]').forEach(function(btn) {{
                btn.addEventListener('click', function() {{ closePopup(btn.getAttribute('data-close')); }});
            }});
            // Click outside popup to close
            document.querySelectorAll('.je-overlay').forEach(function(ov) {{
                ov.addEventListener('click', function(e) {{ if (e.target === ov) {{ closeAllPopups(); showDocks(); refocusGame(); }} }});
            }});

            // Tab switching
            document.querySelectorAll('.je-tab').forEach(function(tab) {{
                tab.addEventListener('click', function() {{
                    var tabs = tab.parentElement.querySelectorAll('.je-tab');
                    tabs.forEach(function(t) {{ t.classList.remove('je-tab-active'); }});
                    tab.classList.add('je-tab-active');
                    var panels = tab.closest('.je-popup-body').querySelectorAll('.je-tab-panel');
                    panels.forEach(function(p) {{ p.classList.remove('je-tab-active'); }});
                    var target = document.getElementById('je-tab-' + tab.getAttribute('data-tab'));
                    if (target) target.classList.add('je-tab-active');
                }});
            }});

            // Helper: get emulator
            function emu() {{ return window.EJS_emulator; }}
            function gm()  {{ var e = emu(); return e ? e.gameManager : null; }}

            function _jeEnsureBinary(data) {{
                if (!data) return null;
                if (data instanceof Blob) return data;
                if (data instanceof Uint8Array) return new Blob([data], {{ type: 'application/octet-stream' }});
                if (data instanceof ArrayBuffer) return new Blob([new Uint8Array(data)], {{ type: 'application/octet-stream' }});
                if (typeof data === 'string') {{
                    try {{
                        var binary = window.atob(data);
                        var bytes = new Uint8Array(binary.length);
                        for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
                        return new Blob([bytes], {{ type: 'application/octet-stream' }});
                    }} catch(ex) {{ return new Blob([new TextEncoder().encode(data)], {{ type: 'application/octet-stream' }}); }}
                }}
                return new Blob([new TextEncoder().encode(JSON.stringify(data))], {{ type: 'application/octet-stream' }});
            }}

            // Focus management: EJS listens for keyboard on ejs_parent, not document
            function refocusGame() {{
                var e = emu();
                if (e && e.elements && e.elements.parent) {{
                    e.elements.parent.focus();
                }} else {{
                    var gp = document.querySelector('.ejs_parent');
                    if (gp) gp.focus();
                }}
            }}

            // Server sync for controls & settings
            var _syncTimer = null;
            function syncControlsToServer() {{
                clearTimeout(_syncTimer);
                _syncTimer = setTimeout(function() {{
                    var e = emu(); if (!e) return;
                    var c = e.controls && e.controls[0] ? e.controls[0] : {{}};
                    // Split keyboard (value) and gamepad (value2) bindings
                    var kb = {{}}, gp = {{}};
                    for (var k in c) {{
                        if (c[k].value !== undefined) {{
                            if (!kb[k]) kb[k] = {{}};
                            kb[k].value = c[k].value;
                        }}
                        if (c[k].value2 !== undefined) {{
                            if (!gp[k]) gp[k] = {{}};
                            gp[k].value2 = c[k].value2;
                        }}
                    }}
                    var payload = {{
                        controls: JSON.stringify(kb),
                        controllerControls: JSON.stringify(gp)
                    }};
                    // Also sync settings if available
                    if (e.settings) {{
                        if (e.settings['shader']) payload.shader = e.settings['shader'];
                        if (e.settings['videoRotation'] !== undefined) payload.videoRotation = parseInt(e.settings['videoRotation']) || 0;
                    }}
                    fetch('/jellyemu/prefs/{userId}', {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/json' }},
                        body: JSON.stringify(payload)
                    }}).catch(function(err) {{ console.warn('[JellyEmu] Controls sync failed:', err); }});
                }}, 800);
            }}

            // Exit button
            document.getElementById('je-exit-btn').addEventListener('click', function() {{
                if (window.EJS_onExit) {{ EJS_onExit(); }}
                else if (window.opener) {{ window.close(); }}
                else {{ window.parent.postMessage('close-jellyemu','*'); }}
            }});

            // Core Options Popup
            document.getElementById('je-btn-coreopts').addEventListener('click', function() {{
                buildCoreOptions();
                openPopup('je-pop-coreopts');
            }});

            function buildCoreOptions() {{
                var body = document.getElementById('je-coreopts-body');
                var g = gm();
                
                if (!g || typeof g.getCoreOptions !== 'function') {{
                    body.innerHTML = '<div style=""opacity:.4;font-size:13px;text-align:center;padding:20px;"">Core options not available yet.</div>';
                    return;
                }}

                var optsRaw = g.getCoreOptions();
                if (!optsRaw || typeof optsRaw !== 'string') {{
                    body.innerHTML = '<div style=""opacity:.4;font-size:13px;text-align:center;padding:20px;"">No options available for this core.</div>';
                    return;
                }}

                var lines = optsRaw.split('\n');
                body.innerHTML = '';
                var hasOpts = false;

                lines.forEach(function(line) {{
                    if (!line.trim()) return;
                    hasOpts = true;
                    
                    // Splits ""key|currentValue; val1|val2|val3""
                    var parts = line.split(/;\s*/);
                    if (parts.length < 2) return;

                    var keyVal = parts[0].split('|');
                    var key = keyVal[0];
                    var currentVal = keyVal[1];
                    var options = parts[1].split('|');

                    // Make the label pretty (e.g., 'yabause_frameskip' -> 'Frameskip')
                    var displayKey = key.replace(/^[^_]+_/, '').replace(/_/g, ' ');
                    displayKey = displayKey.charAt(0).toUpperCase() + displayKey.slice(1);

                    var row = document.createElement('div');
                    row.className = 'je-setting';
                    
                    var label = document.createElement('span');
                    label.className = 'je-setting-label';
                    label.textContent = displayKey;
                    
                    var select = document.createElement('select');
                    options.forEach(function(opt) {{
                        var option = document.createElement('option');
                        option.value = opt;
                        option.textContent = opt;
                        if (opt === currentVal) option.selected = true;
                        select.appendChild(option);
                    }});

                    // Trigger the core change instantly on dropdown change
                    select.addEventListener('change', function() {{
                        g.setVariable(key, this.value);
                    }});

                    row.appendChild(label);
                    row.appendChild(select);
                    body.appendChild(row);
                }});

                if (!hasOpts) {{
                    body.innerHTML = '<div style=""opacity:.4;font-size:13px;text-align:center;padding:20px;"">No options available for this core.</div>';
                }}
            }}

            // Fullscreen
            // Fullscreen
            var btnFs = document.getElementById('je-btn-fullscreen');
            var iconEnter = document.getElementById('je-fs-enter');
            var iconExit = document.getElementById('je-fs-exit');

            btnFs.addEventListener('click', function() {{
                if (!document.fullscreenElement) {{
                    document.body.requestFullscreen().catch(function(err) {{
                        console.warn('[JellyEmu] Fullscreen failed:', err);
                    }});
                }} else {{
                    if (document.exitFullscreen) {{
                        document.exitFullscreen();
                    }}
                }}
            }});

            // Listen for the state change so the UI updates even if the user presses 'Esc'
            document.addEventListener('fullscreenchange', function() {{
                if (document.fullscreenElement) {{
                    iconEnter.style.display = 'none';
                    iconExit.style.display = '';
                    btnFs.title = 'Exit Fullscreen';
                }} else {{
                    iconEnter.style.display = '';
                    iconExit.style.display = 'none';
                    btnFs.title = 'Fullscreen';
                }}
            }});

            // Pause / Play
            var btnPause = document.getElementById('je-btn-pause');
            var btnPlay  = document.getElementById('je-btn-play');
            btnPause.addEventListener('click', function() {{
                var e = emu(); if (!e || !e.started) return;
                e.pause();
                btnPause.style.display = 'none'; btnPlay.style.display = '';
            }});
            btnPlay.addEventListener('click', function() {{
                var e = emu(); if (!e || !e.started) return;
                e.play();
                btnPlay.style.display = 'none'; btnPause.style.display = '';
            }});

            // Restart
            document.getElementById('je-btn-restart').addEventListener('click', function() {{
                var g = gm(); if (g) g.restart();
            }});

            // Fast Forward
            var ffActive = false;
            document.getElementById('je-btn-ff').addEventListener('click', function() {{
                var g = gm(); if (!g) return;
                ffActive = !ffActive;
                g.toggleFastForward(ffActive ? 1 : 0);
                this.classList.toggle('je-active', ffActive);
            }});

            // Slow Motion
            var slowActive = false;
            document.getElementById('je-btn-slow').addEventListener('click', function() {{
                var g = gm(); if (!g) return;
                slowActive = !slowActive;
                g.toggleSlowMotion(slowActive ? 1 : 0);
                this.classList.toggle('je-active', slowActive);
            }});

            // Save States popup
            document.getElementById('je-btn-saves').addEventListener('click', function() {{
                buildSaveSlots();
                openPopup('je-pop-saves');
            }});

            function buildSaveSlots() {{
                var body = document.getElementById('je-saves-body');
                body.innerHTML = '';
                for (var i = 1; i <= 5; i++) {{
                    var slot = document.createElement('div');
                    slot.className = 'je-slot';
                    slot.innerHTML = '<div class=""je-slot-num"">' + i + '</div>' +
                        '<div class=""je-slot-info""><div>Slot ' + i + '</div><small id=""je-slot-status-' + i + '"">Checking…</small></div>' +
                        '<div class=""je-slot-actions"">' +
                        '<button class=""je-btn"" data-save=""' + i + '"">Save</button>' +
                        '<button class=""je-btn je-btn-primary"" data-load=""' + i + '"">Load</button></div>';
                    body.appendChild(slot);
                    // Check if slot has data
                    (function(s) {{
                        fetch('/jellyemu/save/{itemId}/{userId}?slot=' + s, {{ method: 'HEAD' }})
                            .then(function(r) {{
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = r.ok ? 'Has save data' : 'Empty';
                            }}).catch(function() {{
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'Empty';
                            }});
                    }})(i);
                }}
                // Wire save/load button(s)
                body.querySelectorAll('[data-save]').forEach(function(btn) {{
                    btn.addEventListener('click', function() {{
                        var s = parseInt(btn.getAttribute('data-save'));
                        var g = gm(); if (!g) return;
                        Promise.resolve(g.getState()).then(function(rawState) {{
                            var state = _jeEnsureBinary(rawState);
                            if (!state) return;
                            console.log('[JellyEmu] Pipeline STAGE 1 (Client Gen): Payload size ->', state.size || state.byteLength, 'bytes');
                            // Upload save state to server
                            fetch('/jellyemu/save/{itemId}/{userId}?slot=' + s, {{
                                method: 'POST',
                                headers: {{ 'Content-Type': 'application/octet-stream' }},
                                body: state
                            }}).then(function(r) {{
                                if (!r.ok) throw new Error('Save rejected by server');
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'Saved!';
                                // Capture and upload screenshot for this slot
                                var canvas = document.querySelector('canvas.ejs_canvas') || document.querySelector('canvas');
                                if (canvas) {{
                                    try {{
                                        var dataUrl = canvas.toDataURL('image/png');
                                        fetch('/jellyemu/save-screenshot/{itemId}/{userId}/' + s, {{
                                            method: 'POST',
                                            headers: {{ 'Content-Type': 'application/json' }},
                                            body: JSON.stringify({{ dataUrl: dataUrl }})
                                        }}).catch(function(err) {{ console.warn('[JellyEmu] Screenshot upload failed:', err); }});
                                    }} catch(ex) {{ console.warn('[JellyEmu] Screenshot capture failed:', ex); }}
                                }}
                            }}).catch(function(err) {{
                                console.error('[JellyEmu] Save failed:', err);
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'Save Failed';
                            }});
                        }});
                    }});
                }});
                body.querySelectorAll('[data-load]').forEach(function(btn) {{
                    btn.addEventListener('click', function() {{
                        var s = parseInt(btn.getAttribute('data-load'));
                        fetch('/jellyemu/save/{itemId}/{userId}?slot=' + s)
                            .then(function(r) {{
                                if (!r.ok) throw new Error('No save');
                                return r.arrayBuffer();
                            }}).then(function(buf) {{
                                var g = gm(); if (!g) return;
                                console.log('[JellyEmu] Pipeline STAGE 4 (Client Receive): Downloaded bytes ->', buf.byteLength);
                                g.loadState(new Uint8Array(buf));
                                closePopup('je-pop-saves');
                            }}).catch(function() {{
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'No save to load';
                            }});
                    }});
                }});
            }}

            // Volume popup
            var _jeCurrentVol = 1.0; // Single source of truth for the session
            var _jeLastVol = 1.0;    // Memory for the mute toggle

            document.getElementById('je-btn-vol').addEventListener('click', function() {{
                var e = emu();
                if (e) {{
                    var slider = document.getElementById('je-vol-slider');
                    var isMuted = (_jeCurrentVol === 0);
                    
                    // Sync the UI to our session tracker, ignoring EJS's reported value
                    slider.value = _jeCurrentVol;
                    document.getElementById('je-vol-pct').textContent = Math.round(_jeCurrentVol * 100) + '%';
                    document.getElementById('je-vol-mute').textContent = isMuted ? 'Unmute' : 'Mute';
                    
                    // Force the emulator to match our tracked volume, just to be safe
                    e.setVolume(_jeCurrentVol);
                    e.volume = _jeCurrentVol;
                }}
                openPopup('je-pop-vol');
            }});

            document.getElementById('je-vol-slider').addEventListener('input', function() {{
                var v = parseFloat(this.value);
                _jeCurrentVol = v; // Update our session tracker
                
                document.getElementById('je-vol-pct').textContent = Math.round(v * 100) + '%';
                
                var e = emu();
                if (e) {{ 
                    e.setVolume(v); 
                    e.volume = v; // Keep EJS property in sync
                    
                    if (v > 0) {{
                        document.getElementById('je-vol-mute').textContent = 'Mute';
                    }} else {{
                        document.getElementById('je-vol-mute').textContent = 'Unmute';
                    }}
                }}
            }});

            document.getElementById('je-vol-mute').addEventListener('click', function() {{
                var e = emu(); 
                if (!e) return;
                
                var slider = document.getElementById('je-vol-slider');
                
                if (_jeCurrentVol > 0) {{
                    // Action: Mute
                    _jeLastVol = _jeCurrentVol; // Save current volume
                    _jeCurrentVol = 0;          // Update session tracker
                    
                    e.setVolume(0);
                    e.volume = 0;
                    
                    slider.value = 0;
                    document.getElementById('je-vol-pct').textContent = '0%';
                    this.textContent = 'Unmute';
                }} else {{
                    // Action: Unmute
                    var restoreVol = _jeLastVol > 0 ? _jeLastVol : 1.0; 
                    _jeCurrentVol = restoreVol; // Restore session tracker
                    
                    e.setVolume(restoreVol);
                    e.volume = restoreVol;
                    
                    slider.value = restoreVol;
                    document.getElementById('je-vol-pct').textContent = Math.round(restoreVol * 100) + '%';
                    this.textContent = 'Mute';
                }}
            }});

            // Cheats popup — cheats are loaded lazily on first open, never injected at startup.
            // Only individually toggled cheats are ever passed to the emulator via cheatChanged().
            var _jeCheatsLoaded = false;
            var _jeCheatsDb = []; // [{{ desc, code, checked }}] from server

            function buildCheats() {{
                var e = emu(); if (!e) return;
                var list = document.getElementById('je-cheat-list');
                list.innerHTML = '';

                // Merge server cheat db with any user-added cheats already in e.cheats
                // User-added cheats have no matching entry in _jeCheatsDb
                var allCheats = _jeCheatsDb.map(function(ch) {{
                    // Find matching entry in e.cheats to preserve toggled state
                    var live = (e.cheats || []).find(function(c) {{ return c.code === ch.code; }});
                    return {{ desc: ch.desc, code: ch.code, checked: live ? !!live.checked : false, fromDb: true }};
                }});
                // Append any user-added cheats not in the db
                (e.cheats || []).forEach(function(c) {{
                    if (!_jeCheatsDb.find(function(db) {{ return db.code === c.code; }})) {{
                        allCheats.push({{ desc: c.desc, code: c.code, checked: !!c.checked, fromDb: false }});
                    }}
                }});

                if (allCheats.length === 0) {{
                    list.innerHTML = '<div style=""opacity:.4;font-size:13px"">No cheats found for this game</div>';
                    return;
                }}

                allCheats.forEach(function(ch, idx) {{
                    var row = document.createElement('div');
                    row.className = 'je-cheat-row';
                    row.innerHTML = '<label class=""je-toggle""><input type=""checkbox""' + (ch.checked ? ' checked' : '') +
                        '><span class=""je-toggle-track""></span></label><span class=""je-cheat-name"">' +
                        ch.desc + '</span>' + (!ch.fromDb ? '<button class=""je-cheat-del"">&times;</button>' : '');

                    var cb = row.querySelector('input');
                    cb.addEventListener('change', function() {{
                        ch.checked = cb.checked;
                        // Find or create the entry in e.cheats and toggle it
                        var liveIdx = (e.cheats || []).findIndex(function(c) {{ return c.code === ch.code; }});
                        if (cb.checked) {{
                            if (liveIdx === -1) {{
                                e.cheats = e.cheats || [];
                                e.cheats.push({{ desc: ch.desc, code: ch.code, checked: true }});
                                liveIdx = e.cheats.length - 1;
                            }} else {{
                                e.cheats[liveIdx].checked = true;
                            }}
                        }} else {{
                            if (liveIdx !== -1) e.cheats[liveIdx].checked = false;
                        }}
                        e.cheatChanged(cb.checked, ch.code, liveIdx === -1 ? 0 : liveIdx);
                        e.saveSettings();
                    }});

                    var del = row.querySelector('.je-cheat-del');
                    if (del) {{
                        del.addEventListener('click', function() {{
                            var liveIdx = (e.cheats || []).findIndex(function(c) {{ return c.code === ch.code; }});
                            if (liveIdx !== -1) {{
                                e.cheatChanged(false, ch.code, liveIdx);
                                e.cheats.splice(liveIdx, 1);
                            }}
                            allCheats.splice(idx, 1);
                            e.saveSettings();
                            buildCheats();
                        }});
                    }}
                    list.appendChild(row);
                }});
            }}

            document.getElementById('je-btn-cheats').addEventListener('click', function() {{
                openPopup('je-pop-cheats');
                if (_jeCheatsLoaded) {{
                    buildCheats();
                    return;
                }}
                var list = document.getElementById('je-cheat-list');
                list.innerHTML = '<div style=""opacity:.4;font-size:13px"">Loading cheats…</div>';
                fetch('/jellyemu/cheats/{itemId}')
                    .then(function(r) {{ return r.json(); }})
                    .then(function(data) {{
                        _jeCheatsLoaded = true;
                        // data is [[name, code, status], ...] from the server
                        _jeCheatsDb = (data || []).map(function(entry) {{
                            return {{ desc: entry[0], code: entry[1], checked: false }};
                        }});
                        buildCheats();
                    }})
                    .catch(function() {{
                        _jeCheatsLoaded = true; // don't retry on error
                        buildCheats();
                    }});
            }});

            document.getElementById('je-cheat-add').addEventListener('click', function() {{
                var name = document.getElementById('je-cheat-name').value.trim();
                var code = document.getElementById('je-cheat-code').value.trim();
                if (!name || !code) return;
                var e = emu(); if (!e) return;
                e.cheats = e.cheats || [];
                e.cheats.push({{ desc: name, code: code, checked: false }});
                e.saveSettings();
                document.getElementById('je-cheat-name').value = '';
                document.getElementById('je-cheat-code').value = '';
                buildCheats();
            }});

            // EJS uses numeric button indices internally
            var inputMap = {{
                0:  'B',                1:  'Y',               2:  'SELECT',          3:  'START',
                4:  'UP',               5:  'DOWN',            6:  'LEFT',            7:  'RIGHT',
                8:  'A',                9:  'X',               10: 'L',               11: 'R',
                12: 'L2',              13: 'R2',              14: 'L3',              15: 'R3',
                16: 'L STICK RIGHT',   17: 'L STICK LEFT',   18: 'L STICK DOWN',   19: 'L STICK UP',
                20: 'R STICK RIGHT',   21: 'R STICK LEFT',   22: 'R STICK DOWN',   23: 'R STICK UP',
                24: 'QUICK SAVE STATE', 25: 'QUICK LOAD STATE', 26: 'CHANGE STATE SLOT', 27: 'FAST FORWARD',
                28: 'REWIND',           29: 'SLOW MOTION'
            }};

            // Comprehensive keycode → display name map
            var keyCodeMap = {{
                8: 'Backspace', 9: 'Tab', 13: 'Enter', 16: 'Shift', 17: 'Ctrl', 18: 'Alt',
                19: 'Pause', 20: 'Caps Lock', 27: 'Escape', 32: 'Space', 33: 'Page Up',
                34: 'Page Down', 35: 'End', 36: 'Home', 37: '← Left', 38: '↑ Up',
                39: '→ Right', 40: '↓ Down', 45: 'Insert', 46: 'Delete',
                48: '0', 49: '1', 50: '2', 51: '3', 52: '4',
                53: '5', 54: '6', 55: '7', 56: '8', 57: '9',
                65: 'A', 66: 'B', 67: 'C', 68: 'D', 69: 'E',
                70: 'F', 71: 'G', 72: 'H', 73: 'I', 74: 'J',
                75: 'K', 76: 'L', 77: 'M', 78: 'N', 79: 'O',
                80: 'P', 81: 'Q', 82: 'R', 83: 'S', 84: 'T',
                85: 'U', 86: 'V', 87: 'W', 88: 'X', 89: 'Y', 90: 'Z',
                96: 'Num 0', 97: 'Num 1', 98: 'Num 2', 99: 'Num 3', 100: 'Num 4',
                101: 'Num 5', 102: 'Num 6', 103: 'Num 7', 104: 'Num 8', 105: 'Num 9',
                106: 'Num *', 107: 'Num +', 109: 'Num -', 110: 'Num .', 111: 'Num /',
                112: 'F1', 113: 'F2', 114: 'F3', 115: 'F4', 116: 'F5',
                117: 'F6', 118: 'F7', 119: 'F8', 120: 'F9', 121: 'F10',
                122: 'F11', 123: 'F12', 144: 'Num Lock', 145: 'Scroll Lock',
                186: ';', 187: '=', 188: ',', 189: '-', 190: '.', 191: '/',
                192: '`', 219: '[', 220: '\\', 221: ']', 222: '\x27'
            }};

            document.getElementById('je-btn-inputmap').addEventListener('click', function() {{
                var e = emu();
                console.groupEnd();
                buildKeyboardBinds();
                buildGamepadBinds();
                syncVGToggles();
                openPopup('je-pop-inputmap');
            }});

            // Translates native browser keystrokes into EJS strings
            function getEjsKeyStr(ev) {{
                var k = ev.key.toLowerCase();
                if (k === 'arrowup') return 'up arrow';
                if (k === 'arrowdown') return 'down arrow';
                if (k === 'arrowleft') return 'left arrow';
                if (k === 'arrowright') return 'right arrow';
                if (k === ' ') return 'space';
                if (k === '+') return 'add';
                if (k === '-') return 'subtract';
                return k; 
            }}

            // Translates legacy localstorage integer saves back into EJS strings
            function legacyKeyCodeToStr(code) {{
                if (code === 0) return ''; // Explicitly catch the old '0 = unbound' flag
                
                var m = keyCodeMap[code];
                if (!m) return String(code);
                m = m.toLowerCase();
                if (m.indexOf('up') > -1) return 'up arrow';
                if (m.indexOf('down') > -1) return 'down arrow';
                if (m.indexOf('left') > -1) return 'left arrow';
                if (m.indexOf('right') > -1) return 'right arrow';
                if (m === 'space') return 'space';
                if (m === 'num +') return 'add';
                if (m === 'num -') return 'subtract';
                if (m === 'enter') return 'enter';
                if (m === 'escape') return 'escape';
                if (m.length === 1) return m; 
                return m; 
            }}

            function buildKeyboardBinds() {{
                var panel = document.getElementById('je-tab-kb');
                panel.innerHTML = ''; 
                
                var e = emu(); if (!e) return;
                var defaults = (window.EJS_defaultControls && window.EJS_defaultControls[0]) || {{}};
                var c = (e.controls && e.controls[0]) || defaults;

                function keyName(code) {{
                    if (code === undefined || code === null || code === 0 || code === '' || code === '0') return '—';
                    if (typeof code === 'number' || !isNaN(Number(code))) {{
                        return keyCodeMap[Number(code)] || ('Key ' + code);
                    }}
                    return String(code);
                }}

                // Quick reverse lookup to fix corrupted strings from our last test
                function fixCorruptedString(str) {{
                    if (!str) return 0;
                    str = str.toLowerCase();
                    if (str === 'up arrow') return 38;
                    if (str === 'down arrow') return 40;
                    if (str === 'left arrow') return 37;
                    if (str === 'right arrow') return 39;
                    if (str === 'space') return 32;
                    if (str === 'add') return 107;
                    if (str === 'subtract') return 109;
                    if (str === 'enter') return 13;
                    if (str.length === 1) return str.toUpperCase().charCodeAt(0);
                    return 0; // Unbound fallback
                }}

                var needsSave = false;

                Object.keys(inputMap).forEach(function(keyStr) {{
                    (function(idx) {{
                        var key = parseInt(keyStr, 10);
                        var row = document.createElement('div');
                        row.className = 'je-bind-row';
                        
                        var entry = c[key] || defaults[key] || {{}};
                        var rawVal = (entry.value !== undefined && entry.value !== '') ? entry.value : null;

                        // AUTO-REVERT: If the saved value is a string from our last attempt, convert it BACK to an integer
                        if (rawVal !== null && typeof rawVal === 'string' && isNaN(Number(rawVal))) {{
                            rawVal = fixCorruptedString(rawVal);
                            if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                            if (!e.controls[0]) e.controls[0] = {{}};
                            if (!e.controls[0][key]) e.controls[0][key] = {{}};
                            
                            e.controls[0][key].value = rawVal;
                            needsSave = true; // Flag to save the fix
                        }}

                        var displayName = keyName(rawVal);

                        row.innerHTML = '<span>' + inputMap[key] + '</span><span class=""je-bind-key"" data-btn=""' + key + '"">' + displayName + '</span>';
                        
                        var bk = row.querySelector('.je-bind-key');
                        bk.addEventListener('click', function() {{
                            if (bk.classList.contains('je-listening')) return;
                            
                            bk.classList.add('je-listening');
                            bk.textContent = 'Press a key...';

                            function onKey(ev) {{
                                ev.preventDefault();
                                ev.stopPropagation();
                                
                                var kc = ev.keyCode;
                                
                                // Escape cancels
                                if (kc === 27) {{ 
                                    bk.textContent = displayName; 
                                    bk.classList.remove('je-listening'); 
                                    document.removeEventListener('keydown', onKey, true);
                                    return; 
                                }}

                                bk.textContent = keyName(kc);
                                bk.classList.remove('je-listening');
                                document.removeEventListener('keydown', onKey, true);

                                if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                                if (!e.controls[0]) e.controls[0] = {{}};
                                if (!e.controls[0][key]) e.controls[0][key] = {{}};
                                
                                // Save strict integer required by EJS runtime!
                                e.controls[0][key].value = kc;
                                
                                e.saveSettings();
                                syncControlsToServer();
                            }}
                            document.addEventListener('keydown', onKey, true);
                        }});
                        
                        panel.appendChild(row);
                    }})(parseInt(keyStr, 10));
                }});
                
                // Save out the fixed integer bindings if we caught any
                if (needsSave) {{
                    e.saveSettings();
                    syncControlsToServer();
                }}
            }}

            // Translates EJS internal gamepad strings to human-readable UI labels
            var _jeGpLabels = {{
                'BUTTON_1': 'A / Cross', 'BUTTON_2': 'B / Circle', 
                'BUTTON_3': 'X / Square', 'BUTTON_4': 'Y / Triangle',
                'LEFT_TOP_SHOULDER': 'LB / L1', 'RIGHT_TOP_SHOULDER': 'RB / R1',
                'LEFT_BOTTOM_SHOULDER': 'LT / L2', 'RIGHT_BOTTOM_SHOULDER': 'RT / R2',
                'SELECT': 'Select / Back', 'START': 'Start',
                'LEFT_STICK': 'L3', 'RIGHT_STICK': 'R3',
                'DPAD_UP': 'D-Up', 'DPAD_DOWN': 'D-Down', 
                'DPAD_LEFT': 'D-Left', 'DPAD_RIGHT': 'D-Right',
                'LEFT_STICK_X:+1': 'L-Stick →', 'LEFT_STICK_X:-1': 'L-Stick ←',
                'LEFT_STICK_Y:+1': 'L-Stick ↓', 'LEFT_STICK_Y:-1': 'L-Stick ↑',
                'RIGHT_STICK_X:+1': 'R-Stick →', 'RIGHT_STICK_X:-1': 'R-Stick ←',
                'RIGHT_STICK_Y:+1': 'R-Stick ↓', 'RIGHT_STICK_Y:-1': 'R-Stick ↑'
            }};

            function getFriendlyGpLabel(ejsString) {{
                if (!ejsString) return '—';
                return _jeGpLabels[ejsString] || ejsString;
            }}

            window.EJS_defaultControls = {{
                0: {{
                    // Face Buttons (B, Y, Select, Start)
                    0:  {{ 'value': 88, 'value2': 'BUTTON_2' }}, // X key
                    1:  {{ 'value': 83, 'value2': 'BUTTON_4' }}, // S key
                    2:  {{ 'value': 86, 'value2': 'SELECT' }},   // V key
                    3:  {{ 'value': 13, 'value2': 'START' }},    // Enter
                    
                    // D-Pad
                    4:  {{ 'value': 38, 'value2': 'DPAD_UP' }},    // Up Arrow
                    5:  {{ 'value': 40, 'value2': 'DPAD_DOWN' }},  // Down Arrow
                    6:  {{ 'value': 37, 'value2': 'DPAD_LEFT' }},  // Left Arrow
                    7:  {{ 'value': 39, 'value2': 'DPAD_RIGHT' }}, // Right Arrow
                    
                    // Face Buttons (A, X)
                    8:  {{ 'value': 90, 'value2': 'BUTTON_1' }}, // Z key
                    9:  {{ 'value': 65, 'value2': 'BUTTON_3' }}, // A key
                    
                    // Bumpers / Triggers
                    10: {{ 'value': 81, 'value2': 'LEFT_TOP_SHOULDER' }}, // Q key
                    11: {{ 'value': 69, 'value2': 'RIGHT_TOP_SHOULDER' }},// E key
                    12: {{ 'value': 9,  'value2': 'LEFT_BOTTOM_SHOULDER' }}, // Tab
                    13: {{ 'value': 82, 'value2': 'RIGHT_BOTTOM_SHOULDER' }}, // R key
                    
                    // Stick Clicks
                    14: {{ 'value': 0, 'value2': 'LEFT_STICK' }},
                    15: {{ 'value': 0, 'value2': 'RIGHT_STICK' }},
                    
                    // Analog Sticks
                    16: {{ 'value': 72, 'value2': 'LEFT_STICK_X:+1' }}, // H
                    17: {{ 'value': 70, 'value2': 'LEFT_STICK_X:-1' }}, // F
                    18: {{ 'value': 71, 'value2': 'LEFT_STICK_Y:+1' }}, // G
                    19: {{ 'value': 84, 'value2': 'LEFT_STICK_Y:-1' }}, // T
                    20: {{ 'value': 76, 'value2': 'RIGHT_STICK_X:+1' }}, // L
                    21: {{ 'value': 74, 'value2': 'RIGHT_STICK_X:-1' }}, // J
                    22: {{ 'value': 75, 'value2': 'RIGHT_STICK_Y:+1' }}, // K
                    23: {{ 'value': 73, 'value2': 'RIGHT_STICK_Y:-1' }}, // I
                    
                    // Hotkeys
                    24: {{ 'value': 49 }}, // 1
                    25: {{ 'value': 50 }}, // 2
                    26: {{ 'value': 51 }}, // 3
                    27: {{ 'value': 107 }}, // num +
                    28: {{ 'value': 32 }}, // space
                    29: {{ 'value': 109 }} // num -
                }},
                1: {{}}, 2: {{}}, 3: {{}}
            }};

            // Translates EJS internal gamepad strings to human-readable UI labels
            var _jeGpLabels = {{
                'BUTTON_1': 'A / Cross', 'BUTTON_2': 'B / Circle', 
                'BUTTON_3': 'X / Square', 'BUTTON_4': 'Y / Triangle',
                'LEFT_TOP_SHOULDER': 'LB / L1', 'RIGHT_TOP_SHOULDER': 'RB / R1',
                'LEFT_BOTTOM_SHOULDER': 'LT / L2', 'RIGHT_BOTTOM_SHOULDER': 'RT / R2',
                'SELECT': 'Select / Back', 'START': 'Start',
                'LEFT_STICK': 'L3', 'RIGHT_STICK': 'R3',
                'DPAD_UP': 'D-Up', 'DPAD_DOWN': 'D-Down', 
                'DPAD_LEFT': 'D-Left', 'DPAD_RIGHT': 'D-Right',
                'LEFT_STICK_X:+1': 'L-Stick →', 'LEFT_STICK_X:-1': 'L-Stick ←',
                'LEFT_STICK_Y:+1': 'L-Stick ↓', 'LEFT_STICK_Y:-1': 'L-Stick ↑',
                'RIGHT_STICK_X:+1': 'R-Stick →', 'RIGHT_STICK_X:-1': 'R-Stick ←',
                'RIGHT_STICK_Y:+1': 'R-Stick ↓', 'RIGHT_STICK_Y:-1': 'R-Stick ↑'
            }};

            function getFriendlyGpLabel(ejsString) {{
                if (!ejsString) return '—';
                return _jeGpLabels[ejsString] || ejsString;
            }}

            function buildGamepadBinds() {{
                var panel = document.getElementById('je-gp-binds');
                // Create the 3-column header layout
                panel.innerHTML = '<div class=""je-bind-headers""><span>Action</span><span>Primary</span><span>Secondary</span></div>';
                
                // Detect gamepad status
                var gps = navigator.getGamepads ? navigator.getGamepads() : [];
                var gp = null;
                for (var g = 0; g < gps.length; g++) {{ if (gps[g]) {{ gp = gps[g]; break; }} }}
                document.getElementById('je-gp-status').textContent = gp ? ('Detected: ' + gp.id) : 'No gamepad detected';

                var e = emu(); if (!e) return;
                var defaults = (window.EJS_defaultControls && window.EJS_defaultControls[0]) || {{}};
                var c = (e.controls && e.controls[0]) || defaults;

                Object.keys(inputMap).forEach(function(keyStr) {{
                    (function(idx) {{
                        var key = parseInt(keyStr, 10);
                        var row = document.createElement('div');
                        row.className = 'je-bind-row';
                        
                        var entry = c[key] || defaults[key] || {{}};
                        
                        // Gamepads use value2 for primary, and sec_value2 for secondary
                        var rawVal1 = (entry.value2 !== undefined && entry.value2 !== '') ? entry.value2 : null;
                        var rawVal2 = (entry.sec_value2 !== undefined && entry.sec_value2 !== '') ? entry.sec_value2 : null;

                        var disp1 = rawVal1 !== null ? getFriendlyGpLabel(String(rawVal1)) : '—';
                        var disp2 = rawVal2 !== null ? getFriendlyGpLabel(String(rawVal2)) : '—';

                        row.innerHTML = '<span class=""je-bind-label"">' + inputMap[key] + '</span>' +
                                        '<span class=""je-bind-key"" data-target=""primary"">' + disp1 + '</span>' +
                                        '<span class=""je-bind-key"" data-target=""secondary"">' + disp2 + '</span>';

                        // Wire up both primary and secondary buttons
                        row.querySelectorAll('.je-bind-key').forEach(function(bk) {{
                            bk.addEventListener('click', function() {{
                                if (bk.classList.contains('je-listening')) return;
                                
                                var isSecondary = bk.getAttribute('data-target') === 'secondary';
                                bk.classList.add('je-listening');
                                bk.textContent = 'Move stick or press...';

                                // Snapshot current axes to prevent drift/accidental triggers
                                var baseAxes = [];
                                var gps0 = navigator.getGamepads ? navigator.getGamepads() : [];
                                for (var gi0 = 0; gi0 < gps0.length; gi0++) {{
                                    var p0 = gps0[gi0]; if (!p0) continue;
                                    for (var ai0 = 0; ai0 < p0.axes.length; ai0++) {{ baseAxes[ai0] = p0.axes[ai0]; }}
                                    break;
                                }}

                                var pollId = setInterval(function() {{
                                    var gps2 = navigator.getGamepads ? navigator.getGamepads() : [];
                                    for (var gi = 0; gi < gps2.length; gi++) {{
                                        var pad = gps2[gi]; if (!pad) continue;
                                        
                                        // 1. Check Buttons (Mirroring EJS standards)
                                        var ejsButtonMap = [
                                            'BUTTON_1', 'BUTTON_2', 'BUTTON_3', 'BUTTON_4',
                                            'LEFT_TOP_SHOULDER', 'RIGHT_TOP_SHOULDER', 
                                            'LEFT_BOTTOM_SHOULDER', 'RIGHT_BOTTOM_SHOULDER',
                                            'SELECT', 'START', 'LEFT_STICK', 'RIGHT_STICK',
                                            'DPAD_UP', 'DPAD_DOWN', 'DPAD_LEFT', 'DPAD_RIGHT'
                                        ];

                                        for (var bi = 0; bi < pad.buttons.length; bi++) {{
                                            if (pad.buttons[bi].pressed) {{
                                                clearInterval(pollId);
                                                var ejsVal = bi < ejsButtonMap.length ? ejsButtonMap[bi] : ('GAMEPAD_' + bi);
                                                
                                                bk.textContent = getFriendlyGpLabel(ejsVal);
                                                bk.classList.remove('je-listening');

                                                // Update emulator state
                                                if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                                                if (!e.controls[0]) e.controls[0] = {{}};
                                                if (!e.controls[0][key]) e.controls[0][key] = {{}};

                                                if (isSecondary) {{
                                                    e.controls[0][key].sec_value2 = ejsVal;
                                                }} else {{
                                                    e.controls[0][key].value2 = ejsVal;
                                                }}
                                                
                                                e.saveSettings();
                                                syncControlsToServer();
                                                return;
                                            }}
                                        }}

                                        // 2. Check Axes (Mirroring EJS standards)
                                        var ejsAxisMap = ['LEFT_STICK_X', 'LEFT_STICK_Y', 'RIGHT_STICK_X', 'RIGHT_STICK_Y'];
                                        for (var ai = 0; ai < pad.axes.length; ai++) {{
                                            var base = baseAxes[ai] || 0;
                                            var val = pad.axes[ai];
                                            
                                            // EJS standard threshold is > 0.5
                                            if (Math.abs(val) > 0.5 && Math.abs(val - base) > 0.5) {{
                                                clearInterval(pollId);
                                                
                                                var axisName = ai < ejsAxisMap.length ? ejsAxisMap[ai] : ('EXTRA_STICK_' + ai);
                                                var dir = val > 0 ? ':+1' : ':-1';
                                                var ejsVal = axisName + dir;
                                                
                                                bk.textContent = getFriendlyGpLabel(ejsVal);
                                                bk.classList.remove('je-listening');

                                                // Update emulator state
                                                if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                                                if (!e.controls[0]) e.controls[0] = {{}};
                                                if (!e.controls[0][key]) e.controls[0][key] = {{}};

                                                if (isSecondary) {{
                                                    e.controls[0][key].sec_value2 = ejsVal;
                                                }} else {{
                                                    e.controls[0][key].value2 = ejsVal;
                                                }}
                                                
                                                e.saveSettings();
                                                syncControlsToServer();
                                                return;
                                            }}
                                        }}
                                    }}
                                }}, 50);

                                // Cancel mapping if nothing is pressed for 10 seconds
                                setTimeout(function() {{
                                    if (bk.classList.contains('je-listening')) {{
                                        clearInterval(pollId);
                                        bk.classList.remove('je-listening');
                                        bk.textContent = isSecondary ? disp2 : disp1;
                                    }}
                                }}, 10000);
                            }});
                        }});
                        panel.appendChild(row);
                    }})(parseInt(keyStr, 10));
                }});
            }}

            function syncVGToggles() {{
                var e = emu(); if (!e) return;
                var vgOn = document.getElementById('je-vg-toggle');
                if (e.virtualGamepad) {{ vgOn.checked = e.virtualGamepad.style.display !== 'none'; }}
            }}
            document.getElementById('je-vg-toggle').addEventListener('change', function() {{
                var e = emu(); if (!e || !e.toggleVirtualGamepad) return;
                e.toggleVirtualGamepad(this.checked);
            }});
            document.getElementById('je-vg-lefty').addEventListener('change', function() {{
                var e = emu(); if (!e || !e.toggleVirtualGamepadLeftHanded) return;
                e.toggleVirtualGamepadLeftHanded(this.checked);
            }});
            document.getElementById('je-input-reset').addEventListener('click', function() {{
                var e = emu(); if (!e) return;
                
                if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                if (!e.controls[0]) e.controls[0] = {{}};

                // Pull directly from our explicit C# template, ignoring EJS's internal memory
                var defaults = window.EJS_defaultControls && window.EJS_defaultControls[0] ? window.EJS_defaultControls[0] : {{}};
                
                // Forcefully apply both value (Keyboard) and value2 (Gamepad)
                Object.keys(inputMap).forEach(function(keyStr) {{
                    var k = parseInt(keyStr, 10);
                    if (!e.controls[0][k]) e.controls[0][k] = {{}};
                    
                    if (defaults[k] && defaults[k].value !== undefined) {{
                        e.controls[0][k].value = defaults[k].value;
                    }} else {{
                        delete e.controls[0][k].value;
                    }}
                    
                    if (defaults[k] && defaults[k].value2 !== undefined) {{
                        e.controls[0][k].value2 = defaults[k].value2;
                    }} else {{
                        delete e.controls[0][k].value2;
                    }}
                }});

                e.saveSettings();
                syncControlsToServer();
                buildKeyboardBinds();
                buildGamepadBinds();
            }});

            // Screenshot
            document.getElementById('je-btn-screenshot').addEventListener('click', function() {{
                var g = gm(); if (!g) return;
                g.screenshot().then(function(pngBytes) {{
                    var blob = new Blob([pngBytes], {{ type: 'image/png' }});
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = (window.EJS_gameName || 'screenshot') + '.png';
                    a.click();
                    URL.revokeObjectURL(url);
                    // Also post to parent
                    try {{
                        var canvas = document.querySelector('canvas');
                        if (canvas) window.parent.postMessage({{ type: 'jellyemu-screenshot', itemId: '{itemId}', dataUrl: canvas.toDataURL('image/png') }}, '*');
                    }} catch(ex) {{}}
                }}).catch(function(err) {{ console.warn('[JellyEmu] Screenshot failed:', err); }});
            }});

            // Settings popup
            document.getElementById('je-btn-settings').addEventListener('click', function() {{
                syncSettingsUI();
                openPopup('je-pop-settings');
            }});
            function syncSettingsUI() {{
                var e = emu(); if (!e) return;
                var sel = document.getElementById('je-set-shader');
                // Sync current values
                if (e.settings) {{
                    if (e.settings['shader']) sel.value = e.settings['shader'];
                    if (e.settings['ff-ratio']) document.getElementById('je-set-ffrate').value = e.settings['ff-ratio'];
                    if (e.settings['sm-ratio']) document.getElementById('je-set-smrate').value = e.settings['sm-ratio'];
                    var rot = e.settings['videoRotation'];
                    if (rot !== undefined) document.getElementById('je-set-rotation').value = rot;
                    document.getElementById('je-set-vsync').checked = e.settings['vsync'] !== 'disabled';
                }}
            }}
            document.getElementById('je-set-shader').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('shader', this.value);
                syncControlsToServer();
            }});
            document.getElementById('je-set-vsync').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('vsync', this.checked ? 'enabled' : 'disabled');
                syncControlsToServer();
            }});
            document.getElementById('je-set-rotation').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('videoRotation', parseInt(this.value));
                syncControlsToServer();
            }});
            document.getElementById('je-set-ffrate').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('ff-ratio', this.value);
                syncControlsToServer();
            }});
            document.getElementById('je-set-smrate').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('sm-ratio', this.value);
                syncControlsToServer();
            }});

            // Screen Size
            document.getElementById('je-set-screensize').addEventListener('change', function() {{
                var val = this.value;
                var e = emu(); if (!e) return;
                var canvas = e.canvas || document.querySelector('canvas');
                var parent = canvas ? canvas.parentElement : null;
                if (!canvas || !parent) return;
                if (val === 'fit') {{
                    canvas.style.width = '100%';
                    canvas.style.height = '100%';
                    canvas.style.objectFit = 'contain';
                    parent.style.display = 'flex';
                    parent.style.alignItems = 'center';
                    parent.style.justifyContent = 'center';
                }} else {{
                    var w = e.gameManager ? e.gameManager.getVideoDimensions('width') : 256;
                    var h = e.gameManager ? e.gameManager.getVideoDimensions('height') : 224;
                    var mult = val === 'native' ? 1 : parseInt(val);
                    canvas.style.width = (w * mult) + 'px';
                    canvas.style.height = (h * mult) + 'px';
                    canvas.style.objectFit = '';
                    parent.style.display = 'flex';
                    parent.style.alignItems = 'center';
                    parent.style.justifyContent = 'center';
                }}
            }});

            // FPS Counter
            var fpsEl = document.getElementById('je-fps');
            var fpsOn = false;
            var fpsFrames = 0;
            var fpsLast = performance.now();
            function fpsLoop() {{
                if (!fpsOn) return;
                fpsFrames++;
                var now = performance.now();
                if (now - fpsLast >= 1000) {{
                    fpsEl.textContent = fpsFrames + ' FPS';
                    fpsFrames = 0;
                    fpsLast = now;
                }}
                requestAnimationFrame(fpsLoop);
            }}
            document.getElementById('je-set-fps').addEventListener('change', function() {{
                fpsOn = this.checked;
                if (fpsOn) {{
                    fpsEl.classList.add('je-active');
                    fpsFrames = 0;
                    fpsLast = performance.now();
                    requestAnimationFrame(fpsLoop);
                }} else {{
                    fpsEl.classList.remove('je-active');
                }}
            }});

            // Dock minimize / expand
            var dockMinimized = false;
            var dockMinBtn = document.getElementById('je-dock-min');
            // Add minimize button to end of dock
            var minDockBtn = document.createElement('button');
            minDockBtn.className = 'je-dockbtn';
            minDockBtn.title = 'Minimize';
            minDockBtn.innerHTML = '<svg viewBox=""0 0 24 24""><path d=""M19 13H5v-2h14v2z"" fill=""currentColor""/></svg>';
            dock.appendChild(minDockBtn);
            minDockBtn.addEventListener('click', function() {{
                dockMinimized = true;
                dock.classList.add('je-minimized');
                dockMinBtn.classList.add('je-active');
                dockMinBtn.classList.remove('je-hidden');
            }});
            dockMinBtn.addEventListener('click', function() {{
                dockMinimized = false;
                dock.classList.remove('je-minimized');
                dockMinBtn.classList.remove('je-active');
                showDocks();
            }});
            // Override showDocks to handle minimize FAB visibility
            var _origShowDocks = showDocks;
            showDocks = function() {{
                _origShowDocks();
                if (dockMinimized) {{
                    dockMinBtn.classList.remove('je-hidden');
                }}
            }};

            // Dock popup buttons
            document.getElementById('je-btn-inputmap').addEventListener('click', function() {{
                buildKeyboardBinds();
                buildGamepadBinds();
                syncVGToggles();
                openPopup('je-pop-inputmap');
            }});
        }})();
    </script>
    {/*<script>
        (function() {{
            var statusEl = document.getElementById('je-loader-status');
            if (statusEl) statusEl.textContent = 'Downloading ROM...';

            // Standard GET request, completely bypassing HEAD
            fetch('{romUrl}')
                .then(function(response) {{
                    if (!response.ok) throw new Error('HTTP ' + response.status);
                    return response.blob();
                }})
                .then(function(blob) {{
                    if (statusEl) statusEl.textContent = 'Initializing Emulator...';
                    
                    // Extract the filename from your URL so EJS knows if it's a .zip, .gbc, etc.
                    var fileName = ""{romUrl}"".split('/').pop() || ""rom.zip"";
                    if (fileName.indexOf('?') > -1) fileName = fileName.split('?')[0];
                    
                    // Create the native File object in memory
                    console.log(decodeURIComponent(fileName));
                    window.EJS_gameUrl = new File([blob], decodeURIComponent(fileName));
                }})
                .catch(function(err) {{
                    console.error('[JellyEmu] Direct Fetch Failed:', err);
                    if (statusEl) statusEl.textContent = 'ROM Download Failed';
                }});
        }})();
    </script>*/""}
    <script>
        window.EJS_player        = '#game';
        window.EJS_core          = '{core}';
        window.EJS_gameUrl       = '{romUrl}';
        window.EJS_gameName      = '{gameName}';
        window.EJS_pathtodata    = '{ejsBase}/';
        window.EJS_startOnLoaded = true;
        window.EJS_askBeforeExit = true;
        window.EJS_color         = '#00a4dc';

        window.EJS_DEBUG_XX = window.debug;
        if (window.language !== ""auto"") {{
            window.EJS_language = window.language;
        }}
        
        // Inject default options for save states, shader and video rotation
        window.EJS_defaultOptions = {{
            {(string.IsNullOrEmpty(activeShader) ? "" : $",\n            'shader': '{activeShader}'")}
        }};
        {(videoRotation != 0 ? $"window.EJS_videoRotation = {videoRotation};" : "// EJS_videoRotation: 0 (default, no rotation)")}
        {(core is "dos" or "psp" ? "window.EJS_threads = true;" : "// EJS_threads not required for this core")}

        {(!string.IsNullOrEmpty(igdbId) ? $"window.EJS_gameID = {igdbId};" : "")}
        // EJS_cheats not injected at startup — loaded lazily client-side on cheat popup open
        {(hasNetplay ? $@"window.EJS_netplayServer = '{netplayServer}';
        window.EJS_netplayICEServers = [
            {{ urls: 'stun:stun.l.google.com:19302' }},
            {{ urls: 'stun:stun1.l.google.com:19302' }},
            {{ urls: 'stun:stun2.l.google.com:19302' }},
            {{ urls: 'stun:stun.nextcloud.com:3478' }},
            {{ urls: 'turn:openrelay.metered.ca:80',  username: 'openrelayproject', credential: 'openrelayproject' }},
            {{ urls: 'turn:openrelay.metered.ca:443', username: 'openrelayproject', credential: 'openrelayproject' }}
        ];" : "")}

        {(saveExists ? $"window.EJS_loadStateURL = '{saveGetUrl}';" : "")}
        {(hasSaves ? $@"
        // Read the auth token Jellyfin's web UI already stored in localStorage
        var _jellyToken = '';
        try {{
            var _jellyCreds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{{}}');
            var _jellyServer = (_jellyCreds.Servers || []).find(function(s) {{ return s.UserId === '{userId}'; }});
            _jellyToken = (_jellyServer && _jellyServer.AccessToken) || '';
        }} catch(e) {{}}

        // Mark game as played in Jellyfin when the emulator launches
        fetch('/Users/{userId}/PlayedItems/{itemId}', {{
            method: 'POST',
            headers: {{ 'X-Emby-Authorization': 'MediaBrowser Client=""JellyEmu"", Device=""Browser"", DeviceId=""jellyemu"", Version=""1.0"", Token=""' + _jellyToken + '""' }}
        }}).catch(function(err) {{
            console.warn('[JellyEmu] Could not mark item as played:', err);
        }});

        // Open a Jellyfin session so the game appears in Dashboard → Active Sessions
        var _jellyEmuDeviceId = 'jellyemu-' + Math.random().toString(36).slice(2, 10);
        fetch('/jellyemu/session/start/{itemId}/{userId}', {{
            method: 'POST',
            headers: {{
                'X-JellyEmu-DeviceId':   _jellyEmuDeviceId,
                'X-JellyEmu-DeviceName': (navigator.userAgent.indexOf('Mobi') !== -1 ? 'JellyEmu Mobile' : 'JellyEmu Browser')
            }}
        }}).catch(function(err) {{
            console.warn('[JellyEmu] Could not open session:', err);
        }});

        // Ping the session every 30 s to keep it alive and advance the elapsed timer
        var _jellyEmuPingInterval = setInterval(function() {{
            fetch('/jellyemu/session/ping/{itemId}/{userId}', {{ method: 'POST' }})
                .catch(function() {{}});
        }}, 30000);

        // Record session start time for playtime tracking
        var _jellyEmuSessionStart = Date.now();

        // Auto-upload save state whenever EmulatorJS writes one
        // Capture the canvas and POST as save-state screenshot.
        // preserveDrawingBuffer:true is patched onto getContext above so
        // toDataURL works at any time, not just during the render tick.
        function _jePostCanvasScreenshot(savePostPromise) {{
            var dataUrl = null;
            try {{
                var canvas = document.querySelector('canvas.ejs_canvas') ||
                             document.querySelector('#game canvas') ||
                             document.querySelector('canvas');
                if (canvas && canvas.width > 0 && canvas.height > 0) {{
                    dataUrl = canvas.toDataURL('image/jpeg', 0.88);
                }}
            }} catch(e) {{ console.warn('[JellyEmu] Screenshot capture failed:', e); }}
            if (!dataUrl || !dataUrl.startsWith('data:image')) return;
            (savePostPromise || Promise.resolve()).then(function() {{
                fetch('/jellyemu/save-screenshot/{itemId}/{userId}/{activeSlot}', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ dataUrl: dataUrl }})
                }}).catch(function() {{}});
            }});
        }}

        window.EJS_onSaveState = function(e) {{
            if (!e || !e.state) return;
            var state = _jeEnsureBinary(e.state);
            console.log('[JellyEmu] Pipeline STAGE 1 (Client AutoGen): Payload size ->', state.size || state.byteLength, 'bytes');
            // Capture canvas NOW — synchronously, before the fetch, so we get the
            // current game frame rather than whatever is on screen after the round-trip
            var savePromise = fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: state
            }}).then(function(r) {{
                if (!r.ok) throw new Error('Server rejected save');
                console.log('[JellyEmu] Save uploaded, status:', r.status);
                try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
            }}).catch(function(err) {{
                console.error('[JellyEmu] Save upload failed:', err);
            }});
            _jePostCanvasScreenshot(savePromise);
        }};
        // EJS_onSaveUpdate fires on battery/SRAM saves detected by hash comparison.
        window.EJS_onSaveUpdate = function(e) {{
            if (!e || !e.save) return;
            var save = _jeEnsureBinary(e.save);
            console.log('[JellyEmu] Pipeline STAGE 1 (Client SRAM Gen): Payload size ->', save.size || save.byteLength, 'bytes');
            var savePromise = fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: save
            }}).then(function(r) {{
                if (!r.ok) throw new Error('Server rejected save');
                try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
            }}).catch(function(err) {{
                console.error('[JellyEmu] Save update upload failed:', err);
            }});
            _jePostCanvasScreenshot(savePromise);
        }};
        // Auto-save on exit if the user pref is enabled.
        // EJS_onExit is called by both EmulatorJS's own exit menu item and our
        // custom exit button. The _jellyEmuExiting flag prevents double-firing.
        var _jellyEmuExiting = false;
        window.EJS_onExit = function() {{
            if (_jellyEmuExiting) return;
            _jellyEmuExiting = true;

            var prefs = {{}};
            try {{ prefs = JSON.parse(localStorage.getItem('jellyemu-userprefs') || '{{}}'); }} catch(e) {{}}
            var autoSave = prefs.autosave !== 'false'; // default on

            // Stop the session ping and close the Jellyfin session
            clearInterval(_jellyEmuPingInterval);
            var sessionStop = fetch('/jellyemu/session/stop/{itemId}/{userId}', {{ method: 'POST' }})
                .catch(function() {{}});

            // Always record playtime for this session
            var sessionSeconds = Math.round((Date.now() - (_jellyEmuSessionStart || Date.now())) / 1000);
            var playtimeFlush = sessionSeconds > 0
                ? fetch('/jellyemu/playtime/{itemId}/{userId}', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'text/plain' }},
                    body: String(sessionSeconds)
                  }}).catch(function() {{}})
                : Promise.resolve();

            function closeIframe() {{
                if (window.parent === window) {{
                    // New tab: broadcast exit signal, then close self
                    try {{ var ch = new BroadcastChannel('jellyemu-exit'); ch.postMessage('close-jellyemu'); ch.close(); }} catch(e) {{}}
                    window.close();
                }} else {{
                    // Iframe: tell parent to remove it
                    window.parent.postMessage('close-jellyemu', '*');
                }}
            }}

            function _fallbackClose() {{
                Promise.all([sessionStop, playtimeFlush]).finally(function() {{
                    try {{ window.parent.postMessage({{ type: 'jellyemu-session-end', itemId: '{itemId}', seconds: sessionSeconds }}, '*'); }} catch(_) {{}}
                    closeIframe();
                }});
            }}

            if (!autoSave) {{
                _fallbackClose();
                return;
            }}
            
            try {{
                if (!window.EJS_emulator || !window.EJS_emulator.gameManager) throw new Error('EJS_emulator not fully initialized');
                
                EJS_emulator.gameManager.saveSaveFiles();
                Promise.resolve(EJS_emulator.gameManager.getSaveFile()).then(function(rawState) {{
                    var stateData = _jeEnsureBinary(rawState);
                    if (!stateData) {{
                        _fallbackClose();
                        return;
                    }}
                    // Capture before the fetch so rAF fires on the current frame
                    var saveFlush = fetch('{savePostUrl}', {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/octet-stream' }},
                        body: stateData
                    }}).then(function(r) {{
                        if (!r.ok) throw new Error('Server rejected save');
                        try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
                    }}).catch(function(err) {{
                        console.error('[JellyEmu] Auto-save on exit failed:', err);
                    }});
                    _jePostCanvasScreenshot(saveFlush);
                    Promise.all([sessionStop, playtimeFlush, saveFlush]).finally(function() {{
                        // Notify parent of session end for Romm playtime reporting
                        try {{ window.parent.postMessage({{ type: 'jellyemu-session-end', itemId: '{itemId}', seconds: sessionSeconds }}, '*'); }} catch(_) {{}}
                        closeIframe();
                    }});
                }}).catch(function(err) {{
                    console.warn('[JellyEmu] Promise.resolve(getSaveFile) failed', err);
                    _fallbackClose();
                }});
            }} catch (ex) {{
                console.warn('[JellyEmu] Auto-save sequence crashed, triggering fallback exit:', ex);
                _fallbackClose();
            }}
        }};

        // Hook EmulatorJS screenshot button to push to Romm via parent
        document.addEventListener('click', function(ev) {{
            var btn = ev.target.closest && ev.target.closest('[data-action=""screenshot""], .ejs-screenshot-btn, button[title*=""creenshot""], button[aria-label*=""creenshot""]');
            if (!btn) return;
            // Give EJS a tick to generate the canvas then grab it
            setTimeout(function() {{
                try {{
                    var canvas = document.querySelector('#game canvas') || document.querySelector('canvas');
                    if (!canvas) return;
                    var dataUrl = canvas.toDataURL('image/png');
                    window.parent.postMessage({{ type: 'jellyemu-screenshot', itemId: '{itemId}', dataUrl: dataUrl }}, '*');
                }} catch(e) {{ console.warn('[JellyEmu] Screenshot capture failed:', e); }}
            }}, 200);
        }});
" : "")}
    </script>
    <script src=""{ejsBase}/loader.js""></script>
</body>
</html>";

            // When opened as a new tab (threaded cores), these headers make the page
            // cross-origin isolated so SharedArrayBuffer is available. Harmless for iframe mode.
            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";

            return Content(html, MediaTypeNames.Text.Html);
        }


        [HttpGet("/jellyemu/rom/{itemId}/{filename?}")]
        [HttpHead("/jellyemu/rom/{itemId}/{filename?}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Rom(string itemId, string? filename = null)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path) || !System.IO.File.Exists(item.Path))
            {
                Logger.LogWarning("[JellyEmu] Rom: item {ItemId} not found or path missing", itemId);
                return NotFound();
            }

            Logger.LogInformation("[JellyEmu] Serving ROM: {Path}", item.Path);

            var stream = System.IO.File.OpenRead(item.Path);
            var fileName = Path.GetFileName(item.Path);
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }

        /// <summary>
        /// Returns the resolved core name and whether it requires threads (SharedArrayBuffer)
        /// for the given item. Used by the UI to decide iframe vs new tab launch.
        /// 
        /// Path: GET /jellyemu/core/{itemId}
        /// Parameters:
        ///   - itemId (string, path): The unique ID of the item.
        /// Returns Example: { "core": "gba", "needsThreads": false }
        /// </summary>
        [HttpGet("/jellyemu/core/{itemId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetCore(string itemId)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
                return NotFound();

            var core = ResolveCore(item);
            var needsThreads = core is "dos" or "psp";
            return Ok(new { core, needsThreads });
        }


    }
}