// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using System.Collections.Generic;
using CodeGamified.Engine;
using CodeGamified.Engine.Compiler;

namespace Tetris.Scripting
{
    /// <summary>
    /// Tetris-specific opcodes mapped to CUSTOM_0..CUSTOM_N.
    /// These are the I/O operations available to player scripts.
    /// </summary>
    public enum TetrisOpCode
    {
        // ── Queries (read game state → R0) ──
        GET_PIECE        = 0,   // current piece shape (0-6)
        GET_ROTATION     = 1,   // current rotation (0-3)
        GET_PIECE_ROW    = 2,   // piece pivot row
        GET_PIECE_COL    = 3,   // piece pivot col
        GET_NEXT         = 4,   // next piece shape (0-6)
        GET_HELD         = 5,   // held piece shape (-1 if none)
        GET_SCORE        = 6,   // current score
        GET_LEVEL        = 7,   // current level
        GET_LINES        = 8,   // total lines cleared
        GET_BOARD_CELL   = 9,   // get cell at (R0=row, R1=col) → R0 (0=empty, 1-7=shape+1)
        GET_COL_HEIGHT   = 10,  // get column height (R0=col) → R0
        GET_HOLES        = 11,  // total hole count → R0
        GET_MAX_HEIGHT   = 12,  // max column height → R0
        GET_GHOST_ROW    = 13,  // ghost piece landing row → R0
        GET_BOARD_WIDTH  = 14,  // board width (10) → R0
        GET_BOARD_HEIGHT = 15,  // board height (20) → R0
        GET_DROP_TIMER   = 16,  // time until next gravity drop → R0
        GET_INPUT        = 17,  // keyboard input state → R0

        // ── Helper queries ──
        GET_LOWEST_COL   = 18,  // column with lowest height → R0
        GET_BUMPINESS    = 19,  // sum of adjacent height differences → R0

        // ── Commands (write to game state) ──
        MOVE_LEFT        = 20,  // move piece left, R0 = 1 if success
        MOVE_RIGHT       = 21,  // move piece right, R0 = 1 if success
        SOFT_DROP        = 22,  // soft drop one row, R0 = 1 if success
        HARD_DROP        = 23,  // hard drop, R0 = rows dropped
        ROTATE_CW        = 24,  // rotate clockwise, R0 = 1 if success
        ROTATE_CCW       = 25,  // rotate counter-clockwise, R0 = 1 if success
        HOLD             = 26,  // hold piece, R0 = 1 if success

        // ── Helper commands ──
        MOVE_TO          = 27,  // move piece to target col (R0=col), R0 = 1 if arrived
        ORIENT           = 28,  // rotate piece to target rot (R0=rot), R0 = 1 if arrived

        // ── AI queries (expensive — evaluate all placements) ──
        FIND_BEST_COL    = 29,  // evaluate all placements → best col in R0 (caches rot)
        FIND_BEST_ROT    = 30,  // return cached best rot from last find_best_col → R0
        FIND_WELL_COL    = 31,  // deepest well column → R0

        // ── 2-ply AI queries (considers next piece) ──
        FIND_BEST_2_COL  = 32,  // 2-ply search → best col for current piece considering next
        FIND_BEST_2_ROT  = 33,  // return cached best rot from last find_best_2_col → R0

        // ── Continuous input queries (Pong-style axes) ──
        GET_INPUT_X      = 34,  // horizontal axis (-1/0/+1) → R0
        GET_INPUT_Y      = 35,  // rotation axis (+1=CW, -1=CCW) → R0
        GET_INPUT_Q      = 36,  // hold button (0/1) → R0
        GET_ACTION       = 37,  // drop/action button (0/1) → R0

        // ── Axis-driven commands (R0 = value from input) ──
        MOVE             = 38,  // R0=dx: <0 → left, >0 → right
        CMD_ROTATE       = 39,  // R0=dy: >0 → CW, <0 → CCW
        CMD_HOLD         = 40,  // R0=guard: if nonzero → hold
        CMD_DROP         = 41,  // R0=guard: if nonzero → hard_drop
    }

    /// <summary>
    /// Compiler extension for Tetris — registers builtins for piece control and board queries.
    /// </summary>
    public class TetrisCompilerExtension : ICompilerExtension
    {
        public void RegisterBuiltins(CompilerContext ctx)
        {
            // Register Tetris event handler names so the parser recognizes them
            ctx.KnownEvents.Add("spawn");
            ctx.KnownEvents.Add("lock");
            ctx.KnownEvents.Add("lines");
            ctx.KnownEvents.Add("ishape");
            ctx.KnownEvents.Add("oshape");
            ctx.KnownEvents.Add("tshape");
            ctx.KnownEvents.Add("sshape");
            ctx.KnownEvents.Add("zshape");
            ctx.KnownEvents.Add("jshape");
            ctx.KnownEvents.Add("lshape");
        }

        public bool TryCompileCall(string functionName, List<AstNodes.ExprNode> args,
                                   CompilerContext ctx, int sourceLine)
        {
            switch (functionName)
            {
                // ── Queries: result in R0 ──
                case "get_piece":
                    EmitQuery(ctx, TetrisOpCode.GET_PIECE, sourceLine, "get_piece → R0");
                    return true;
                case "get_rotation":
                    EmitQuery(ctx, TetrisOpCode.GET_ROTATION, sourceLine, "get_rotation → R0");
                    return true;
                case "get_piece_row":
                    EmitQuery(ctx, TetrisOpCode.GET_PIECE_ROW, sourceLine, "get_piece_row → R0");
                    return true;
                case "get_piece_col":
                    EmitQuery(ctx, TetrisOpCode.GET_PIECE_COL, sourceLine, "get_piece_col → R0");
                    return true;
                case "get_next":
                    EmitQuery(ctx, TetrisOpCode.GET_NEXT, sourceLine, "get_next → R0");
                    return true;
                case "get_held":
                    EmitQuery(ctx, TetrisOpCode.GET_HELD, sourceLine, "get_held → R0");
                    return true;
                case "get_score":
                    EmitQuery(ctx, TetrisOpCode.GET_SCORE, sourceLine, "get_score → R0");
                    return true;
                case "get_level":
                    EmitQuery(ctx, TetrisOpCode.GET_LEVEL, sourceLine, "get_level → R0");
                    return true;
                case "get_lines":
                    EmitQuery(ctx, TetrisOpCode.GET_LINES, sourceLine, "get_lines → R0");
                    return true;
                case "get_ghost_row":
                    EmitQuery(ctx, TetrisOpCode.GET_GHOST_ROW, sourceLine, "get_ghost_row → R0");
                    return true;
                case "get_board_width":
                    EmitQuery(ctx, TetrisOpCode.GET_BOARD_WIDTH, sourceLine, "get_board_width → R0");
                    return true;
                case "get_board_height":
                    EmitQuery(ctx, TetrisOpCode.GET_BOARD_HEIGHT, sourceLine, "get_board_height → R0");
                    return true;
                case "get_holes":
                    EmitQuery(ctx, TetrisOpCode.GET_HOLES, sourceLine, "get_holes → R0");
                    return true;
                case "get_max_height":
                    EmitQuery(ctx, TetrisOpCode.GET_MAX_HEIGHT, sourceLine, "get_max_height → R0");
                    return true;
                case "get_input":
                    EmitQuery(ctx, TetrisOpCode.GET_INPUT, sourceLine, "get_input → R0");
                    return true;
                case "get_input_x":
                    EmitQuery(ctx, TetrisOpCode.GET_INPUT_X, sourceLine, "get_input_x → R0");
                    return true;
                case "get_input_y":
                    EmitQuery(ctx, TetrisOpCode.GET_INPUT_Y, sourceLine, "get_input_y → R0");
                    return true;
                case "get_input_q":
                    EmitQuery(ctx, TetrisOpCode.GET_INPUT_Q, sourceLine, "get_input_q → R0");
                    return true;
                case "get_action":
                    EmitQuery(ctx, TetrisOpCode.GET_ACTION, sourceLine, "get_action → R0");
                    return true;
                case "get_lowest_col":
                    EmitQuery(ctx, TetrisOpCode.GET_LOWEST_COL, sourceLine, "get_lowest_col → R0");
                    return true;
                case "get_bumpiness":
                    EmitQuery(ctx, TetrisOpCode.GET_BUMPINESS, sourceLine, "get_bumpiness → R0");
                    return true;
                case "find_best_col":
                    EmitQuery(ctx, TetrisOpCode.FIND_BEST_COL, sourceLine, "find_best_col → R0");
                    return true;
                case "find_best_rot":
                    EmitQuery(ctx, TetrisOpCode.FIND_BEST_ROT, sourceLine, "find_best_rot → R0");
                    return true;
                case "find_well_col":
                    EmitQuery(ctx, TetrisOpCode.FIND_WELL_COL, sourceLine, "find_well_col → R0");
                    return true;
                case "find_best_2_col":
                    EmitQuery(ctx, TetrisOpCode.FIND_BEST_2_COL, sourceLine, "find_best_2_col → R0 (2-ply)");
                    return true;
                case "find_best_2_rot":
                    EmitQuery(ctx, TetrisOpCode.FIND_BEST_2_ROT, sourceLine, "find_best_2_rot → R0 (2-ply)");
                    return true;

                // ── Queries with args: load args into registers first ──
                case "get_board_cell":
                    if (args != null && args.Count >= 2)
                    {
                        args[0].Compile(ctx); // row → R0
                        ctx.Emit(OpCode.MOV, 1, 0, comment: "row → R1");
                        // R0 now free — but we need R1 to hold row.
                        // Actually: push R0, compile col, pop into R1 pattern
                        ctx.Emit(OpCode.PUSH, 0, comment: "save row");
                        args[1].Compile(ctx); // col → R0
                        ctx.Emit(OpCode.MOV, 1, 0, comment: "col → R1");
                        ctx.Emit(OpCode.POP, 0, comment: "restore row → R0");
                    }
                    ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.GET_BOARD_CELL, 0, 0, 0, sourceLine,
                        "get_board_cell(R0=row, R1=col) → R0");
                    return true;

                case "get_col_height":
                    if (args != null && args.Count > 0)
                        args[0].Compile(ctx); // col → R0
                    ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.GET_COL_HEIGHT, 0, 0, 0, sourceLine,
                        "get_col_height(R0=col) → R0");
                    return true;

                // ── Commands: result in R0 (1=success, 0=fail) ──
                case "move_left":
                    EmitCommand(ctx, TetrisOpCode.MOVE_LEFT, sourceLine, "move_left → R0");
                    return true;
                case "move_right":
                    EmitCommand(ctx, TetrisOpCode.MOVE_RIGHT, sourceLine, "move_right → R0");
                    return true;
                case "soft_drop":
                    EmitCommand(ctx, TetrisOpCode.SOFT_DROP, sourceLine, "soft_drop → R0");
                    return true;
                case "hard_drop":
                    EmitCommand(ctx, TetrisOpCode.HARD_DROP, sourceLine, "hard_drop → R0");
                    return true;
                case "rotate_cw":
                    EmitCommand(ctx, TetrisOpCode.ROTATE_CW, sourceLine, "rotate_cw → R0");
                    return true;
                case "rotate_ccw":
                    EmitCommand(ctx, TetrisOpCode.ROTATE_CCW, sourceLine, "rotate_ccw → R0");
                    return true;
                case "hold":
                    if (args != null && args.Count > 0)
                    {
                        args[0].Compile(ctx); // guard → R0
                        ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.CMD_HOLD, 0, 0, 0, sourceLine,
                            "hold(R0=guard) → R0");
                    }
                    else
                    {
                        EmitCommand(ctx, TetrisOpCode.HOLD, sourceLine, "hold → R0");
                    }
                    return true;

                // ── Helper commands: arg from R0 ──
                case "move_to":
                    if (args != null && args.Count > 0)
                        args[0].Compile(ctx); // target col → R0
                    ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.MOVE_TO, 0, 0, 0, sourceLine,
                        "move_to(R0=col) → R0 (1=arrived)");
                    return true;
                case "orient":
                    if (args != null && args.Count > 0)
                        args[0].Compile(ctx); // target rot → R0
                    ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.ORIENT, 0, 0, 0, sourceLine,
                        "orient(R0=rot) → R0 (1=arrived)");
                    return true;

                // ── Axis-driven commands (for keyboard control) ──
                case "move":
                    if (args != null && args.Count > 0)
                        args[0].Compile(ctx); // dx → R0
                    ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.MOVE, 0, 0, 0, sourceLine,
                        "move(R0=dx) → R0");
                    return true;
                case "rotate":
                    if (args != null && args.Count > 0)
                        args[0].Compile(ctx); // dy → R0
                    ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.CMD_ROTATE, 0, 0, 0, sourceLine,
                        "rotate(R0=dy) → R0");
                    return true;
                case "drop":
                    if (args != null && args.Count > 0)
                        args[0].Compile(ctx); // guard → R0
                    ctx.Emit(OpCode.CUSTOM_0 + (int)TetrisOpCode.CMD_DROP, 0, 0, 0, sourceLine,
                        "drop(R0=guard) → R0");
                    return true;

                default:
                    return false;
            }
        }

        private static void EmitQuery(CompilerContext ctx, TetrisOpCode op, int line, string comment)
        {
            ctx.Emit(OpCode.CUSTOM_0 + (int)op, 0, 0, 0, line, comment);
        }

        private static void EmitCommand(CompilerContext ctx, TetrisOpCode op, int line, string comment)
        {
            ctx.Emit(OpCode.CUSTOM_0 + (int)op, 0, 0, 0, line, comment);
        }

        public bool TryCompileMethodCall(string objectName, string methodName,
                                         List<AstNodes.ExprNode> args,
                                         CompilerContext ctx, int sourceLine)
        {
            return false; // No objects in Tetris
        }

        public bool TryCompileObjectDecl(string typeName, string varName,
                                         List<AstNodes.ExprNode> constructorArgs,
                                         CompilerContext ctx, int sourceLine)
        {
            return false;
        }
    }
}
