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
    }

    public class PlatformControlScheme
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<InputButtonDefinition> Buttons { get; set; } = new();
        public Dictionary<int, InputBindingDefault> DefaultBindings { get; set; } = new();
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
            new() { Id = 29, Label = "SLOW MOTION", Description = "Toggle slow motion gameplay" }
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
            { 27, new() { Kb1 = 107, Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 28, new() { Kb1 = 32,  Kb2 = 0, Gp1 = "",                      Gp2 = "" } },
            { 29, new() { Kb1 = 109, Kb2 = 0, Gp1 = "",                      Gp2 = "" } }
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
                        { 20, new() { Kb1 = 0,  Kb2 = 0, Gp1 = "RIGHT_STICK_X:+1", Gp2 = "" } },
                        { 21, new() { Kb1 = 83, Kb2 = 0, Gp1 = "RIGHT_STICK_X:-1", Gp2 = "" } },
                        { 22, new() { Kb1 = 65, Kb2 = 0, Gp1 = "RIGHT_STICK_Y:+1", Gp2 = "" } },
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
            { "beetle_vb", "vb" },
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
            { "fbneo", "arcade" }, { "fbalpha2012_cps1", "arcade" }, { "fbalpha2012_cps2", "arcade" },
            { "same_cdi", "arcade" }, { "mame2003", "arcade" }, { "mame2003_plus", "arcade" },
            { "a5200", "default" },
            { "puae", "default" },
            { "vice_x64sc", "default" }, { "vice_x128", "default" }, { "vice_xpet", "default" },
            { "vice_xplus4", "default" }, { "vice_xvic", "default" },
            { "dosbox_pure", "default" },
            { "freeintv", "default" },
            { "azahar", "default" }
        };

        private static readonly Dictionary<string, string> PlatformToScheme = new(StringComparer.OrdinalIgnoreCase)
        {
            { "GAME BOY", "gb" }, { "GAME BOY COLOR", "gb" }, { "GB", "gb" }, { "GBC", "gb" },
            { "NES", "nes" }, { "FAMICOM", "nes" }, { "FDS", "nes" },
            { "SNES", "snes" }, { "SUPER NINTENDO", "snes" }, { "SUPER FAMICOM", "snes" },
            { "N64", "n64" }, { "NINTENDO 64", "n64" },
            { "GAME BOY ADVANCE", "gba" }, { "GBA", "gba" },
            { "NINTENDO DS", "nds" }, { "NDS", "nds" }, { "DS", "nds" },
            { "VIRTUAL BOY", "vb" }, { "VB", "vb" },
            { "SEGA GENESIS", "segaMD" }, { "GENESIS", "segaMD" }, { "MEGA DRIVE", "segaMD" }, { "MD", "segaMD" },
            { "SEGA CD", "segaMD" }, { "SEGA 32X", "segaMD" }, { "32X", "segaMD" },
            { "MASTER SYSTEM", "segaMS" }, { "SMS", "segaMS" },
            { "GAME GEAR", "segaGG" }, { "GG", "segaGG" },
            { "SEGA SATURN", "segaSaturn" }, { "SATURN", "segaSaturn" },
            { "3DO", "3do" },
            { "ATARI 2600", "atari2600" },
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
            { "ARCADE", "arcade" }, { "MAME", "arcade" }, { "MAME 2003", "arcade" }
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

        public PlatformControlScheme GetScheme(string? platformOrCoreOrScheme)
        {
            var key = ResolveSchemeKey(platformOrCoreOrScheme);
            if (!SchemeDefinitions.TryGetValue(key, out var def))
            {
                key = "default";
                def = SchemeDefinitions["default"];
            }

            var allButtons = def.Buttons.Concat(Hotkeys).ToList();
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

            return new PlatformControlScheme
            {
                Id = key,
                Name = def.Name,
                Buttons = allButtons,
                DefaultBindings = defaultBindings,
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
