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

        // Cached AI placement result (find_best_col computes, find_best_rot reads)
        private int _cachedBestCol;
        private int _cachedBestRot;
        private int _cacheGeneration = -1;

        // Cached 2-ply placement result
        private int _cached2PlyCol;
        private int _cached2PlyRot;
        private int _cache2PlyGeneration = -1;

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
                    state.SetRegister(0, 0f); // deprecated — use get_input_x/y/q, get_action
                    break;
                case TetrisOpCode.GET_INPUT_X:
                    state.SetRegister(0, TetrisInputProvider.Instance?.InputX ?? 0f);
                    break;
                case TetrisOpCode.GET_INPUT_Y:
                    state.SetRegister(0, TetrisInputProvider.Instance?.InputY ?? 0f);
                    break;
                case TetrisOpCode.GET_INPUT_Q:
                    state.SetRegister(0, TetrisInputProvider.Instance?.InputQ ?? 0f);
                    break;
                case TetrisOpCode.GET_ACTION:
                    state.SetRegister(0, TetrisInputProvider.Instance?.Action ?? 0f);
                    break;
                case TetrisOpCode.GET_LOWEST_COL:
                    state.SetRegister(0, _board.LowestColumn());
                    break;
                case TetrisOpCode.GET_BUMPINESS:
                    state.SetRegister(0, _board.Bumpiness());
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

                // ── Axis-driven commands ──
                case TetrisOpCode.MOVE:
                {
                    float dx = state.GetRegister(0);
                    if (dx < 0) state.SetRegister(0, _match.MoveLeft() ? 1f : 0f);
                    else if (dx > 0) state.SetRegister(0, _match.MoveRight() ? 1f : 0f);
                    else state.SetRegister(0, 0f);
                    break;
                }
                case TetrisOpCode.CMD_ROTATE:
                {
                    float dy = state.GetRegister(0);
                    if (dy > 0) state.SetRegister(0, _match.RotateCW() ? 1f : 0f);
                    else if (dy < 0) state.SetRegister(0, _match.RotateCCW() ? 1f : 0f);
                    else state.SetRegister(0, 0f);
                    break;
                }
                case TetrisOpCode.CMD_HOLD:
                {
                    float guard = state.GetRegister(0);
                    state.SetRegister(0, guard != 0f ? (_match.Hold() ? 1f : 0f) : 0f);
                    break;
                }
                case TetrisOpCode.CMD_DROP:
                {
                    float guard = state.GetRegister(0);
                    state.SetRegister(0, guard != 0f ? _match.DoHardDrop() : 0f);
                    break;
                }

                // ── Helper commands (full movement — complete in one call) ──
                case TetrisOpCode.MOVE_TO:
                {
                    int targetCol = (int)state.GetRegister(0);
                    int curCol = _match.ActivePiece?.PivotCol ?? targetCol;
                    while (curCol != targetCol)
                    {
                        bool ok = curCol < targetCol ? _match.MoveRight() : _match.MoveLeft();
                        if (!ok) break;
                        curCol = _match.ActivePiece?.PivotCol ?? curCol;
                    }
                    state.SetRegister(0, curCol == targetCol ? 1f : 0f);
                    break;
                }
                case TetrisOpCode.ORIENT:
                {
                    int targetRot = ((int)state.GetRegister(0)) & 3;
                    int curRot = _match.ActivePiece?.Rotation ?? targetRot;
                    int cwDist = (targetRot - curRot + 4) % 4;
                    bool useCW = cwDist <= 2;
                    int attempts = 0;
                    while (curRot != targetRot && attempts < 4)
                    {
                        bool ok = useCW ? _match.RotateCW() : _match.RotateCCW();
                        if (!ok) break;
                        curRot = _match.ActivePiece?.Rotation ?? curRot;
                        attempts++;
                    }
                    state.SetRegister(0, curRot == targetRot ? 1f : 0f);
                    break;
                }

                // ── AI queries ──
                case TetrisOpCode.FIND_BEST_COL:
                {
                    int shape = _match.ActivePiece?.Shape ?? 0;
                    int gen = _match.PiecesPlaced;
                    if (_cacheGeneration != gen)
                    {
                        var (bestCol, bestRot) = _board.FindBestPlacement(shape);
                        _cachedBestCol = bestCol;
                        _cachedBestRot = bestRot;
                        _cacheGeneration = gen;
                    }
                    state.SetRegister(0, _cachedBestCol);
                    break;
                }
                case TetrisOpCode.FIND_BEST_ROT:
                    state.SetRegister(0, _cachedBestRot);
                    break;
                case TetrisOpCode.FIND_WELL_COL:
                    state.SetRegister(0, _board.FindWellColumn());
                    break;

                // ── 2-ply AI queries ──
                case TetrisOpCode.FIND_BEST_2_COL:
                {
                    int shape2 = _match.ActivePiece?.Shape ?? 0;
                    int nextShape = _match.NextShape;
                    int gen2 = _match.PiecesPlaced;
                    if (_cache2PlyGeneration != gen2)
                    {
                        var (bestCol2, bestRot2) = _board.FindBestPlacement2Ply(shape2, nextShape);
                        _cached2PlyCol = bestCol2;
                        _cached2PlyRot = bestRot2;
                        _cache2PlyGeneration = gen2;
                    }
                    state.SetRegister(0, _cached2PlyCol);
                    break;
                }
                case TetrisOpCode.FIND_BEST_2_ROT:
                    state.SetRegister(0, _cached2PlyRot);
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
