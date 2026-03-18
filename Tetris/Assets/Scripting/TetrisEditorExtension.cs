// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using System.Collections.Generic;
using CodeGamified.Editor;

namespace Tetris.Scripting
{
    /// <summary>
    /// Editor extension for Tetris — provides game-specific options
    /// to CodeEditorWindow's option tree.
    /// Exposes Tetris builtins as available functions for tap-to-code editing.
    /// </summary>
    public class TetrisEditorExtension : IEditorExtension
    {
        public List<EditorTypeInfo> GetAvailableTypes()
        {
            return new List<EditorTypeInfo>(); // No object types in Tetris
        }

        public List<EditorFuncInfo> GetAvailableFunctions()
        {
            return new List<EditorFuncInfo>
            {
                // Queries
                new EditorFuncInfo { Name = "get_piece",        Hint = "current shape (0-6)",       ArgCount = 0 },
                new EditorFuncInfo { Name = "get_rotation",     Hint = "current rotation (0-3)",    ArgCount = 0 },
                new EditorFuncInfo { Name = "get_piece_row",    Hint = "piece pivot row",           ArgCount = 0 },
                new EditorFuncInfo { Name = "get_piece_col",    Hint = "piece pivot col",           ArgCount = 0 },
                new EditorFuncInfo { Name = "get_next",         Hint = "next piece shape",          ArgCount = 0 },
                new EditorFuncInfo { Name = "get_held",         Hint = "held piece (-1=none)",      ArgCount = 0 },
                new EditorFuncInfo { Name = "get_ghost_row",    Hint = "ghost landing row",         ArgCount = 0 },
                new EditorFuncInfo { Name = "get_score",        Hint = "current score",             ArgCount = 0 },
                new EditorFuncInfo { Name = "get_level",        Hint = "current level",             ArgCount = 0 },
                new EditorFuncInfo { Name = "get_lines",        Hint = "total lines cleared",       ArgCount = 0 },
                new EditorFuncInfo { Name = "get_holes",        Hint = "total holes on board",      ArgCount = 0 },
                new EditorFuncInfo { Name = "get_max_height",   Hint = "tallest column height",     ArgCount = 0 },
                new EditorFuncInfo { Name = "get_board_width",  Hint = "board width (10)",          ArgCount = 0 },
                new EditorFuncInfo { Name = "get_board_height", Hint = "board height (20)",         ArgCount = 0 },
                new EditorFuncInfo { Name = "get_input",        Hint = "keyboard input code",       ArgCount = 0 },
                new EditorFuncInfo { Name = "get_board_cell",   Hint = "cell at (row, col)",        ArgCount = 2 },
                new EditorFuncInfo { Name = "get_col_height",   Hint = "column height",             ArgCount = 1 },

                // Commands
                new EditorFuncInfo { Name = "move_left",        Hint = "slide piece left",          ArgCount = 0 },
                new EditorFuncInfo { Name = "move_right",       Hint = "slide piece right",         ArgCount = 0 },
                new EditorFuncInfo { Name = "soft_drop",        Hint = "drop down one row",         ArgCount = 0 },
                new EditorFuncInfo { Name = "hard_drop",        Hint = "instant drop + lock",       ArgCount = 0 },
                new EditorFuncInfo { Name = "rotate_cw",        Hint = "rotate clockwise",          ArgCount = 0 },
                new EditorFuncInfo { Name = "rotate_ccw",       Hint = "rotate counter-clockwise",  ArgCount = 0 },
                new EditorFuncInfo { Name = "hold",             Hint = "hold/swap piece",           ArgCount = 0 },
            };
        }

        public List<EditorMethodInfo> GetMethodsForType(string typeName)
        {
            return new List<EditorMethodInfo>(); // No object methods in Tetris
        }

        public List<string> GetVariableNameSuggestions()
        {
            return new List<string>
            {
                "piece", "rotation", "col", "row",
                "target_col", "target_rot", "next",
                "holes", "height", "inp", "best_col"
            };
        }

        public List<string> GetStringLiteralSuggestions()
        {
            return new List<string>(); // No string builtins in Tetris
        }
    }
}
