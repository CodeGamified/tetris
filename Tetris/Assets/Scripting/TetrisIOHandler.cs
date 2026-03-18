// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using CodeGamified.Engine;
using CodeGamified.Time;
using Tetris.Game;

namespace Tetris.Scripting
{
    /// <summary>
    /// Game I/O handler for Tetris — bridges CUSTOM opcodes to game state.
    /// </summary>
    public class TetrisIOHandler : IGameIOHandler
    {
        private readonly TetrisMatchManager _match;
        private readonly TetrisBoard _board;

        public TetrisIOHandler(TetrisMatchManager match, TetrisBoard board)
        {
            _match = match;
            _board = board;
        }

        public bool PreExecute(Instruction inst, MachineState state)
        {
            return true; // No gating in Tetris
        }

        public void ExecuteIO(Instruction inst, MachineState state)
        {
            int op = (int)inst.Op - (int)OpCode.CUSTOM_0;

            switch ((TetrisOpCode)op)
            {
                // ── Queries → R0 ──
                case TetrisOpCode.GET_PIECE:
                    state.SetRegister(0, _match.ActivePiece?.Shape ?? -1);
                    break;
                case TetrisOpCode.GET_ROTATION:
                    state.SetRegister(0, _match.ActivePiece?.Rotation ?? 0);
                    break;
                case TetrisOpCode.GET_PIECE_ROW:
                    state.SetRegister(0, _match.ActivePiece?.PivotRow ?? 0);
                    break;
                case TetrisOpCode.GET_PIECE_COL:
                    state.SetRegister(0, _match.ActivePiece?.PivotCol ?? 0);
                    break;
                case TetrisOpCode.GET_NEXT:
                    state.SetRegister(0, _match.NextShape);
                    break;
                case TetrisOpCode.GET_HELD:
                    state.SetRegister(0, _match.HeldShape);
                    break;
                case TetrisOpCode.GET_SCORE:
                    state.SetRegister(0, _match.Score);
                    break;
                case TetrisOpCode.GET_LEVEL:
                    state.SetRegister(0, _match.Level);
                    break;
                case TetrisOpCode.GET_LINES:
                    state.SetRegister(0, _match.LinesTotal);
                    break;
                case TetrisOpCode.GET_GHOST_ROW:
                    state.SetRegister(0, _match.ActivePiece?.GhostRow() ?? 0);
                    break;
                case TetrisOpCode.GET_BOARD_WIDTH:
                    state.SetRegister(0, TetrisBoard.Width);
                    break;
                case TetrisOpCode.GET_BOARD_HEIGHT:
                    state.SetRegister(0, TetrisBoard.Height);
                    break;
                case TetrisOpCode.GET_HOLES:
                    state.SetRegister(0, _board.CountHoles());
                    break;
                case TetrisOpCode.GET_MAX_HEIGHT:
                    state.SetRegister(0, _board.MaxHeight());
                    break;
                case TetrisOpCode.GET_DROP_TIMER:
                    state.SetRegister(0, _match.CurrentDropInterval);
                    break;
                case TetrisOpCode.GET_INPUT:
                    state.SetRegister(0, TetrisInputProvider.Instance != null
                        ? TetrisInputProvider.Instance.CurrentInput : 0f);
                    break;

                // ── Queries with args ──
                case TetrisOpCode.GET_BOARD_CELL:
                    int row = (int)state.GetRegister(0);
                    int col = (int)state.GetRegister(1);
                    state.SetRegister(0, _board.GetCell(row, col));
                    break;
                case TetrisOpCode.GET_COL_HEIGHT:
                    int c = (int)state.GetRegister(0);
                    state.SetRegister(0, c >= 0 && c < TetrisBoard.Width ? _board.ColumnHeight(c) : 0);
                    break;

                // ── Commands → R0 = result ──
                case TetrisOpCode.MOVE_LEFT:
                    state.SetRegister(0, _match.MoveLeft() ? 1f : 0f);
                    break;
                case TetrisOpCode.MOVE_RIGHT:
                    state.SetRegister(0, _match.MoveRight() ? 1f : 0f);
                    break;
                case TetrisOpCode.SOFT_DROP:
                    state.SetRegister(0, _match.SoftDrop() ? 1f : 0f);
                    break;
                case TetrisOpCode.HARD_DROP:
                    state.SetRegister(0, _match.DoHardDrop());
                    break;
                case TetrisOpCode.ROTATE_CW:
                    state.SetRegister(0, _match.RotateCW() ? 1f : 0f);
                    break;
                case TetrisOpCode.ROTATE_CCW:
                    state.SetRegister(0, _match.RotateCCW() ? 1f : 0f);
                    break;
                case TetrisOpCode.HOLD:
                    state.SetRegister(0, _match.Hold() ? 1f : 0f);
                    break;
            }
        }

        public float GetTimeScale()
        {
            return SimulationTime.Instance?.timeScale ?? 1f;
        }

        public double GetSimulationTime()
        {
            return SimulationTime.Instance?.simulationTime ?? 0.0;
        }
    }
}
