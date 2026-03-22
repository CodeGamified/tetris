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
    /// EXECUTION MODEL (tick-based, deterministic, event-driven):
    ///   - Each simulation tick (~20 ops/sec sim-time), the script runs from the top
    ///   - Memory (variables) persists across ticks
    ///   - PC resets to 0 each tick — no while loop needed
    ///   - EVENT HANDLERS fire on game events (piece spawn, lock, lines cleared)
    ///   - Shape-specific handlers: ishape: oshape: tshape: sshape: zshape: jshape: lshape:
    ///   - Generic handlers: spawn: lock: lines:
    ///   - Smarter scripts survive longer by making better placement decisions
    ///   - Results are IDENTICAL at 0.5x, 1x, 100x speed
    ///
    /// BUILTINS — Queries:
    ///   get_piece()          → current shape (0-6: I,O,T,S,Z,J,L)
    ///   get_rotation()       → current rotation (0-3)
    ///   get_piece_row/col()  → piece pivot position
    ///   get_next()           → next piece shape
    ///   get_held()           → held piece (-1 if none)
    ///   get_ghost_row()      → where piece would land
    ///   get_board_cell(r,c)  → cell value at (row,col)
    ///   get_col_height(c)    → column height
    ///   get_lowest_col()     → column with lowest height
    ///   get_holes()          → total holes on board
    ///   get_max_height()     → tallest column
    ///   get_bumpiness()      → sum of adjacent height differences
    ///   find_best_col()      → best column (evaluates all placements)
    ///   find_best_rot()      → best rotation (paired with find_best_col)
    ///   find_best_2_col()    → best column (2-ply: considers next piece)
    ///   find_best_2_rot()    → best rotation (paired with find_best_2_col)
    ///   find_well_col()      → deepest well column
    ///
    /// BUILTINS — Input (continuous axes, like Pong):
    ///   get_input_x()        → horizontal axis (-1/0/+1)
    ///   get_input_y()        → rotation axis (+1=CW, -1=CCW)
    ///   get_input_q()        → hold button (0/1)
    ///   get_action()         → drop button (0/1)
    ///
    /// BUILTINS — Commands:
    ///   move(dx)             → axis-driven move (-1=left, +1=right)
    ///   rotate(dy)           → axis-driven rotate (+1=CW, -1=CCW)
    ///   hold(guard)          → hold if guard is truthy
    ///   drop(guard)          → hard drop if guard is truthy
    ///   move_left/right()    → slide piece one step
    ///   move_to(col)         → move piece to column (full movement)
    ///   orient(rot)          → rotate piece to rotation (full rotation)
    ///   soft_drop()          → drop one row
    ///   hard_drop()          → instant drop
    ///   rotate_cw/ccw()      → rotate piece one step
    ///   hold()               → hold/swap piece
    /// </summary>
    public class TetrisProgram : ProgramBehaviour
    {
        private TetrisMatchManager _match;
        private TetrisBoard _board;
        private TetrisIOHandler _ioHandler;
        private TetrisCompilerExtension _compilerExt;

        // Execution rate — THE core gameplay constraint
        // Each Python line compiles to ~5-11 bytecode ops, so 20 ops/sec ≈ 2-4 lines/sec.
        public const float OPS_PER_SECOND = 20f;
        private float _opAccumulator;

        // Event handler addresses (from compiled metadata)
        private int _spawnPC = -1;
        private int _lockPC = -1;
        private int _linesPC = -1;
        private readonly int[] _shapePCs = new int[Tetrominos.ShapeCount]; // ishape..lshape

        /// <summary>True if the compiled script has any event handlers registered.</summary>
        private bool HasEventHandlers
        {
            get
            {
                if (_spawnPC >= 0 || _lockPC >= 0 || _linesPC >= 0) return true;
                for (int i = 0; i < _shapePCs.Length; i++)
                    if (_shapePCs[i] >= 0) return true;
                return false;
            }
        }

        // Default starter code — simple but playable
        private const string DEFAULT_CODE = @"# 🧱 TETRIS — Write your stacker AI!
# Your script runs at 20 ops/sec (sim-time).
# When it finishes, it restarts from the top.
# Variables persist — use them to track state.
#
# EVENTS — handlers fire on game events:
#   spawn:    → any new piece
#   ishape:   → I-piece (0)   oshape: → O-piece (1)
#   tshape:   → T-piece (2)   sshape: → S-piece (3)
#   zshape:   → Z-piece (4)   jshape: → J-piece (5)
#   lshape:   → L-piece (6)
#   lock:     → piece locked  lines:  → lines cleared
#
# INPUT (continuous axes — like Pong):
#   get_input_x()       → horizontal (-1/0/+1)
#   get_input_y()       → rotation (+1=CW, -1=CCW)
#   get_input_q()       → hold button (0/1)
#   get_action()        → drop button (0/1)
#
# QUERIES:
#   get_piece()         → current shape (0-6)
#   get_rotation()      → rotation (0-3)
#   get_piece_row()     → pivot row
#   get_piece_col()     → pivot col
#   get_next()          → next piece shape
#   get_held()          → held piece (-1=none)
#   get_ghost_row()     → where piece would land
#   get_board_cell(r,c) → cell at (row,col), 0=empty
#   get_col_height(c)   → height of column c
#   get_lowest_col()    → column with lowest height
#   get_holes()         → total holes on board
#   get_max_height()    → tallest column
#   get_bumpiness()     → sum of adjacent height diffs
#   find_best_col()     → best col (evaluates all placements)
#   find_best_rot()     → best rot (paired with find_best_col)
#   find_best_2_col()   → best col (2-ply: considers next piece)
#   find_best_2_rot()   → best rot (paired with find_best_2_col)
#   find_well_col()     → deepest well column
#   get_score()         → current score
#   get_level()         → current level
#   get_lines()         → total lines cleared
#
# COMMANDS (return 1=success, 0=fail):
#   move(dx)            → axis move (-1=left, +1=right)
#   rotate(dy)          → axis rotate (+1=CW, -1=CCW)
#   hold(guard)         → hold if guard is true
#   drop(guard)         → hard drop if guard is true
#   move_left/right()   → slide left/right
#   move_to(col)        → move to col (full movement)
#   orient(rot)         → rotate to rot (full rotation)
#   soft_drop()         → drop one row
#   hard_drop()         → instant drop
#   rotate_cw/ccw()     → rotate one step
#   hold()              → hold/swap piece (no guard)
#
# HOTKEYS: [1] Easy  [2] Medium  [3] Hard  [4] Keyboard  [5] Reset
#
# This starter passes keyboard input through:
move(get_input_x())
rotate(get_input_y())
hold(get_input_q())
drop(get_action())
";

        public const string USER_CONTROLLED_CODE = @"# KEYBOARD CONTROL — Arrow keys / WASD
# Left/Right = move, Up/X = CW, Z = CCW
# C/Shift/Q = hold, Space = drop
move(get_input_x())
rotate(get_input_y())
hold(get_input_q())
drop(get_action())
";

        public const string EASY_AI_CODE = @"# EASY AI — Smart stacking
# Evaluates all placements, drops at the best one.

def drop():
    orient(_rot)
    move_to(_target)
    hard_drop()

spawn:
    _target = find_best_col()
    _rot = find_best_rot()
    drop()
";

        public const string MEDIUM_AI_CODE = @"# MEDIUM AI — Shape-aware heuristics
# Each piece type has hand-tuned placement logic.
# Decisions from column heights — no brute-force search.

def place():
    orient(_rot)
    move_to(_col)
    hard_drop()

ishape:
    # I-piece: vertical in well when board is high
    _col = find_well_col()
    _rot = 1
    if get_max_height() < 4:
        _rot = 0
        _col = get_lowest_col()
    place()

oshape:
    _rot = 0
    _col = get_lowest_col()
    place()

tshape:
    _col = get_lowest_col()
    _rot = 0
    lh = get_col_height(_col - 1)
    rh = get_col_height(_col + 1)
    if lh > rh:
        _rot = 3
    if rh > lh:
        _rot = 1
    place()

sshape:
    _col = get_lowest_col()
    _rot = 0
    h = get_col_height(_col)
    rh = get_col_height(_col + 1)
    if rh > h:
        _rot = 1
    place()

zshape:
    _col = get_lowest_col()
    _rot = 0
    h = get_col_height(_col)
    lh = get_col_height(_col - 1)
    if lh > h:
        _rot = 1
    place()

jshape:
    _col = get_lowest_col()
    _rot = 0
    h = get_col_height(_col)
    rh = get_col_height(_col + 1)
    if rh > h + 1:
        _rot = 1
    place()

lshape:
    _col = get_lowest_col()
    _rot = 0
    h = get_col_height(_col)
    lh = get_col_height(_col - 1)
    if lh > h + 1:
        _rot = 3
    place()
";

        public const string HARD_AI_CODE = @"# HARD AI — Two-piece lookahead + Tetris strategy
# Saves I-pieces for Tetris clears.
# Releases when well is 4+ deep or board is getting high.

def drop():
    orient(_rot)
    move_to(_col)
    hard_drop()

ishape:
    # Save I for later when board is low
    if get_max_height() < 10 and get_held() != 0:
        hold()
        _col = find_best_2_col()
        _rot = find_best_2_rot()
    else:
        _rot = 1
        _col = find_well_col()
    drop()

spawn:
    if get_held() == 0:
        # I is saved — check for Tetris opportunity
        wc = find_well_col()
        wh = get_col_height(wc)
        lh = get_col_height(wc - 1)
        rh = get_col_height(wc + 1)
        if wc == 0:
            lh = 20
        if wc == 9:
            rh = 20
        mn = lh
        if rh < mn:
            mn = rh
        wd = mn - wh
        if wd >= 4 or get_max_height() > 10:
            # Well ready for Tetris or board getting high — release I
            hold()
            _rot = 1
            _col = wc
        else:
            _col = find_best_2_col()
            _rot = find_best_2_rot()
    else:
        _col = find_best_2_col()
        _rot = find_best_2_rot()
    drop()
";

        /// <summary>True when the loaded script is the user-controlled keyboard script.</summary>
        public bool IsUserMode { get; private set; }

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

            // Wire game events to script handlers
            if (_match != null)
            {
                _match.OnPieceSpawned += OnPieceSpawned;
                _match.OnPieceLocked += OnPieceLocked;
                _match.OnLinesCleared += OnLinesClearedEvent;
            }

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
                // If halted (end of script), restart from top
                // But for hook-based scripts, stay halted (idle) until event fires
                if (_executor.State.IsHalted)
                {
                    if (HasEventHandlers)
                        break; // hook-based: stay idle, events will wake us
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
            var program = PythonCompiler.Compile(source, name, _compilerExt);

            // Extract event handler addresses from compiled metadata
            _spawnPC = program.Metadata.TryGetValue("handler:spawn", out var sp) ? (int)sp : -1;
            _lockPC = program.Metadata.TryGetValue("handler:lock", out var lk) ? (int)lk : -1;
            _linesPC = program.Metadata.TryGetValue("handler:lines", out var ln) ? (int)ln : -1;

            string[] shapeNames = { "ishape", "oshape", "tshape", "sshape", "zshape", "jshape", "lshape" };
            for (int i = 0; i < Tetrominos.ShapeCount; i++)
                _shapePCs[i] = program.Metadata.TryGetValue($"handler:{shapeNames[i]}", out var pc) ? (int)pc : -1;

            return program;
        }

        protected override void ProcessEvents()
        {
            if (_executor?.State == null) return;

            // Drain output events
            while (_executor.State.OutputEvents.Count > 0)
                _executor.State.OutputEvents.Dequeue();
        }

        // ═══════════════════════════════════════════════════════════════
        // EVENT HANDLERS — jump script PC on game events
        // ═══════════════════════════════════════════════════════════════

        /// <summary>On piece spawn: jump to shape-specific handler, else spawn: handler.</summary>
        private void OnPieceSpawned()
        {
            if (_executor?.State == null) return;

            int shape = _match.ActivePiece?.Shape ?? -1;
            if (shape >= 0 && shape < Tetrominos.ShapeCount && _shapePCs[shape] >= 0)
            {
                JumpToHandler(_shapePCs[shape]);
                return;
            }
            if (_spawnPC >= 0)
                JumpToHandler(_spawnPC);
        }

        /// <summary>On piece locked: jump to lock: handler if defined.</summary>
        private void OnPieceLocked()
        {
            if (_executor?.State == null || _lockPC < 0) return;
            JumpToHandler(_lockPC);
        }

        /// <summary>On lines cleared: jump to lines: handler if defined.</summary>
        private void OnLinesClearedEvent(int count)
        {
            if (_executor?.State == null || _linesPC < 0) return;
            JumpToHandler(_linesPC);
        }

        /// <summary>
        /// Interrupt current execution and jump PC to a handler address.
        /// Clears call stack so we don't return into stale code.
        /// </summary>
        private void JumpToHandler(int handlerPC)
        {
            var s = _executor.State;
            s.PC = handlerPC;
            s.IsHalted = false;
            s.IsWaiting = false;
            s.Stack.Clear();
        }

        /// <summary>Upload new code (called from TUI editor).</summary>
        public void UploadCode(string newSource)
        {
            _sourceCode = newSource ?? DEFAULT_CODE;
            _opAccumulator = 0;
            IsUserMode = (newSource == USER_CONTROLLED_CODE);
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

        private void OnDestroy()
        {
            if (_match != null)
            {
                _match.OnPieceSpawned -= OnPieceSpawned;
                _match.OnPieceLocked -= OnPieceLocked;
                _match.OnLinesCleared -= OnLinesClearedEvent;
            }
        }
    }
}
