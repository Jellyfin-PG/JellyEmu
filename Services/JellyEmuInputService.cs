using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyEmu.Services
{
    public class InputButtonDefinition
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class InputBindingDefault
    {
        public int Kb1 { get; set; }
        public int Kb2 { get; set; }
        public string Gp1 { get; set; } = string.Empty;
        public string Gp2 { get; set; } = string.Empty;
        public string Activation { get; set; } = "press";
        public int HoldDuration { get; set; } = 5;
    }

    public class PlatformControlScheme
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int MaxPlayers { get; set; } = 2;
        public List<InputButtonDefinition> Buttons { get; set; } = new();
        public Dictionary<int, InputBindingDefault> DefaultBindings { get; set; } = new();
        public Dictionary<int, Dictionary<int, InputBindingDefault>> PlayerDefaultBindings { get; set; } = new();
        public List<int> AnalogAxes { get; set; } = new();
    }

    public class JellyEmuInputService
    {
        public static readonly List<InputButtonDefinition> Hotkeys = new()
        {
            new() { Id = 24, Label = "QUICK SAVE", Description = "Save state immediately" },
            new() { Id = 25, Label = "QUICK LOAD", Description = "Load state immediately" },
            new() { Id = 26, Label = "CHANGE SLOT", Description = "Cycle active save state slot" },
            new() { Id = 27, Label = "FAST FORWARD", Description = "Toggle fast forward emulation" },
            new() { Id = 28, Label = "REWIND", Description = "Rewind gameplay in real time" },
            new() { Id = 29, Label = "SLOW MOTION", Description = "Toggle slow motion gameplay" },
            new() { Id = 30, Label = "EXIT GAME", Description = "Exit emulation and return to Jellyfin" },
            new() { Id = 31, Label = "PAUSE / RESUME", Description = "Pause or resume emulation" },
            new() { Id = 32, Label = "RESTART GAME", Description = "Restart / reset current game" },
            new() { Id = 33, Label = "MUTE AUDIO", Description = "Toggle audio mute on/off" },
            new() { Id = 34, Label = "SCREENSHOT", Description = "Capture in-game screenshot" },
            new() { Id = 35, Label = "FULLSCREEN", Description = "Toggle fullscreen display" }
        };

        private static readonly Dictionary<int, InputBindingDefault> BaseDefaultBindings = new()
        {
            { 0,  new() { Kb1 = 88,  Kb2 = 0, Gp1 = "BUTTON_2",              Gp2 = "" } },
            { 1,  new() { Kb1 = 83,  Kb2 = 0, Gp1 = "BUTTON_4",              Gp2 = "" } },
            { 2,  new() { Kb1 = 86,  Kb2 = 0, Gp1 = "SELECT",                Gp2 = "" } },
            { 3,  new() { Kb1 = 13,  Kb2 = 0, Gp1 = "START",                 Gp2 = "" } },
            { 4,  new() { Kb1 = 38,  Kb2 = 0, Gp1 = "DPAD_UP",               Gp2 = "LEFT_STICK_Y:-1" } },
            { 5,  new() { Kb1 = 40,  Kb2 = 0, Gp1 = "DPAD_DOWN",             Gp2 = "LEFT_STICK_Y:+1" } },
            { 6,  new() { Kb1 = 37,  Kb2 = 0, Gp1 = "DPAD_LEFT",             Gp2 = "LEFT_STICK_X:-1" } },
            { 7,  new() { Kb1 = 39,  Kb2 = 0, Gp1 = "DPAD_RIGHT",            Gp2 = "LEFT_STICK_X:+1" } },
            { 8,  new() { Kb1 = 90,  Kb2 = 0, Gp1 = "BUTTON_1",              Gp2 = "" } },
            { 9,  new() { Kb1 = 65,  Kb2 = 0, Gp1 = "BUTTON_3",              Gp2 = "" } },
            { 10, new() { Kb1 = 81,  Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER",     Gp2 = "" } },
            { 11, new() { Kb1 = 69,  Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER",    Gp2 = "" } },
            { 12, new() { Kb1 = 9,   Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER",  Gp2 = "" } },
            { 13, new() { Kb1 = 82,  Kb2 = 0, Gp1 = "RIGHT_BOTTOM_SHOULDER", Gp2 = "" } },
            { 14, new() { Kb1 = 0,   Kb2 = 0, Gp1 = "LEFT_STICK",            Gp2 = "" } },
            { 15, new() { Kb1 = 0,   Kb2 = 0, Gp1 = "RIGHT_STICK",           Gp2 = "" } },
            { 16, new() { Kb1 = 72,  Kb2 = 0, Gp1 = "LEFT_STICK_X:+1",       Gp2 = "" } },
            { 17, new() { Kb1 = 70,  Kb2 = 0, Gp1 = "LEFT_STICK_X:-1",       Gp2 = "" } },
            { 18, new() { Kb1 = 71,  Kb2 = 0, Gp1 = "LEFT_STICK_Y:+1",       Gp2 = "" } },
            { 19, new() { Kb1 = 84,  Kb2 = 0, Gp1 = "LEFT_STICK_Y:-1",       Gp2 = "" } },
            { 20, new() { Kb1 = 76,  Kb2 = 0, Gp1 = "RIGHT_STICK_X:+1",      Gp2 = "" } },
            { 21, new() { Kb1 = 74,  Kb2 = 0, Gp1 = "RIGHT_STICK_X:-1",      Gp2 = "" } },
            { 22, new() { Kb1 = 75,  Kb2 = 0, Gp1 = "RIGHT_STICK_Y:+1",      Gp2 = "" } },
            { 23, new() { Kb1 = 73,  Kb2 = 0, Gp1 = "RIGHT_STICK_Y:-1",      Gp2 = "" } },
            { 24, new() { Kb1 = 49,  Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 25, new() { Kb1 = 50,  Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 26, new() { Kb1 = 51,  Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 27, new() { Kb1 = 107, Kb2 = 9, Gp1 = "",                      Gp2 = "" } },
            { 28, new() { Kb1 = 8,   Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 29, new() { Kb1 = 109, Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 30, new() { Kb1 = 27,  Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 31, new() { Kb1 = 32,  Kb2 = 80, Gp1 = "",                     Gp2 = "" } },
            { 32, new() { Kb1 = 115, Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 33, new() { Kb1 = 77,  Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 34, new() { Kb1 = 123, Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 35, new() { Kb1 = 70,  Kb2 = 122, Gp1 = "",                    Gp2 = "" } }
        };

        private static readonly Dictionary<string, Dictionary<int, InputBindingDefault>> SchemeDefaultOverrides =
            new(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "gb", new()
                    {
                        { 8, new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },             // A -> A / Cross
                        { 0, new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "BUTTON_2" } },     // B -> X / Square & B / Circle
                        { 2, new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3, new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } }
                    }
                },
                {
                    "gba", new()
                    {
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },            // A -> A / Cross
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "BUTTON_2" } },    // B -> X / Square & B / Circle
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },   // L
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },  // R
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } }
                    }
                },
                {
                    "nes", new()
                    {
                        { 8, new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },             // A -> A / Cross
                        { 0, new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "BUTTON_2" } },     // B -> X / Square & B / Circle
                        { 2, new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3, new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } }
                    }
                },
                {
                    "snes", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },            // B -> A / Cross (bottom)
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },            // A -> B / Circle (right)
                        { 1,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },            // Y -> X / Square (left)
                        { 9,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } },            // X -> Y / Triangle (top)
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },   // L
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },  // R
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } }
                    }
                },
                {
                    "nds", new()
                    {
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },                        // A -> Right (Xbox B / PS Circle)
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },                        // B -> Bottom (Xbox A / PS Cross)
                        { 9,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } },                        // X -> Top (Xbox Y / PS Triangle)
                        { 1,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },                        // Y -> Left (Xbox X / PS Square)
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },                          // SELECT
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },                           // START
                        { 4,  new() { Kb1 = 38, Kb2 = 0, Gp1 = "DPAD_UP", Gp2 = "LEFT_STICK_Y:-1" } },         // UP
                        { 5,  new() { Kb1 = 40, Kb2 = 0, Gp1 = "DPAD_DOWN", Gp2 = "LEFT_STICK_Y:+1" } },       // DOWN
                        { 6,  new() { Kb1 = 37, Kb2 = 0, Gp1 = "DPAD_LEFT", Gp2 = "LEFT_STICK_X:-1" } },       // LEFT
                        { 7,  new() { Kb1 = 39, Kb2 = 0, Gp1 = "DPAD_RIGHT", Gp2 = "LEFT_STICK_X:+1" } },      // RIGHT
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },               // L
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },              // R
                        { 14, new() { Kb1 = 77, Kb2 = 0, Gp1 = "LEFT_STICK", Gp2 = "RIGHT_STICK" } }             // MICROPHONE -> M key / L3 / R3
                    }
                },
                {
                    "n64", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },
                        { 1,  new() { Kb1 = 67, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "BUTTON_2" } },
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },
                        { 4,  new() { Kb1 = 38, Kb2 = 0, Gp1 = "DPAD_UP", Gp2 = "" } },
                        { 5,  new() { Kb1 = 40, Kb2 = 0, Gp1 = "DPAD_DOWN", Gp2 = "" } },
                        { 6,  new() { Kb1 = 37, Kb2 = 0, Gp1 = "DPAD_LEFT", Gp2 = "" } },
                        { 7,  new() { Kb1 = 39, Kb2 = 0, Gp1 = "DPAD_RIGHT", Gp2 = "" } },
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },
                        { 12, new() { Kb1 = 90, Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER", Gp2 = "RIGHT_BOTTOM_SHOULDER" } },
                        { 16, new() { Kb1 = 76, Kb2 = 0, Gp1 = "LEFT_STICK_X:+1", Gp2 = "" } },
                        { 17, new() { Kb1 = 74, Kb2 = 0, Gp1 = "LEFT_STICK_X:-1", Gp2 = "" } },
                        { 18, new() { Kb1 = 75, Kb2 = 0, Gp1 = "LEFT_STICK_Y:+1", Gp2 = "" } },
                        { 19, new() { Kb1 = 73, Kb2 = 0, Gp1 = "LEFT_STICK_Y:-1", Gp2 = "" } },
                        { 20, new() { Kb1 = 68, Kb2 = 0, Gp1 = "RIGHT_STICK_X:+1", Gp2 = "" } },
                        { 21, new() { Kb1 = 65, Kb2 = 0, Gp1 = "RIGHT_STICK_X:-1", Gp2 = "" } },
                        { 22, new() { Kb1 = 83, Kb2 = 0, Gp1 = "RIGHT_STICK_Y:+1", Gp2 = "" } },
                        { 23, new() { Kb1 = 87, Kb2 = 0, Gp1 = "RIGHT_STICK_Y:-1", Gp2 = "BUTTON_4" } }
                    }
                },
                {
                    "segaMD", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } }, // B
                        { 1,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } }, // A
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },   // MODE
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },    // START
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } }, // C
                        { 9,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } }, // Y
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } }, // X
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } }  // Z
                    }
                },
                {
                    "segaSaturn", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },
                        { 1,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },
                        { 9,  new() { Kb1 = 81, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },
                        { 10, new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } },
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },
                        { 12, new() { Kb1 = 9,  Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER", Gp2 = "" } },
                        { 13, new() { Kb1 = 82, Kb2 = 0, Gp1 = "RIGHT_BOTTOM_SHOULDER", Gp2 = "" } }
                    }
                },
                {
                    "psx", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } }, // CROSS
                        { 1,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } }, // SQUARE
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },
                        { 4,  new() { Kb1 = 38, Kb2 = 0, Gp1 = "DPAD_UP", Gp2 = "" } },
                        { 5,  new() { Kb1 = 40, Kb2 = 0, Gp1 = "DPAD_DOWN", Gp2 = "" } },
                        { 6,  new() { Kb1 = 37, Kb2 = 0, Gp1 = "DPAD_LEFT", Gp2 = "" } },
                        { 7,  new() { Kb1 = 39, Kb2 = 0, Gp1 = "DPAD_RIGHT", Gp2 = "" } },
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } }, // CIRCLE
                        { 9,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } }, // TRIANGLE
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },
                        { 12, new() { Kb1 = 9,  Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER", Gp2 = "" } },
                        { 13, new() { Kb1 = 82, Kb2 = 0, Gp1 = "RIGHT_BOTTOM_SHOULDER", Gp2 = "" } },
                        { 14, new() { Kb1 = 0,  Kb2 = 0, Gp1 = "LEFT_STICK", Gp2 = "" } },
                        { 15, new() { Kb1 = 0,  Kb2 = 0, Gp1 = "RIGHT_STICK", Gp2 = "" } }
                    }
                },
                {
                    "ps2", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } }, // CROSS
                        { 1,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } }, // SQUARE
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },
                        { 4,  new() { Kb1 = 38, Kb2 = 0, Gp1 = "DPAD_UP", Gp2 = "" } },
                        { 5,  new() { Kb1 = 40, Kb2 = 0, Gp1 = "DPAD_DOWN", Gp2 = "" } },
                        { 6,  new() { Kb1 = 37, Kb2 = 0, Gp1 = "DPAD_LEFT", Gp2 = "" } },
                        { 7,  new() { Kb1 = 39, Kb2 = 0, Gp1 = "DPAD_RIGHT", Gp2 = "" } },
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } }, // CIRCLE
                        { 9,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } }, // TRIANGLE
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },
                        { 12, new() { Kb1 = 9,  Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER", Gp2 = "" } },
                        { 13, new() { Kb1 = 82, Kb2 = 0, Gp1 = "RIGHT_BOTTOM_SHOULDER", Gp2 = "" } },
                        { 14, new() { Kb1 = 0,  Kb2 = 0, Gp1 = "LEFT_STICK", Gp2 = "" } },
                        { 15, new() { Kb1 = 0,  Kb2 = 0, Gp1 = "RIGHT_STICK", Gp2 = "" } }
                    }
                },
                {
                    "psp", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },
                        { 1,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },
                        { 4,  new() { Kb1 = 38, Kb2 = 0, Gp1 = "DPAD_UP", Gp2 = "" } },
                        { 5,  new() { Kb1 = 40, Kb2 = 0, Gp1 = "DPAD_DOWN", Gp2 = "" } },
                        { 6,  new() { Kb1 = 37, Kb2 = 0, Gp1 = "DPAD_LEFT", Gp2 = "" } },
                        { 7,  new() { Kb1 = 39, Kb2 = 0, Gp1 = "DPAD_RIGHT", Gp2 = "" } },
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },
                        { 9,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } },
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } }
                    }
                },
                {
                    "pce", new()
                    {
                        { 0, new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },
                        { 2, new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },
                        { 3, new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },
                        { 8, new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } }
                    }
                },
                {
                    "segaMS", new()
                    {
                        { 0, new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },
                        { 8, new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } }
                    }
                },
                {
                    "segaGG", new()
                    {
                        { 0, new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },
                        { 3, new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },
                        { 8, new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } }
                    }
                },
                {
                    "3ds", new()
                    {
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },                        // A -> Right (Xbox B / PS Circle)
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },                        // B -> Bottom (Xbox A / PS Cross)
                        { 9,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } },                        // X -> Top (Xbox Y / PS Triangle)
                        { 1,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },                        // Y -> Left (Xbox X / PS Square)
                        { 2,  new() { Kb1 = 86, Kb2 = 0, Gp1 = "SELECT", Gp2 = "" } },                          // SELECT
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },                           // START
                        { 4,  new() { Kb1 = 38, Kb2 = 0, Gp1 = "DPAD_UP", Gp2 = "" } },                         // UP
                        { 5,  new() { Kb1 = 40, Kb2 = 0, Gp1 = "DPAD_DOWN", Gp2 = "" } },                       // DOWN
                        { 6,  new() { Kb1 = 37, Kb2 = 0, Gp1 = "DPAD_LEFT", Gp2 = "" } },                       // LEFT
                        { 7,  new() { Kb1 = 39, Kb2 = 0, Gp1 = "DPAD_RIGHT", Gp2 = "" } },                      // RIGHT
                        { 10, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_TOP_SHOULDER", Gp2 = "" } },               // L
                        { 11, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },              // R
                        { 12, new() { Kb1 = 9,  Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER", Gp2 = "" } },            // ZL
                        { 13, new() { Kb1 = 82, Kb2 = 0, Gp1 = "RIGHT_BOTTOM_SHOULDER", Gp2 = "" } },           // ZR
                        { 16, new() { Kb1 = 72, Kb2 = 0, Gp1 = "LEFT_STICK_X:+1", Gp2 = "" } },                 // CIRCLE PAD RIGHT
                        { 17, new() { Kb1 = 70, Kb2 = 0, Gp1 = "LEFT_STICK_X:-1", Gp2 = "" } },                 // CIRCLE PAD LEFT
                        { 18, new() { Kb1 = 71, Kb2 = 0, Gp1 = "LEFT_STICK_Y:+1", Gp2 = "" } },                 // CIRCLE PAD DOWN
                        { 19, new() { Kb1 = 84, Kb2 = 0, Gp1 = "LEFT_STICK_Y:-1", Gp2 = "" } },                 // CIRCLE PAD UP
                        { 20, new() { Kb1 = 76, Kb2 = 0, Gp1 = "RIGHT_STICK_X:+1", Gp2 = "" } },                // C-STICK RIGHT
                        { 21, new() { Kb1 = 74, Kb2 = 0, Gp1 = "RIGHT_STICK_X:-1", Gp2 = "" } },                // C-STICK LEFT
                        { 22, new() { Kb1 = 75, Kb2 = 0, Gp1 = "RIGHT_STICK_Y:+1", Gp2 = "" } },                // C-STICK DOWN
                        { 23, new() { Kb1 = 73, Kb2 = 0, Gp1 = "RIGHT_STICK_Y:-1", Gp2 = "" } }                 // C-STICK UP
                    }
                },
                {
                    "dc", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },                        // A
                        { 8,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },                        // B
                        { 1,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },                        // X
                        { 9,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } },                        // Y
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },                           // START
                        { 4,  new() { Kb1 = 38, Kb2 = 0, Gp1 = "DPAD_UP", Gp2 = "" } },
                        { 5,  new() { Kb1 = 40, Kb2 = 0, Gp1 = "DPAD_DOWN", Gp2 = "" } },
                        { 6,  new() { Kb1 = 37, Kb2 = 0, Gp1 = "DPAD_LEFT", Gp2 = "" } },
                        { 7,  new() { Kb1 = 39, Kb2 = 0, Gp1 = "DPAD_RIGHT", Gp2 = "" } },
                        { 12, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER", Gp2 = "LEFT_TOP_SHOULDER" } },   // L
                        { 13, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_BOTTOM_SHOULDER", Gp2 = "RIGHT_TOP_SHOULDER" } }, // R
                        { 16, new() { Kb1 = 72, Kb2 = 0, Gp1 = "LEFT_STICK_X:+1", Gp2 = "" } },
                        { 17, new() { Kb1 = 70, Kb2 = 0, Gp1 = "LEFT_STICK_X:-1", Gp2 = "" } },
                        { 18, new() { Kb1 = 71, Kb2 = 0, Gp1 = "LEFT_STICK_Y:+1", Gp2 = "" } },
                        { 19, new() { Kb1 = 84, Kb2 = 0, Gp1 = "LEFT_STICK_Y:-1", Gp2 = "" } }
                    }
                },
                {
                    "gc", new()
                    {
                        { 0,  new() { Kb1 = 88, Kb2 = 0, Gp1 = "BUTTON_1", Gp2 = "" } },                        // A
                        { 1,  new() { Kb1 = 90, Kb2 = 0, Gp1 = "BUTTON_3", Gp2 = "" } },                        // B
                        { 8,  new() { Kb1 = 65, Kb2 = 0, Gp1 = "BUTTON_2", Gp2 = "" } },                        // X
                        { 9,  new() { Kb1 = 83, Kb2 = 0, Gp1 = "BUTTON_4", Gp2 = "" } },                        // Y
                        { 3,  new() { Kb1 = 13, Kb2 = 0, Gp1 = "START", Gp2 = "" } },                           // START
                        { 11, new() { Kb1 = 82, Kb2 = 0, Gp1 = "RIGHT_TOP_SHOULDER", Gp2 = "" } },              // Z
                        { 12, new() { Kb1 = 81, Kb2 = 0, Gp1 = "LEFT_BOTTOM_SHOULDER", Gp2 = "" } },            // L
                        { 13, new() { Kb1 = 69, Kb2 = 0, Gp1 = "RIGHT_BOTTOM_SHOULDER", Gp2 = "" } },           // R
                        { 16, new() { Kb1 = 72, Kb2 = 0, Gp1 = "LEFT_STICK_X:+1", Gp2 = "" } },
                        { 17, new() { Kb1 = 70, Kb2 = 0, Gp1 = "LEFT_STICK_X:-1", Gp2 = "" } },
                        { 18, new() { Kb1 = 71, Kb2 = 0, Gp1 = "LEFT_STICK_Y:+1", Gp2 = "" } },
                        { 19, new() { Kb1 = 84, Kb2 = 0, Gp1 = "LEFT_STICK_Y:-1", Gp2 = "" } },
                        { 20, new() { Kb1 = 76, Kb2 = 0, Gp1 = "RIGHT_STICK_X:+1", Gp2 = "" } },
                        { 21, new() { Kb1 = 74, Kb2 = 0, Gp1 = "RIGHT_STICK_X:-1", Gp2 = "" } },
                        { 22, new() { Kb1 = 75, Kb2 = 0, Gp1 = "RIGHT_STICK_Y:+1", Gp2 = "" } },
                        { 23, new() { Kb1 = 73, Kb2 = 0, Gp1 = "RIGHT_STICK_Y:-1", Gp2 = "" } }
                    }
                },
                {
                    "pico8", new()
                    {
                        { 0, new() { Kb1 = 90, Kb2 = 67, Gp1 = "BUTTON_1", Gp2 = "" } }, // O (Z / C)
                        { 8, new() { Kb1 = 88, Kb2 = 86, Gp1 = "BUTTON_2", Gp2 = "" } }, // X (X / V)
                        { 3, new() { Kb1 = 13, Kb2 = 80, Gp1 = "START", Gp2 = "" } }     // PAUSE
                    }
                },
                {
                    "arcade", new()
                    {
                        { 2, new() { Kb1 = 86, Kb2 = 53, Gp1 = "SELECT", Gp2 = "" } }, // INSERT COIN: V or 5 / Select
                        { 3, new() { Kb1 = 13, Kb2 = 0,  Gp1 = "START",  Gp2 = "" } }  // START: Enter / Start
                    }
                }
            };

        private static readonly Dictionary<string, (string Name, List<InputButtonDefinition> Buttons, List<int> AnalogAxes)> SchemeDefinitions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "gb",
                    ("Game Boy / Color", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "nes",
                    ("NES / Famicom", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "SWAP DISKS" },
                        new() { Id = 11, Label = "EJECT/INSERT DISK" }
                    }, new List<int>())
                },
                {
                    "snes",
                    ("Super Nintendo (SNES)", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 1, Label = "Y" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" }
                    }, new List<int>())
                },
                {
                    "n64",
                    ("Nintendo 64", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "A" },
                        new() { Id = 1, Label = "B" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "D-PAD UP" },
                        new() { Id = 5, Label = "D-PAD DOWN" },
                        new() { Id = 6, Label = "D-PAD LEFT" },
                        new() { Id = 7, Label = "D-PAD RIGHT" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 12, Label = "Z" },
                        new() { Id = 19, Label = "STICK UP" },
                        new() { Id = 18, Label = "STICK DOWN" },
                        new() { Id = 17, Label = "STICK LEFT" },
                        new() { Id = 16, Label = "STICK RIGHT" },
                        new() { Id = 23, Label = "C-PAD UP" },
                        new() { Id = 22, Label = "C-PAD DOWN" },
                        new() { Id = 21, Label = "C-PAD LEFT" },
                        new() { Id = 20, Label = "C-PAD RIGHT" }
                    }, new List<int> { 16, 17, 18, 19 })
                },
                {
                    "gba",
                    ("Game Boy Advance", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "nds",
                    ("Nintendo DS", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 1, Label = "Y" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 14, Label = "MICROPHONE" }
                    }, new List<int>())
                },
                {
                    "vb",
                    ("Virtual Boy", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "LEFT D-PAD UP" },
                        new() { Id = 5, Label = "LEFT D-PAD DOWN" },
                        new() { Id = 6, Label = "LEFT D-PAD LEFT" },
                        new() { Id = 7, Label = "LEFT D-PAD RIGHT" },
                        new() { Id = 19, Label = "RIGHT D-PAD UP" },
                        new() { Id = 18, Label = "RIGHT D-PAD DOWN" },
                        new() { Id = 17, Label = "RIGHT D-PAD LEFT" },
                        new() { Id = 16, Label = "RIGHT D-PAD RIGHT" }
                    }, new List<int>())
                },
                {
                    "segaMD",
                    ("Sega Genesis / Mega Drive", new List<InputButtonDefinition>
                    {
                        new() { Id = 1, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 8, Label = "C" },
                        new() { Id = 10, Label = "X" },
                        new() { Id = 9, Label = "Y" },
                        new() { Id = 11, Label = "Z" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 2, Label = "MODE" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "segaMS",
                    ("Sega Master System", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "BUTTON 1 / START" },
                        new() { Id = 8, Label = "BUTTON 2" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "segaGG",
                    ("Sega Game Gear", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "BUTTON 1" },
                        new() { Id = 8, Label = "BUTTON 2" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "segaSaturn",
                    ("Sega Saturn", new List<InputButtonDefinition>
                    {
                        new() { Id = 1, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 8, Label = "C" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 10, Label = "Y" },
                        new() { Id = 11, Label = "Z" },
                        new() { Id = 12, Label = "L" },
                        new() { Id = 13, Label = "R" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "3do",
                    ("3DO Interactive Multiplayer", new List<InputButtonDefinition>
                    {
                        new() { Id = 1, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 8, Label = "C" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 2, Label = "X" },
                        new() { Id = 3, Label = "P" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "atari2600",
                    ("Atari 2600", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "FIRE" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "RESET" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "LEFT DIFFICULTY A" },
                        new() { Id = 12, Label = "LEFT DIFFICULTY B" },
                        new() { Id = 11, Label = "RIGHT DIFFICULTY A" },
                        new() { Id = 13, Label = "RIGHT DIFFICULTY B" },
                        new() { Id = 14, Label = "COLOR" },
                        new() { Id = 15, Label = "B/W" }
                    }, new List<int>())
                },
                {
                    "atari5200",
                    ("Atari 5200", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "FIRE (BOTTOM)" },
                        new() { Id = 8, Label = "FIRE 2 (TOP)" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 2, Label = "PAUSE" },
                        new() { Id = 9, Label = "RESET" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 19, Label = "STICK UP" },
                        new() { Id = 18, Label = "STICK DOWN" },
                        new() { Id = 17, Label = "STICK LEFT" },
                        new() { Id = 16, Label = "STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19 })
                },
                {
                    "atari7800",
                    ("Atari 7800", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "BUTTON 1" },
                        new() { Id = 8, Label = "BUTTON 2" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "PAUSE" },
                        new() { Id = 9, Label = "RESET" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "LEFT DIFFICULTY" },
                        new() { Id = 11, Label = "RIGHT DIFFICULTY" }
                    }, new List<int>())
                },
                {
                    "lynx",
                    ("Atari Lynx", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 10, Label = "OPTION 1" },
                        new() { Id = 11, Label = "OPTION 2" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "jaguar",
                    ("Atari Jaguar", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 1, Label = "C" },
                        new() { Id = 2, Label = "PAUSE" },
                        new() { Id = 3, Label = "OPTION" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "pce",
                    ("TurboGrafx-16 / PC Engine", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "I" },
                        new() { Id = 0, Label = "II" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "RUN" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "ngp",
                    ("Neo Geo Pocket", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "A" },
                        new() { Id = 8, Label = "B" },
                        new() { Id = 3, Label = "OPTION" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "ws",
                    ("WonderSwan", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "X UP" },
                        new() { Id = 5, Label = "X DOWN" },
                        new() { Id = 6, Label = "X LEFT" },
                        new() { Id = 7, Label = "X RIGHT" },
                        new() { Id = 13, Label = "Y UP" },
                        new() { Id = 12, Label = "Y DOWN" },
                        new() { Id = 10, Label = "Y LEFT" },
                        new() { Id = 11, Label = "Y RIGHT" }
                    }, new List<int>())
                },
                {
                    "coleco",
                    ("ColecoVision", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "LEFT BUTTON" },
                        new() { Id = 0, Label = "RIGHT BUTTON" },
                        new() { Id = 9, Label = "1" },
                        new() { Id = 1, Label = "2" },
                        new() { Id = 11, Label = "3" },
                        new() { Id = 10, Label = "4" },
                        new() { Id = 13, Label = "5" },
                        new() { Id = 12, Label = "6" },
                        new() { Id = 15, Label = "7" },
                        new() { Id = 14, Label = "8" },
                        new() { Id = 2, Label = "*" },
                        new() { Id = 3, Label = "#" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "pcfx",
                    ("PC-FX", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "I" },
                        new() { Id = 0, Label = "II" },
                        new() { Id = 9, Label = "III" },
                        new() { Id = 1, Label = "IV" },
                        new() { Id = 10, Label = "V" },
                        new() { Id = 11, Label = "VI" },
                        new() { Id = 3, Label = "RUN" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 12, Label = "MODE1" },
                        new() { Id = 13, Label = "MODE2" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "psp",
                    ("PlayStation Portable (PSP)", new List<InputButtonDefinition>
                    {
                        new() { Id = 9, Label = "△ TRIANGLE" },
                        new() { Id = 1, Label = "□ SQUARE" },
                        new() { Id = 0, Label = "⨯ CROSS" },
                        new() { Id = 8, Label = "○ CIRCLE" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 19, Label = "STICK UP" },
                        new() { Id = 18, Label = "STICK DOWN" },
                        new() { Id = 17, Label = "STICK LEFT" },
                        new() { Id = 16, Label = "STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19 })
                },
                {
                    "psx",
                    ("PlayStation", new List<InputButtonDefinition>
                    {
                        new() { Id = 9, Label = "△ TRIANGLE" },
                        new() { Id = 1, Label = "□ SQUARE" },
                        new() { Id = 0, Label = "⨯ CROSS" },
                        new() { Id = 8, Label = "○ CIRCLE" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L1" },
                        new() { Id = 11, Label = "R1" },
                        new() { Id = 12, Label = "L2" },
                        new() { Id = 13, Label = "R2" },
                        new() { Id = 14, Label = "L3" },
                        new() { Id = 15, Label = "R3" },
                        new() { Id = 19, Label = "L STICK UP" },
                        new() { Id = 18, Label = "L STICK DOWN" },
                        new() { Id = 17, Label = "L STICK LEFT" },
                        new() { Id = 16, Label = "L STICK RIGHT" },
                        new() { Id = 23, Label = "R STICK UP" },
                        new() { Id = 22, Label = "R STICK DOWN" },
                        new() { Id = 21, Label = "R STICK LEFT" },
                        new() { Id = 20, Label = "R STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19, 20, 21, 22, 23 })
                },
                {
                    "ps2",
                    ("PlayStation 2", new List<InputButtonDefinition>
                    {
                        new() { Id = 9, Label = "△ TRIANGLE" },
                        new() { Id = 1, Label = "□ SQUARE" },
                        new() { Id = 0, Label = "⨯ CROSS" },
                        new() { Id = 8, Label = "○ CIRCLE" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L1" },
                        new() { Id = 11, Label = "R1" },
                        new() { Id = 12, Label = "L2" },
                        new() { Id = 13, Label = "R2" },
                        new() { Id = 14, Label = "L3" },
                        new() { Id = 15, Label = "R3" },
                        new() { Id = 19, Label = "L STICK UP" },
                        new() { Id = 18, Label = "L STICK DOWN" },
                        new() { Id = 17, Label = "L STICK LEFT" },
                        new() { Id = 16, Label = "L STICK RIGHT" },
                        new() { Id = 23, Label = "R STICK UP" },
                        new() { Id = 22, Label = "R STICK DOWN" },
                        new() { Id = 21, Label = "R STICK LEFT" },
                        new() { Id = 20, Label = "R STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19, 20, 21, 22, 23 })
                },
                {
                    "arcade",
                    ("Arcade", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "B" },
                        new() { Id = 1, Label = "Y" },
                        new() { Id = 2, Label = "INSERT COIN" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 8, Label = "A" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 12, Label = "L2" },
                        new() { Id = 13, Label = "R2" },
                        new() { Id = 14, Label = "L3" },
                        new() { Id = 15, Label = "R3" },
                        new() { Id = 19, Label = "L STICK UP" },
                        new() { Id = 18, Label = "L STICK DOWN" },
                        new() { Id = 17, Label = "L STICK LEFT" },
                        new() { Id = 16, Label = "L STICK RIGHT" },
                        new() { Id = 23, Label = "R STICK UP" },
                        new() { Id = 22, Label = "R STICK DOWN" },
                        new() { Id = 21, Label = "R STICK LEFT" },
                        new() { Id = 20, Label = "R STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19, 20, 21, 22, 23 })
                },
                {
                    "3ds",
                    ("Nintendo 3DS", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A" },
                        new() { Id = 0, Label = "B" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 1, Label = "Y" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 12, Label = "ZL" },
                        new() { Id = 13, Label = "ZR" },
                        new() { Id = 19, Label = "CIRCLE PAD UP" },
                        new() { Id = 18, Label = "CIRCLE PAD DOWN" },
                        new() { Id = 17, Label = "CIRCLE PAD LEFT" },
                        new() { Id = 16, Label = "CIRCLE PAD RIGHT" },
                        new() { Id = 23, Label = "C-STICK UP" },
                        new() { Id = 22, Label = "C-STICK DOWN" },
                        new() { Id = 21, Label = "C-STICK LEFT" },
                        new() { Id = 20, Label = "C-STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19, 20, 21, 22, 23 })
                },
                {
                    "dc",
                    ("Sega Dreamcast", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "A" },
                        new() { Id = 8, Label = "B" },
                        new() { Id = 1, Label = "X" },
                        new() { Id = 9, Label = "Y" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 12, Label = "L TRIGGER" },
                        new() { Id = 13, Label = "R TRIGGER" },
                        new() { Id = 19, Label = "ANALOG UP" },
                        new() { Id = 18, Label = "ANALOG DOWN" },
                        new() { Id = 17, Label = "ANALOG LEFT" },
                        new() { Id = 16, Label = "ANALOG RIGHT" }
                    }, new List<int> { 16, 17, 18, 19 })
                },
                {
                    "gc",
                    ("Nintendo GameCube", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "A" },
                        new() { Id = 1, Label = "B" },
                        new() { Id = 8, Label = "X" },
                        new() { Id = 9, Label = "Y" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 11, Label = "Z" },
                        new() { Id = 12, Label = "L" },
                        new() { Id = 13, Label = "R" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 19, Label = "MAIN STICK UP" },
                        new() { Id = 18, Label = "MAIN STICK DOWN" },
                        new() { Id = 17, Label = "MAIN STICK LEFT" },
                        new() { Id = 16, Label = "MAIN STICK RIGHT" },
                        new() { Id = 23, Label = "C-STICK UP" },
                        new() { Id = 22, Label = "C-STICK DOWN" },
                        new() { Id = 21, Label = "C-STICK LEFT" },
                        new() { Id = 20, Label = "C-STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19, 20, 21, 22, 23 })
                },
                {
                    "pico8",
                    ("PICO-8", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "O" },
                        new() { Id = 8, Label = "X" },
                        new() { Id = 3, Label = "PAUSE" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "dos",
                    ("MS-DOS (Keyboard / Mouse)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "JOYSTICK BTN 1" },
                        new() { Id = 8, Label = "JOYSTICK BTN 2" },
                        new() { Id = 1, Label = "JOYSTICK BTN 3" },
                        new() { Id = 9, Label = "JOYSTICK BTN 4" },
                        new() { Id = 2, Label = "ON-SCREEN KEYBOARD" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 19, Label = "JOYSTICK UP" },
                        new() { Id = 18, Label = "JOYSTICK DOWN" },
                        new() { Id = 17, Label = "JOYSTICK LEFT" },
                        new() { Id = 16, Label = "JOYSTICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19 })
                },
                {
                    "amiga",
                    ("Commodore Amiga (Keyboard / Mouse / Joystick)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "FIRE / RED" },
                        new() { Id = 8, Label = "BLUE" },
                        new() { Id = 1, Label = "YELLOW" },
                        new() { Id = 9, Label = "GREEN" },
                        new() { Id = 2, Label = "VIRTUAL KEYBOARD" },
                        new() { Id = 3, Label = "PLAY / PAUSE" },
                        new() { Id = 4, Label = "JOYSTICK UP" },
                        new() { Id = 5, Label = "JOYSTICK DOWN" },
                        new() { Id = 6, Label = "JOYSTICK LEFT" },
                        new() { Id = 7, Label = "JOYSTICK RIGHT" },
                        new() { Id = 10, Label = "REWIND (L)" },
                        new() { Id = 11, Label = "FORWARD (R)" }
                    }, new List<int>())
                },
                {
                    "c64",
                    ("Commodore 64 / 8-Bit (Keyboard / Joystick)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "FIRE" },
                        new() { Id = 8, Label = "FIRE 2" },
                        new() { Id = 2, Label = "VIRTUAL KEYBOARD" },
                        new() { Id = 3, Label = "STATUS BAR" },
                        new() { Id = 4, Label = "JOYSTICK UP" },
                        new() { Id = 5, Label = "JOYSTICK DOWN" },
                        new() { Id = 6, Label = "JOYSTICK LEFT" },
                        new() { Id = 7, Label = "JOYSTICK RIGHT" }
                    }, new List<int>())
                },
                {
                    "computer",
                    ("Home Computer (Keyboard / Mouse / Joystick)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "FIRE 1" },
                        new() { Id = 8, Label = "FIRE 2" },
                        new() { Id = 2, Label = "VIRTUAL KEYBOARD" },
                        new() { Id = 3, Label = "MENU / START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "mac",
                    ("Macintosh II (Keyboard / Mouse)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "MOUSE CLICK" },
                        new() { Id = 8, Label = "RETURN" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "palmos",
                    ("Palm OS (Stylus / Hardware Buttons)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "DATE BOOK" },
                        new() { Id = 8, Label = "ADDRESS" },
                        new() { Id = 1, Label = "TO DO" },
                        new() { Id = 9, Label = "MEMO PAD" },
                        new() { Id = 2, Label = "FIND / CALC" },
                        new() { Id = 3, Label = "POWER" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "ti83",
                    ("TI-83 Calculator (Keypad)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "2ND" },
                        new() { Id = 8, Label = "ALPHA" },
                        new() { Id = 1, Label = "MODE" },
                        new() { Id = 9, Label = "DEL" },
                        new() { Id = 2, Label = "CLEAR" },
                        new() { Id = 3, Label = "ENTER" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "scummvm",
                    ("ScummVM (Mouse / Keyboard)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "LEFT CLICK" },
                        new() { Id = 8, Label = "RIGHT CLICK" },
                        new() { Id = 2, Label = "VIRTUAL KEYBOARD" },
                        new() { Id = 3, Label = "GAME MENU (F5)" },
                        new() { Id = 4, Label = "CURSOR UP" },
                        new() { Id = 5, Label = "CURSOR DOWN" },
                        new() { Id = 6, Label = "CURSOR LEFT" },
                        new() { Id = 7, Label = "CURSOR RIGHT" }
                    }, new List<int>())
                },
                {
                    "flash",
                    ("Adobe Flash (Keyboard / Mouse)", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "SPACE" },
                        new() { Id = 8, Label = "ENTER" },
                        new() { Id = 1, Label = "Z" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "love",
                    ("LÖVE / ChaiLove / Lutro", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "B" },
                        new() { Id = 8, Label = "A" },
                        new() { Id = 1, Label = "Y" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" }
                    }, new List<int>())
                },
                {
                    "intellivision",
                    ("Intellivision", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "BOTTOM ACTION" },
                        new() { Id = 8, Label = "TOP-LEFT ACTION" },
                        new() { Id = 1, Label = "TOP-RIGHT ACTION" },
                        new() { Id = 2, Label = "CLEAR" },
                        new() { Id = 3, Label = "ENTER" },
                        new() { Id = 4, Label = "DISC UP" },
                        new() { Id = 5, Label = "DISC DOWN" },
                        new() { Id = 6, Label = "DISC LEFT" },
                        new() { Id = 7, Label = "DISC RIGHT" },
                        new() { Id = 10, Label = "KEYPAD 1" },
                        new() { Id = 11, Label = "KEYPAD 2" },
                        new() { Id = 12, Label = "KEYPAD 3" },
                        new() { Id = 13, Label = "KEYPAD 0" }
                    }, new List<int>())
                },
                {
                    "vectrex",
                    ("Vectrex", new List<InputButtonDefinition>
                    {
                        new() { Id = 1, Label = "BUTTON 1" },
                        new() { Id = 0, Label = "BUTTON 2" },
                        new() { Id = 8, Label = "BUTTON 3" },
                        new() { Id = 9, Label = "BUTTON 4" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 19, Label = "STICK UP" },
                        new() { Id = 18, Label = "STICK DOWN" },
                        new() { Id = 17, Label = "STICK LEFT" },
                        new() { Id = 16, Label = "STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19 })
                },
                {
                    "channelf",
                    ("Fairchild Channel F", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "PUSH" },
                        new() { Id = 8, Label = "PULL" },
                        new() { Id = 10, Label = "TWIST LEFT" },
                        new() { Id = 11, Label = "TWIST RIGHT" },
                        new() { Id = 2, Label = "MODE" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "odyssey2",
                    ("Magnavox Odyssey²", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "ACTION" },
                        new() { Id = 2, Label = "VIRTUAL KEYBOARD" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "cdi",
                    ("Philips CD-i", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "BUTTON 1" },
                        new() { Id = 8, Label = "BUTTON 2" },
                        new() { Id = 9, Label = "BUTTON 1+2" },
                        new() { Id = 3, Label = "PAUSE / MENU" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "neocd",
                    ("SNK Neo Geo CD", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "A" },
                        new() { Id = 8, Label = "B" },
                        new() { Id = 1, Label = "C" },
                        new() { Id = 9, Label = "D" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "tamagotchi",
                    ("Tamagotchi P1", new List<InputButtonDefinition>
                    {
                        new() { Id = 1, Label = "A (LEFT)" },
                        new() { Id = 0, Label = "B (MIDDLE)" },
                        new() { Id = 8, Label = "C (RIGHT)" }
                    }, new List<int>())
                },
                {
                    "gw",
                    ("Game & Watch", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A / RIGHT ACTION" },
                        new() { Id = 0, Label = "B / LEFT ACTION" },
                        new() { Id = 2, Label = "GAME B" },
                        new() { Id = 3, Label = "GAME A" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "fantasy2btn",
                    ("2-Button Handheld / Fantasy Console", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "A / X" },
                        new() { Id = 0, Label = "B / Z" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START / PAUSE" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "tic80",
                    ("TIC-80", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "A (Z)" },
                        new() { Id = 8, Label = "B (X)" },
                        new() { Id = 1, Label = "X (A)" },
                        new() { Id = 9, Label = "Y (S)" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" }
                    }, new List<int>())
                },
                {
                    "chip8",
                    ("CHIP-8 (Hex Keypad)", new List<InputButtonDefinition>
                    {
                        new() { Id = 4, Label = "UP (2)" },
                        new() { Id = 5, Label = "DOWN (8)" },
                        new() { Id = 6, Label = "LEFT (4)" },
                        new() { Id = 7, Label = "RIGHT (6)" },
                        new() { Id = 0, Label = "ACTION (5)" },
                        new() { Id = 8, Label = "KEY A" },
                        new() { Id = 1, Label = "KEY B" },
                        new() { Id = 9, Label = "KEY C" },
                        new() { Id = 2, Label = "KEY 1" },
                        new() { Id = 3, Label = "KEY 3" }
                    }, new List<int>())
                },
                {
                    "fps",
                    ("FPS Port (Keyboard / Mouse / RetroPad)", new List<InputButtonDefinition>
                    {
                        new() { Id = 8, Label = "USE / OPEN" },
                        new() { Id = 0, Label = "FIRE" },
                        new() { Id = 1, Label = "RUN / JUMP" },
                        new() { Id = 9, Label = "CROUCH / SWIM" },
                        new() { Id = 10, Label = "STRAFE LEFT / PREV" },
                        new() { Id = 11, Label = "STRAFE RIGHT / NEXT" },
                        new() { Id = 12, Label = "PREV WEAPON" },
                        new() { Id = 13, Label = "NEXT WEAPON" },
                        new() { Id = 2, Label = "MAP" },
                        new() { Id = 3, Label = "MENU" },
                        new() { Id = 4, Label = "FORWARD" },
                        new() { Id = 5, Label = "BACKWARD" },
                        new() { Id = 6, Label = "TURN LEFT" },
                        new() { Id = 7, Label = "TURN RIGHT" },
                        new() { Id = 19, Label = "MOVE UP" },
                        new() { Id = 18, Label = "MOVE DOWN" },
                        new() { Id = 17, Label = "STRAFE LEFT" },
                        new() { Id = 16, Label = "STRAFE RIGHT" },
                        new() { Id = 23, Label = "LOOK UP" },
                        new() { Id = 22, Label = "LOOK DOWN" },
                        new() { Id = 21, Label = "LOOK LEFT" },
                        new() { Id = 20, Label = "LOOK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19, 20, 21, 22, 23 })
                },
                {
                    "media",
                    ("Audio / Media Player", new List<InputButtonDefinition>
                    {
                        new() { Id = 3, Label = "PLAY / PAUSE" },
                        new() { Id = 6, Label = "PREV TRACK" },
                        new() { Id = 7, Label = "NEXT TRACK" }
                    }, new List<int>())
                },
                {
                    "default",
                    ("Standard Controller", new List<InputButtonDefinition>
                    {
                        new() { Id = 0, Label = "B" },
                        new() { Id = 1, Label = "Y" },
                        new() { Id = 2, Label = "SELECT" },
                        new() { Id = 3, Label = "START" },
                        new() { Id = 4, Label = "UP" },
                        new() { Id = 5, Label = "DOWN" },
                        new() { Id = 6, Label = "LEFT" },
                        new() { Id = 7, Label = "RIGHT" },
                        new() { Id = 8, Label = "A" },
                        new() { Id = 9, Label = "X" },
                        new() { Id = 10, Label = "L" },
                        new() { Id = 11, Label = "R" },
                        new() { Id = 12, Label = "L2" },
                        new() { Id = 13, Label = "R2" },
                        new() { Id = 14, Label = "L3" },
                        new() { Id = 15, Label = "R3" },
                        new() { Id = 19, Label = "L STICK UP" },
                        new() { Id = 18, Label = "L STICK DOWN" },
                        new() { Id = 17, Label = "L STICK LEFT" },
                        new() { Id = 16, Label = "L STICK RIGHT" },
                        new() { Id = 23, Label = "R STICK UP" },
                        new() { Id = 22, Label = "R STICK DOWN" },
                        new() { Id = 21, Label = "R STICK LEFT" },
                        new() { Id = 20, Label = "R STICK RIGHT" }
                    }, new List<int> { 16, 17, 18, 19, 20, 21, 22, 23 })
                }
            };

        private static readonly Dictionary<string, string> CoreToScheme = new(StringComparer.OrdinalIgnoreCase)
        {
            { "gambatte", "gb" }, { "sameboy", "gb" },
            { "nestopia", "nes" }, { "fceumm", "nes" },
            { "snes9x", "snes" }, { "bsnes", "snes" }, { "snes9x2010", "snes" }, { "snes9x2005", "snes" },
            { "mupen64plus_next", "n64" }, { "parallel_n64", "n64" }, { "n64", "n64" },
            { "mgba", "gba" }, { "vba_next", "gba" },
            { "melonds", "nds" }, { "desmume", "nds" }, { "desmume2015", "nds" },
            { "beetle_vb", "vb" }, { "mednafen_vb", "vb" },
            { "genesis_plus_gx", "segaMD" }, { "genesis_plus_gx_wide", "segaMD" }, { "picodrive", "segaMD" },
            { "smsplus", "segaMS" },
            { "yabause", "segaSaturn" },
            { "opera", "3do" },
            { "stella2014", "atari2600" },
            { "prosystem", "atari7800" },
            { "handy", "lynx" },
            { "virtualjaguar", "jaguar" },
            { "mednafen_pce", "pce" },
            { "mednafen_ngp", "ngp" },
            { "mednafen_wswan", "ws" },
            { "gearcoleco", "coleco" },
            { "mednafen_pcfx", "pcfx" },
            { "ppsspp", "psp" },
            { "pcsx_rearmed", "psx" }, { "mednafen_psx_hw", "psx" },
            { "play", "ps2" }, { "play!", "ps2" },
            { "fbneo", "arcade" }, { "fbalpha2012_cps1", "arcade" }, { "fbalpha2012_cps2", "arcade" },
            { "same_cdi", "cdi" }, { "mame2003", "arcade" }, { "mame2003_plus", "arcade" },
            { "a5200", "atari5200" },
            { "puae", "amiga" },
            { "vice_x64", "c64" }, { "vice_x64sc", "c64" }, { "vice_x128", "c64" }, { "vice_xpet", "c64" },
            { "vice_xplus4", "c64" }, { "vice_xvic", "c64" }, { "vice_xscpu64", "c64" },
            { "dosbox_pure", "dos" },
            { "freeintv", "intellivision" },
            { "ruffle", "flash" },
            { "love", "love" }, { "chailove", "love" }, { "lutro", "love" },
            { "scummvm", "scummvm" },
            { "vecx", "vectrex" },
            { "freechaf", "channelf" },
            { "o2em", "odyssey2" },
            { "neocd", "neocd" },
            { "quasi88", "computer" }, { "theodore", "computer" }, { "x1", "computer" },
            { "bk", "computer" }, { "galaksija", "computer" }, { "m2000", "computer" },
            { "minivmac", "mac" },
            { "mu", "palmos" },
            { "numero", "ti83" },
            { "tamalibretro", "tamagotchi" },
            { "gw", "gw" },
            { "gam4980", "fantasy2btn" }, { "wasm4", "fantasy2btn" },
            { "ardens", "fantasy2btn" }, { "arduous", "fantasy2btn" },
            { "lowresnx", "fantasy2btn" }, { "uw8", "fantasy2btn" }, { "vaporspec", "fantasy2btn" },
            { "uzem", "snes" },
            { "tic80", "tic80" },
            { "jaxe", "chip8" },
            { "prboom", "fps" }, { "tyrquake", "fps" },
            { "vitaquake2", "fps" }, { "vitaquake2-rogue", "fps" }, { "vitaquake2-xatrix", "fps" }, { "vitaquake2-zaero", "fps" },
            { "ecwolf", "fps" }, { "anarch", "fps" }, { "craft", "fps" },
            { "nxengine", "fantasy2btn" }, { "xrick", "fantasy2btn" }, { "mrboom", "fantasy2btn" },
            { "gong", "fantasy2btn" }, { "2048", "fantasy2btn" },
            { "gme", "media" }, { "pocketcdg", "media" },
            { "azahar", "3ds" }, { "citra", "3ds" },
            { "flycast", "dc" }, { "reicast", "dc" },
            { "dolphin", "gc" },
            { "pico8", "pico8" }
        };

        private static readonly Dictionary<string, string> PlatformToScheme = new(StringComparer.OrdinalIgnoreCase)
        {
            { "GAME BOY", "gb" }, { "GAME BOY COLOR", "gb" }, { "GB", "gb" }, { "GBC", "gb" },
            { "NES", "nes" }, { "FAMICOM", "nes" }, { "FDS", "nes" },
            { "SNES", "snes" }, { "SUPER NINTENDO", "snes" }, { "SUPER FAMICOM", "snes" },
            { "N64", "n64" }, { "NINTENDO 64", "n64" },
            { "GAME BOY ADVANCE", "gba" }, { "GBA", "gba" },
            { "NINTENDO DS", "nds" }, { "NDS", "nds" }, { "DS", "nds" },
            { "VIRTUAL BOY", "vb" }, { "VIRTUALBOY", "vb" }, { "VB", "vb" },
            { "SEGA GENESIS", "segaMD" }, { "GENESIS", "segaMD" }, { "MEGA DRIVE", "segaMD" }, { "MD", "segaMD" },
            { "SEGA CD", "segaMD" }, { "SEGA 32X", "segaMD" }, { "32X", "segaMD" },
            { "MASTER SYSTEM", "segaMS" }, { "SMS", "segaMS" },
            { "GAME GEAR", "segaGG" }, { "GG", "segaGG" },
            { "SEGA SATURN", "segaSaturn" }, { "SATURN", "segaSaturn" },
            { "3DO", "3do" },
            { "ATARI 2600", "atari2600" },
            { "ATARI 5200", "atari5200" },
            { "ATARI 7800", "atari7800" },
            { "ATARI LYNX", "lynx" }, { "LYNX", "lynx" },
            { "ATARI JAGUAR", "jaguar" }, { "JAGUAR", "jaguar" },
            { "TURBOGRAFX-16", "pce" }, { "PC ENGINE", "pce" }, { "PCE", "pce" },
            { "NEOGEO POCKET", "ngp" }, { "NEO GEO POCKET", "ngp" }, { "NGP", "ngp" }, { "NGPC", "ngp" },
            { "WONDERSWAN", "ws" }, { "WS", "ws" },
            { "COLECOVISION", "coleco" }, { "COLECO", "coleco" },
            { "PC-FX", "pcfx" }, { "PCFX", "pcfx" },
            { "PSP", "psp" }, { "PLAYSTATION PORTABLE", "psp" },
            { "PLAYSTATION", "psx" }, { "PSX", "psx" }, { "PS1", "psx" },
            { "PLAYSTATION 2", "ps2" }, { "PLAYSTATION2", "ps2" }, { "PS2", "ps2" },
            { "3DS", "3ds" }, { "NINTENDO 3DS", "3ds" },
            { "DC", "dc" }, { "DREAMCAST", "dc" }, { "SEGA DREAMCAST", "dc" },
            { "GC", "gc" }, { "GAMECUBE", "gc" }, { "NINTENDO GAMECUBE", "gc" },
            { "PICO8", "pico8" }, { "PICO-8", "pico8" },
            { "MASTERSYSTEM", "segaMS" }, { "WSWAN", "ws" },
            { "ARCADE", "arcade" }, { "MAME", "arcade" }, { "MAME 2003", "arcade" },
            { "NEOGEO", "arcade" }, { "NEO GEO", "arcade" }, { "FBNEO", "arcade" }, { "FINALBURN NEO", "arcade" },
            { "CPS1", "arcade" }, { "CPS2", "arcade" }, { "CPS3", "arcade" },
            { "DOS", "dos" }, { "MS-DOS", "dos" }, { "MSDOS", "dos" },
            { "COMMODORE AMIGA", "amiga" }, { "AMIGA", "amiga" },
            { "COMMODORE 64", "c64" }, { "C64", "c64" },
            { "INTELLIVISION", "intellivision" },
            { "FLASH", "flash" }, { "ADOBE FLASH", "flash" },
            { "LOVE", "love" }, { "LÖVE", "love" }, { "CHAILOVE", "love" }, { "LUTRO", "love" },
            { "SCUMMVM", "scummvm" },
            { "VECTREX", "vectrex" },
            { "CHANNEL F", "channelf" }, { "CHANNELF", "channelf" }, { "FAIRCHILD CHANNEL F", "channelf" },
            { "ODYSSEY2", "odyssey2" }, { "ODYSSEY 2", "odyssey2" },
            { "CDI", "cdi" }, { "CD-I", "cdi" }, { "PHILIPS CD-I", "cdi" },
            { "NEOCD", "neocd" }, { "NEO GEO CD", "neocd" },
            { "PC88", "computer" }, { "PC-88", "computer" }, { "X1", "computer" },
            { "THOMSON", "computer" }, { "BK", "computer" }, { "GALAKSIJA", "computer" }, { "P2000", "computer" },
            { "MAC", "mac" }, { "MACINTOSH", "mac" },
            { "PALMOS", "palmos" }, { "PALM OS", "palmos" },
            { "TI83", "ti83" }, { "TI-83", "ti83" },
            { "TAMAGOTCHI", "tamagotchi" },
            { "GW", "gw" }, { "GAME & WATCH", "gw" },
            { "TIC80", "tic80" }, { "TIC-80", "tic80" },
            { "CHIP8", "chip8" }, { "CHIP-8", "chip8" },
            { "WASM4", "fantasy2btn" }, { "ARDUBOY", "fantasy2btn" }, { "LOWRESNX", "fantasy2btn" },
            { "MICROW8", "fantasy2btn" }, { "VAPORSPEC", "fantasy2btn" }, { "GAM4980", "fantasy2btn" },
            { "CAVESTORY", "fantasy2btn" }, { "XRICK", "fantasy2btn" }, { "MRBOOM", "fantasy2btn" },
            { "GONG", "fantasy2btn" }, { "2048", "fantasy2btn" },
            { "UZEBOX", "snes" },
            { "DOOM", "fps" }, { "QUAKE", "fps" }, { "QUAKE2", "fps" }, { "ECWOLF", "fps" }, { "ANARCH", "fps" }, { "CRAFT", "fps" },
            { "GME", "media" }, { "POCKETCDG", "media" }
        };

        public static string ResolveSchemeKey(string? platformOrCoreOrScheme)
        {
            if (string.IsNullOrWhiteSpace(platformOrCoreOrScheme))
                return "default";

            var trimmed = platformOrCoreOrScheme.Trim();

            // Core match
            if (CoreToScheme.TryGetValue(trimmed, out var schemeFromCore))
                return schemeFromCore;

            // Platform match
            if (PlatformToScheme.TryGetValue(trimmed, out var schemeFromPlatform))
                return schemeFromPlatform;

            // Normalize underscores/spaces and recheck
            var normalized = trimmed.Replace('_', ' ');
            if (PlatformToScheme.TryGetValue(normalized, out var schemeFromNorm))
                return schemeFromNorm;

            // Direct canonical scheme key match
            var canonicalKey = SchemeDefinitions.Keys.FirstOrDefault(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase));
            if (canonicalKey != null)
                return canonicalKey;

            return "default";
        }

        private static readonly Dictionary<string, int> SchemeMaxPlayers = new(StringComparer.OrdinalIgnoreCase)
        {
            { "gb", 2 },
            { "gba", 2 },
            { "nds", 2 },
            { "vb", 1 },
            { "psp", 1 },
            { "lynx", 2 },
            { "ngp", 2 },
            { "ws", 2 },
            { "segaGG", 2 },
            { "segaMS", 2 },
            { "atari2600", 2 },
            { "atari7800", 2 },
            { "jaguar", 2 },
            { "coleco", 2 },
            { "pcfx", 2 },
            { "pce", 2 },
            { "nes", 2 },
            { "snes", 2 },
            { "n64", 2 },
            { "psx", 2 },
            { "ps2", 1 },
            { "segaMD", 2 },
            { "segaSaturn", 2 },
            { "3do", 2 },
            { "3ds", 1 },
            { "dc", 4 },
            { "gc", 4 },
            { "pico8", 2 },
            { "arcade", 2 },
            { "default", 2 }
        };

        private static Dictionary<int, InputBindingDefault> GeneratePlayerDefaults(
            int playerIndex,
            List<InputButtonDefinition> allButtons,
            Dictionary<int, InputBindingDefault> p1Defaults)
        {
            var pBinds = new Dictionary<int, InputBindingDefault>();
            foreach (var btn in allButtons)
            {
                var p1 = p1Defaults.TryGetValue(btn.Id, out var b) ? b : null;
                var kb1 = 0;
                if (playerIndex == 1) // Player 2
                {
                    kb1 = btn.Id switch
                    {
                        4 => 87,  // UP -> W
                        5 => 83,  // DOWN -> S
                        6 => 65,  // LEFT -> A
                        7 => 68,  // RIGHT -> D
                        8 => 70,  // A -> F
                        0 => 71,  // B -> G
                        9 => 82,  // X -> R
                        1 => 84,  // Y -> T
                        2 => 89,  // SELECT -> Y
                        3 => 85,  // START -> U
                        10 => 81, // L1 -> Q
                        11 => 69, // R1 -> E
                        12 => 49, // L2 -> 1
                        13 => 50, // R2 -> 2
                        14 => 67, // L3 -> C
                        15 => 86, // R3 -> V
                        _ => 0
                    };
                }
                else if (playerIndex == 2) // Player 3
                {
                    kb1 = btn.Id switch
                    {
                        4 => 104, // UP -> Num 8
                        5 => 98,  // DOWN -> Num 2
                        6 => 100, // LEFT -> Num 4
                        7 => 102, // RIGHT -> Num 6
                        8 => 97,  // A -> Num 1
                        0 => 99,  // B -> Num 3
                        9 => 103, // X -> Num 7
                        1 => 105, // Y -> Num 9
                        2 => 96,  // SELECT -> Num 0
                        3 => 110, // START -> Num .
                        10 => 111, // L1 -> Num /
                        11 => 106, // R1 -> Num *
                        _ => 0
                    };
                }
                else if (playerIndex == 3) // Player 4
                {
                    kb1 = btn.Id switch
                    {
                        4 => 73, // UP -> I
                        5 => 75, // DOWN -> K
                        6 => 74, // LEFT -> J
                        7 => 76, // RIGHT -> L
                        8 => 79, // A -> O
                        0 => 80, // B -> P
                        2 => 78, // SELECT -> N
                        3 => 77, // START -> M
                        _ => 0
                    };
                }

                // If hotkey (id >= 24), no default gamepad/keyboard for secondary players
                if (btn.Id >= 24)
                {
                    pBinds[btn.Id] = new InputBindingDefault
                    {
                        Kb1 = 0,
                        Kb2 = 0,
                        Gp1 = string.Empty,
                        Gp2 = string.Empty
                    };
                }
                else
                {
                    pBinds[btn.Id] = new InputBindingDefault
                    {
                        Kb1 = kb1,
                        Kb2 = 0,
                        Gp1 = p1?.Gp1 ?? string.Empty,
                        Gp2 = p1?.Gp2 ?? string.Empty,
                        Activation = p1?.Activation ?? "press",
                        HoldDuration = p1?.HoldDuration ?? 5
                    };
                }
            }
            return pBinds;
        }

        public PlatformControlScheme GetScheme(string? platformOrCoreOrScheme)
        {
            var key = ResolveSchemeKey(platformOrCoreOrScheme);
            if (!SchemeDefinitions.TryGetValue(key, out var def))
            {
                key = "default";
                def = SchemeDefinitions["default"];
            }

            var isPs2 = string.Equals(key, "ps2", StringComparison.OrdinalIgnoreCase);
            var allButtons = isPs2 ? def.Buttons.ToList() : def.Buttons.Concat(Hotkeys).ToList();
            var overrides = SchemeDefaultOverrides.TryGetValue(key, out var ovr) ? ovr : null;

            var defaultBindings = new Dictionary<int, InputBindingDefault>();
            foreach (var btn in allButtons)
            {
                if (overrides != null && overrides.TryGetValue(btn.Id, out var o))
                {
                    defaultBindings[btn.Id] = new InputBindingDefault
                    {
                        Kb1 = o.Kb1,
                        Kb2 = o.Kb2,
                        Gp1 = o.Gp1,
                        Gp2 = o.Gp2
                    };
                }
                else if (BaseDefaultBindings.TryGetValue(btn.Id, out var b))
                {
                    defaultBindings[btn.Id] = new InputBindingDefault
                    {
                        Kb1 = b.Kb1,
                        Kb2 = b.Kb2,
                        Gp1 = b.Gp1,
                        Gp2 = b.Gp2
                    };
                }
                else
                {
                    defaultBindings[btn.Id] = new InputBindingDefault
                    {
                        Kb1 = 0,
                        Kb2 = 0,
                        Gp1 = string.Empty,
                        Gp2 = string.Empty
                    };
                }
            }

            var maxPlayers = SchemeMaxPlayers.TryGetValue(key, out var mp) ? Math.Min(2, mp) : 2;
            var playerDefaults = new Dictionary<int, Dictionary<int, InputBindingDefault>>
            {
                { 0, defaultBindings }
            };

            for (var p = 1; p < maxPlayers; p++)
            {
                playerDefaults[p] = GeneratePlayerDefaults(p, allButtons, defaultBindings);
            }

            return new PlatformControlScheme
            {
                Id = key,
                Name = def.Name,
                MaxPlayers = maxPlayers,
                Buttons = allButtons,
                DefaultBindings = defaultBindings,
                PlayerDefaultBindings = playerDefaults,
                AnalogAxes = new List<int>(def.AnalogAxes)
            };
        }

        public Dictionary<string, PlatformControlScheme> GetAllSchemes()
        {
            var result = new Dictionary<string, PlatformControlScheme>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in SchemeDefinitions.Keys)
            {
                result[key] = GetScheme(key);
            }
            return result;
        }
    }
}
