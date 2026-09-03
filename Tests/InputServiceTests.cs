using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using JellyEmu.Controllers;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyEmu.Tests
{
    public class InputServiceTests
    {
        private readonly JellyEmuInputService _inputService;

        public InputServiceTests()
        {
            _inputService = new JellyEmuInputService();
        }

        [Theory]
        [InlineData("mupen64plus_next", "n64")]
        [InlineData("parallel_n64", "n64")]
        [InlineData("genesis_plus_gx", "segaMD")]
        [InlineData("snes9x", "snes")]
        [InlineData("nestopia", "nes")]
        [InlineData("pcsx_rearmed", "psx")]
        [InlineData("ppsspp", "psp")]
        [InlineData("mgba", "gba")]
        [InlineData("mednafen_pce", "pce")]
        [InlineData("opera", "3do")]
        [InlineData("stella2014", "atari2600")]
        [InlineData("gearcoleco", "coleco")]
        [InlineData("fbneo", "arcade")]
        public void ResolveSchemeKey_Core_ShouldResolveCorrectly(string core, string expectedScheme)
        {
            var result = JellyEmuInputService.ResolveSchemeKey(core);
            Assert.Equal(expectedScheme, result);
        }

        [Theory]
        [InlineData("N64", "n64")]
        [InlineData("Nintendo 64", "n64")]
        [InlineData("Sega Genesis", "segaMD")]
        [InlineData("Genesis", "segaMD")]
        [InlineData("Mega Drive", "segaMD")]
        [InlineData("Super Nintendo", "snes")]
        [InlineData("NES", "nes")]
        [InlineData("PlayStation", "psx")]
        [InlineData("PlayStation Portable", "psp")]
        [InlineData("Game Boy Advance", "gba")]
        [InlineData("TurboGrafx-16", "pce")]
        [InlineData("3DO", "3do")]
        [InlineData("Atari 2600", "atari2600")]
        [InlineData("Arcade", "arcade")]
        public void ResolveSchemeKey_PlatformTag_ShouldResolveCorrectly(string tag, string expectedScheme)
        {
            var result = JellyEmuInputService.ResolveSchemeKey(tag);
            Assert.Equal(expectedScheme, result);
        }

        [Fact]
        public void GetScheme_N64_ShouldIncludeNativeButtonsAndAnalogAxes()
        {
            var scheme = _inputService.GetScheme("N64");

            Assert.Equal("n64", scheme.Id);
            Assert.Equal("Nintendo 64", scheme.Name);

            // Verify specific N64 buttons exist
            Assert.Contains(scheme.Buttons, b => b.Id == 0 && b.Label == "A");
            Assert.Contains(scheme.Buttons, b => b.Id == 1 && b.Label == "B");
            Assert.Contains(scheme.Buttons, b => b.Id == 12 && b.Label == "Z");
            Assert.Contains(scheme.Buttons, b => b.Id == 16 && b.Label == "STICK RIGHT");
            Assert.Contains(scheme.Buttons, b => b.Id == 20 && b.Label == "C-PAD RIGHT");

            // Verify hotkeys are appended
            Assert.Contains(scheme.Buttons, b => b.Id == 24 && b.Label == "QUICK SAVE");
            Assert.Contains(scheme.Buttons, b => b.Id == 27 && b.Label == "FAST FORWARD");

            // Verify analog axes
            Assert.Contains(16, scheme.AnalogAxes);
            Assert.Contains(17, scheme.AnalogAxes);
            Assert.Contains(18, scheme.AnalogAxes);
            Assert.Contains(19, scheme.AnalogAxes);

            // Verify default bindings exist for all buttons
            foreach (var btn in scheme.Buttons)
            {
                Assert.True(scheme.DefaultBindings.ContainsKey(btn.Id));
            }
        }

        [Fact]
        public void GetScheme_SegaGenesis_ShouldInclude6ButtonLayout()
        {
            var scheme = _inputService.GetScheme("genesis_plus_gx");

            Assert.Equal("segaMD", scheme.Id);
            Assert.Contains(scheme.Buttons, b => b.Id == 1 && b.Label == "A");
            Assert.Contains(scheme.Buttons, b => b.Id == 0 && b.Label == "B");
            Assert.Contains(scheme.Buttons, b => b.Id == 8 && b.Label == "C");
            Assert.Contains(scheme.Buttons, b => b.Id == 10 && b.Label == "X");
            Assert.Contains(scheme.Buttons, b => b.Id == 9 && b.Label == "Y");
            Assert.Contains(scheme.Buttons, b => b.Id == 11 && b.Label == "Z");
            Assert.Contains(scheme.Buttons, b => b.Id == 2 && b.Label == "MODE");
        }

        [Fact]
        public void GetScheme_PlayStation_ShouldIncludeSymbolButtons()
        {
            var scheme = _inputService.GetScheme("PlayStation");

            Assert.Equal("psx", scheme.Id);
            Assert.Contains(scheme.Buttons, b => b.Id == 9 && b.Label == "△ TRIANGLE");
            Assert.Contains(scheme.Buttons, b => b.Id == 1 && b.Label == "□ SQUARE");
            Assert.Contains(scheme.Buttons, b => b.Id == 0 && b.Label == "⨯ CROSS");
            Assert.Contains(scheme.Buttons, b => b.Id == 8 && b.Label == "○ CIRCLE");
            Assert.Contains(scheme.Buttons, b => b.Id == 10 && b.Label == "L1");
            Assert.Contains(scheme.Buttons, b => b.Id == 12 && b.Label == "L2");
            Assert.Contains(scheme.Buttons, b => b.Id == 14 && b.Label == "L3");
        }

        [Fact]
        public void GetScheme_GameBoy_ShouldHaveAAsButton1AndBAsButton3Or2()
        {
            var scheme = _inputService.GetScheme("Game Boy");

            Assert.Equal("gb", scheme.Id);
            Assert.Contains(scheme.Buttons, b => b.Id == 8 && b.Label == "A");
            Assert.Contains(scheme.Buttons, b => b.Id == 0 && b.Label == "B");

            // A (ID 8) should default to BUTTON_1 (A / Cross)
            Assert.True(scheme.DefaultBindings.ContainsKey(8));
            Assert.Equal("BUTTON_1", scheme.DefaultBindings[8].Gp1);

            // B (ID 0) should default to BUTTON_3 (X / Square) with secondary BUTTON_2 (B / Circle)
            Assert.True(scheme.DefaultBindings.ContainsKey(0));
            Assert.Equal("BUTTON_3", scheme.DefaultBindings[0].Gp1);
            Assert.Equal("BUTTON_2", scheme.DefaultBindings[0].Gp2);
        }

        [Fact]
        public void GetScheme_GameBoyAdvance_ShouldHaveAAsButton1AndBAsButton3Or2()
        {
            var scheme = _inputService.GetScheme("GBA");

            Assert.Equal("gba", scheme.Id);
            Assert.Contains(scheme.Buttons, b => b.Id == 8 && b.Label == "A");
            Assert.Contains(scheme.Buttons, b => b.Id == 0 && b.Label == "B");

            // A (ID 8) should default to BUTTON_1 (A / Cross)
            Assert.Equal("BUTTON_1", scheme.DefaultBindings[8].Gp1);

            // B (ID 0) should default to BUTTON_3 (X / Square) with secondary BUTTON_2 (B / Circle)
            Assert.Equal("BUTTON_3", scheme.DefaultBindings[0].Gp1);
            Assert.Equal("BUTTON_2", scheme.DefaultBindings[0].Gp2);

            // Shoulders L and R
            Assert.Equal("LEFT_TOP_SHOULDER", scheme.DefaultBindings[10].Gp1);
            Assert.Equal("RIGHT_TOP_SHOULDER", scheme.DefaultBindings[11].Gp1);
        }

        [Fact]
        public void GetScheme_NintendoDS_ShouldIncludeMicrophoneButton()
        {
            var scheme = _inputService.GetScheme("Nintendo DS");

            Assert.Equal("nds", scheme.Id);
            Assert.Contains(scheme.Buttons, b => b.Id == 14 && b.Label == "MICROPHONE");

            // Verify Microphone default bindings (M key, Left Stick L3 click)
            Assert.True(scheme.DefaultBindings.ContainsKey(14));
            Assert.Equal(77, scheme.DefaultBindings[14].Kb1); // 'M' key
            Assert.Equal("LEFT_STICK", scheme.DefaultBindings[14].Gp1);
            Assert.Equal("RIGHT_STICK", scheme.DefaultBindings[14].Gp2);

            // Verify EVERY button in NDS scheme has a default binding
            foreach (var btn in scheme.Buttons)
            {
                Assert.True(scheme.DefaultBindings.ContainsKey(btn.Id), $"Missing default binding for button {btn.Id} ({btn.Label})");
            }
        }

        [Fact]
        public void GetAllSchemes_ShouldReturnAllSupportedSystems()
        {
            var all = _inputService.GetAllSchemes();

            Assert.True(all.Count >= 24);
            Assert.True(all.ContainsKey("gb"));
            Assert.True(all.ContainsKey("nes"));
            Assert.True(all.ContainsKey("snes"));
            Assert.True(all.ContainsKey("n64"));
            Assert.True(all.ContainsKey("segaMD"));
            Assert.True(all.ContainsKey("segaSaturn"));
            Assert.True(all.ContainsKey("psx"));
            Assert.True(all.ContainsKey("psp"));
            Assert.True(all.ContainsKey("pce"));
            Assert.True(all.ContainsKey("arcade"));
            Assert.True(all.ContainsKey("default"));
        }

        [Fact]
        public void InputController_Endpoints_ShouldReturnOk()
        {
            var controller = new JellyEmuInputController(
                null!,
                null!,
                NullLogger<JellyEmuInputController>.Instance,
                null!,
                null!,
                null!,
                _inputService);

            var allResult = controller.GetAllSchemes();
            Assert.IsType<OkObjectResult>(allResult);

            var singleResult = controller.GetScheme("N64");
            Assert.IsType<OkObjectResult>(singleResult);
        }

        [Fact]
        public void EjsTemplate_ShouldParseAndRenderWithoutErrors()
        {
            var assembly = typeof(JellyEmuPlayController).Assembly;
            var resourceName = "JellyEmu.Templates.ejs.html";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            Assert.NotNull(stream);

            using var reader = new System.IO.StreamReader(stream!);
            var templateContent = reader.ReadToEnd();
            var template = Scriban.Template.Parse(templateContent);

            Assert.False(template.HasErrors, string.Join("; ", template.Messages));

            var rendered = template.Render(new
            {
                game_name = "Test Game",
                core = "mgba",
                platform_tag = "GBA",
                input_scheme_json = "{\"id\":\"gba\"}",
                custom_bindings_json = "{\"0\":{\"gp1\":\"BUTTON_1\"}}",
                available_cores_json = "[]",
                rom_url = "/test.rom",
                ejs_base = "/ejs",
                item_id = "123",
                user_id = "user1",
                bios_url = "",
                active_slot = 1,
                slot_value = 1,
                has_saves = true,
                active_shader = "",
                video_rotation = 0,
                igdb_id = "",
                has_netplay = false,
                netplay_server = "",
                save_exists = false,
                save_get_url = "",
                save_post_url = "",
                is_m3u = false,
                needs_threads = false,
                virtual_gamepad = "0",
                virtual_gamepad_lefty = "0",
                vsync = "1",
                ffrate = "3",
                smrate = "3",
                show_fps = "0",
                scale = "fit",
                volume = "1",
                mute = "0",
                version = "0.8.8"
            });

            Assert.NotNull(rendered);
            Assert.Contains("inputScheme: {\"id\":\"gba\"}", rendered);
            Assert.Contains("customBindings: {\"0\":{\"gp1\":\"BUTTON_1\"}}", rendered);
        }
    }
}
