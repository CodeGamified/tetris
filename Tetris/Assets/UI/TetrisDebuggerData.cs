// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using System.Collections.Generic;
using UnityEngine;
using CodeGamified.Engine;
using CodeGamified.Engine.Runtime;
using CodeGamified.TUI;
using Tetris.Scripting;
using static Tetris.Scripting.TetrisOpCode;

namespace Tetris.UI
{
    /// <summary>
    /// Adapts a TetrisProgram into the engine's IDebuggerDataSource contract.
    /// </summary>
    public class TetrisDebuggerData : IDebuggerDataSource
    {
        private readonly TetrisProgram _program;
        private readonly string _label;

        public TetrisDebuggerData(TetrisProgram program, string label = null)
        {
            _program = program;
            _label = label;
        }

        public string ProgramName => _label ?? _program?.ProgramName ?? "TetrisAI";
        public string[] SourceLines => _program?.Program?.SourceLines;
        public bool HasLiveProgram =>
            _program != null && _program.Executor != null && _program.Program != null
            && _program.Program.Instructions != null && _program.Program.Instructions.Length > 0;
        public int PC
        {
            get
            {
                var s = _program?.State;
                if (s == null) return 0;
                return s.LastExecutedPC >= 0 ? s.LastExecutedPC : s.PC;
            }
        }
        public long CycleCount => _program?.State?.CycleCount ?? 0;

        public string StatusString
        {
            get
            {
                if (_program == null || _program.Executor == null)
                    return TUIColors.Dimmed("NO PROGRAM");
                var state = _program.State;
                if (state == null) return TUIColors.Dimmed("NO STATE");
                int instCount = _program.Program?.Instructions?.Length ?? 0;
                return TUIColors.Fg(TUIColors.BrightGreen, $"TICK {instCount} inst");
            }
        }

        public List<string> BuildSourceLines(int pc, int scrollOffset, int maxRows)
        {
            var lines = new List<string>();
            var src = SourceLines;
            if (src == null) return lines;

            int activeLine = -1;
            int activeEnd = -1;
            bool isHalt = false;
            Instruction activeInst = default;
            if (HasLiveProgram && _program.Program.Instructions.Length > 0
                && pc < _program.Program.Instructions.Length)
            {
                activeInst = _program.Program.Instructions[pc];
                activeLine = activeInst.SourceLine - 1;
                isHalt = activeInst.Op == OpCode.HALT;
                if (activeLine >= 0)
                    activeEnd = SourceHighlight.GetContinuationEnd(src, activeLine);
            }

            if (scrollOffset == 0 && lines.Count < maxRows)
            {
                string whileLine = "while True:";
                if (isHalt)
                    lines.Add(TUIColors.Fg(TUIColors.BrightGreen, $"  {TUIGlyphs.ArrowR}   {whileLine}"));
                else
                    lines.Add($"  {TUIColors.Dimmed(TUIGlyphs.ArrowR)}   {SynthwaveHighlighter.Highlight(whileLine)}");
            }

            int tokenLine = -1;
            if (activeLine >= 0)
            {
                string token = SourceHighlight.GetSourceToken(activeInst);
                if (token != null)
                {
                    for (int k = activeLine; k <= activeEnd; k++)
                    {
                        if (src[k].IndexOf(token) >= 0) { tokenLine = k; break; }
                    }
                }
                if (tokenLine < 0) tokenLine = activeLine;
            }

            for (int i = scrollOffset; i < src.Length && lines.Count < maxRows; i++)
            {
                if (i == tokenLine)
                {
                    lines.Add(SourceHighlight.HighlightActiveLine(
                        src[i], $" {i + 1:D3}      ", activeInst));
                }
                else
                {
                    string num = TUIColors.Dimmed($"{i + 1:D3}");
                    lines.Add($" {num}      {SynthwaveHighlighter.Highlight(src[i])}");
                }
            }
            return lines;
        }

        public List<string> BuildMachineLines(int pc, int maxRows)
        {
            var lines = new List<string>();
            if (!HasLiveProgram) return lines;

            var instructions = _program.Program.Instructions;
            int total = instructions.Length;

            int offset = 0;
            if (total > maxRows)
                offset = Mathf.Clamp(pc - maxRows / 3, 0, total - maxRows);
            int visibleCount = Mathf.Min(maxRows, total);

            for (int j = 0; j < visibleCount; j++)
            {
                int i = offset + j;
                var inst = instructions[i];
                bool isPC = (i == pc);
                string asm = inst.ToAssembly(FormatTetrisOp);
                if (isPC)
                {
                    lines.Add(TUIColors.Fg(TUIColors.BrightGreen, $" {i:X3}  {asm}"));
                }
                else
                {
                    string addr = TUIColors.Dimmed($"{i:X3}");
                    lines.Add($" {addr}  {SynthwaveHighlighter.HighlightAsm(asm)}");
                }
            }
            return lines;
        }

        public List<string> BuildStateLines()
        {
            if (!HasLiveProgram) return new List<string>();
            var s = _program.State;
            int displayPC = s.LastExecutedPC >= 0 ? s.LastExecutedPC : s.PC;
            return TUIWidgets.BuildStateLines(
                s.Registers, s.LastRegisterModified,
                s.Flags, displayPC, s.Stack.Count,
                s.NameToAddress, s.Memory);
        }

        static string FormatTetrisOp(Instruction inst)
        {
            int id = (int)inst.Op - (int)OpCode.CUSTOM_0;
            return (TetrisOpCode)id switch
            {
                GET_PIECE        => "INP R0, PIECE",
                GET_ROTATION     => "INP R0, ROT",
                GET_PIECE_ROW    => "INP R0, P.ROW",
                GET_PIECE_COL    => "INP R0, P.COL",
                GET_NEXT         => "INP R0, NEXT",
                GET_HELD         => "INP R0, HELD",
                GET_SCORE        => "INP R0, SCORE",
                GET_LEVEL        => "INP R0, LEVEL",
                GET_LINES        => "INP R0, LINES",
                GET_BOARD_CELL   => "INP R0, CELL",
                GET_COL_HEIGHT   => "INP R0, COL.H",
                GET_HOLES        => "INP R0, HOLES",
                GET_MAX_HEIGHT   => "INP R0, MAX.H",
                GET_GHOST_ROW    => "INP R0, GHST",
                GET_BOARD_WIDTH  => "INP R0, BRD.W",
                GET_BOARD_HEIGHT => "INP R0, BRD.H",
                GET_DROP_TIMER   => "INP R0, DROP.T",
                GET_INPUT        => "INP R0, INPUT",
                MOVE_LEFT        => "OUT MOV.L",
                MOVE_RIGHT       => "OUT MOV.R",
                SOFT_DROP        => "OUT S.DRP",
                HARD_DROP        => "OUT H.DRP",
                ROTATE_CW        => "OUT ROT.CW",
                ROTATE_CCW       => "OUT ROT.CC",
                HOLD             => "OUT HOLD",
                _                => $"IO.{id,2} {inst.Arg0}, {inst.Arg1}"
            };
        }
    }
}
