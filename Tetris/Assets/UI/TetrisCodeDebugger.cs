// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using CodeGamified.TUI;
using Tetris.Scripting;

namespace Tetris.UI
{
    /// <summary>
    /// Thin adapter — wires a TetrisProgram into the engine's CodeDebuggerWindow
    /// via TetrisDebuggerData (IDebuggerDataSource).
    /// </summary>
    public class TetrisCodeDebugger : CodeDebuggerWindow
    {
        protected override void Awake()
        {
            base.Awake();
            windowTitle = "CODE";
        }

        public void Bind(TetrisProgram program)
        {
            SetDataSource(new TetrisDebuggerData(program));
        }
    }
}
