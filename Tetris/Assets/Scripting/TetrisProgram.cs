// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;
using CodeGamified.Engine;
using CodeGamified.Engine.Compiler;
using CodeGamified.Engine.Runtime;
using CodeGamified.Time;
using Tetris.Game;

namespace Tetris.Scripting
{
    /// <summary>
    /// TetrisProgram — code-controlled Tetris AI.
    /// Subclasses ProgramBehaviour from .engine.
    ///
    /// EXECUTION MODEL (tick-based, deterministic):
    ///   - Each simulation tick (~20 ops/sec sim-time), the script runs from the top
    ///   - Memory (variables) persists across ticks
    ///   - PC resets to 0 each tick — no while loop needed
    ///   - On each tick the script reads board state and issues commands
    ///   - Smarter scripts survive longer by making better placement decisions
    ///   - Results are IDENTICAL at 0.5x, 1x, 100x speed
    ///
    /// BUILTINS:
    ///   get_piece()          → current shape (0-6: I,O,T,S,Z,J,L)
    ///   get_rotation()       → current rotation (0-3)
    ///   get_piece_row/col()  → piece pivot position
    ///   get_next()           → next piece shape
    ///   get_held()           → held piece (-1 if none)
    ///   get_ghost_row()      → where piece would land
    ///   get_board_cell(r,c)  → cell value at (row,col)
    ///   get_col_height(c)    → column height
    ///   get_holes()          → total holes on board
    ///   get_max_height()     → tallest column
    ///   move_left/right()    → slide piece
    ///   soft_drop()          → drop one row
    ///   hard_drop()          → instant drop
    ///   rotate_cw/ccw()      → rotate piece
    ///   hold()               → hold/swap piece
    /// </summary>
    public class TetrisProgram : ProgramBehaviour
    {
        private TetrisMatchManager _match;
        private TetrisBoard _board;
        private TetrisIOHandler _ioHandler;
        private TetrisCompilerExtension _compilerExt;

        // Execution rate — THE core gameplay constraint
        public const float OPS_PER_SECOND = 20f;
        private float _opAccumulator;

        // Default starter code — simple but playable
        private const string DEFAULT_CODE = @"# 🧱 TETRIS — Write your stacker AI!
# Your script runs at 20 ops/sec (sim-time).
# When it finishes, it restarts from the top.
# Variables persist — use them to track state.
#
# BUILTINS — Queries:
#   get_piece()         → current shape (0-6)
#   get_rotation()      → rotation (0-3)
#   get_piece_row()     → pivot row
#   get_piece_col()     → pivot col
#   get_next()          → next piece shape
#   get_held()          → held piece (-1=none)
#   get_ghost_row()     → where piece would land
#   get_board_cell(r,c) → cell at (row,col), 0=empty
#   get_col_height(c)   → height of column c
#   get_holes()         → total holes on board
#   get_max_height()    → tallest column
#   get_input()         → keyboard input code
#   get_score()         → current score
#   get_level()         → current level
#   get_lines()         → total lines cleared
#
# BUILTINS — Commands (return 1=success, 0=fail):
#   move_left()         → slide left
#   move_right()        → slide right
#   soft_drop()         → drop one row
#   hard_drop()         → instant drop
#   rotate_cw()         → rotate clockwise
#   rotate_ccw()        → rotate counter-clockwise
#   hold()              → hold/swap piece
#
# This starter passes keyboard input through:
inp = get_input()
if inp == 1:
    move_left()
if inp == 2:
    move_right()
if inp == 3:
    soft_drop()
if inp == 4:
    hard_drop()
if inp == 5:
    rotate_cw()
if inp == 6:
    rotate_ccw()
if inp == 7:
    hold()
";

        public string CurrentSourceCode => _sourceCode;

        // Persistence callback
        public System.Action OnCodeChanged;

        public void Initialize(TetrisMatchManager match, TetrisBoard board,
                               string initialCode = null, string programName = "TetrisAI")
        {
            _match = match;
            _board = board;
            _compilerExt = new TetrisCompilerExtension();

            _programName = programName;
            _sourceCode = initialCode ?? DEFAULT_CODE;
            _autoRun = true;

            LoadAndRun(_sourceCode);
        }

        /// <summary>
        /// Override Update — drip-feed instructions at OPS_PER_SECOND.
        /// Deterministic: same sim-time = same ops executed regardless of time scale.
        /// </summary>
        protected override void Update()
        {
            if (_executor == null || _program == null || _isPaused) return;
            if (_match == null || !_match.MatchInProgress || _match.GameOver) return;

            float timeScale = SimulationTime.Instance?.timeScale ?? 1f;
            if (SimulationTime.Instance != null && SimulationTime.Instance.isPaused) return;

            float simDelta = UnityEngine.Time.deltaTime * timeScale;
            _opAccumulator += simDelta * OPS_PER_SECOND;

            int opsToRun = (int)_opAccumulator;
            _opAccumulator -= opsToRun;

            for (int i = 0; i < opsToRun; i++)
            {
                if (_executor.State.IsHalted)
                {
                    _executor.State.PC = 0;
                    _executor.State.IsHalted = false;
                }

                _executor.ExecuteOne();
            }

            if (opsToRun > 0)
                ProcessEvents();
        }

        protected override IGameIOHandler CreateIOHandler()
        {
            _ioHandler = new TetrisIOHandler(_match, _board);
            return _ioHandler;
        }

        protected override CompiledProgram CompileSource(string source, string name)
        {
            return PythonCompiler.Compile(source, name, _compilerExt);
        }

        protected override void ProcessEvents()
        {
            if (_executor?.State == null) return;

            // Drain output events
            while (_executor.State.OutputEvents.Count > 0)
                _executor.State.OutputEvents.Dequeue();
        }

        /// <summary>Upload new code (called from TUI editor).</summary>
        public void UploadCode(string newSource)
        {
            _sourceCode = newSource ?? DEFAULT_CODE;
            LoadAndRun(_sourceCode);
            Debug.Log($"[TetrisAI] Uploaded new code ({_program?.Instructions?.Length ?? 0} instructions)");
            OnCodeChanged?.Invoke();
        }

        /// <summary>Reset execution state without recompiling.</summary>
        public void ResetExecution()
        {
            if (_executor?.State == null) return;
            _executor.State.Reset();
            _opAccumulator = 0f;
        }
    }
}
