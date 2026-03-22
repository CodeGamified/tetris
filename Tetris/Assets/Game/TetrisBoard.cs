// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;
using CodeGamified.Time;

namespace Tetris.Game
{
    /// <summary>
    /// The Tetris board — a 10×20 grid (plus hidden rows above).
    /// Manages locked cells, line clears, and collision checks.
    /// Time-scale aware for warp-speed testing.
    /// </summary>
    public class TetrisBoard : MonoBehaviour
    {
        public const int Width = 10;
        public const int Height = 20;
        public const int BufferRows = 4; // hidden rows above the visible field
        public int TotalHeight => Height + BufferRows;

        /// <summary>
        /// Grid of locked cells. 0 = empty, 1-7 = shape index + 1.
        /// Row 0 = bottom of the board. Row TotalHeight-1 = top of buffer.
        /// </summary>
        public int[,] Grid { get; private set; }

        /// <summary>True each frame a line was cleared this tick.</summary>
        public bool LinesCleared { get; private set; }

        /// <summary>Number of lines cleared in the last clear operation.</summary>
        public int LastClearCount { get; private set; }

        // Events
        public System.Action<int> OnLinesCleared;  // count
        public System.Action OnBoardChanged;

        public void Initialize()
        {
            Grid = new int[TotalHeight, Width];
            Clear();
        }

        /// <summary>Clear the entire board.</summary>
        public void Clear()
        {
            System.Array.Clear(Grid, 0, Grid.Length);
            LinesCleared = false;
            LastClearCount = 0;
            OnBoardChanged?.Invoke();
        }

        /// <summary>Check if a shape fits at the given position and rotation.</summary>
        public bool CanPlace(int shape, int rotation, int pivotRow, int pivotCol)
        {
            var cells = Tetrominos.Rotations[shape][rotation];
            for (int i = 0; i < cells.Length; i++)
            {
                int r = pivotRow + cells[i].row;
                int c = pivotCol + cells[i].col;
                if (c < 0 || c >= Width || r < 0) return false;
                if (r >= TotalHeight) return false;
                if (Grid[r, c] != 0) return false;
            }
            return true;
        }

        /// <summary>Lock a piece into the grid. Returns false if any cell is out of visible bounds (game over).</summary>
        public bool LockPiece(int shape, int rotation, int pivotRow, int pivotCol)
        {
            var cells = Tetrominos.Rotations[shape][rotation];
            bool aboveVisible = false;
            for (int i = 0; i < cells.Length; i++)
            {
                int r = pivotRow + cells[i].row;
                int c = pivotCol + cells[i].col;
                if (r < 0 || r >= TotalHeight || c < 0 || c >= Width)
                    continue;
                Grid[r, c] = shape + 1;
                if (r >= Height)
                    aboveVisible = true;
            }
            OnBoardChanged?.Invoke();
            return !aboveVisible;
        }

        /// <summary>Get indices of all full rows (not yet cleared). Bottom-up order.</summary>
        public int[] GetFullRows()
        {
            var rows = new System.Collections.Generic.List<int>();
            for (int row = 0; row < TotalHeight; row++)
                if (IsRowFull(row))
                    rows.Add(row);
            return rows.ToArray();
        }

        /// <summary>Clear completed lines. Returns number of lines cleared.</summary>
        public int ClearLines()
        {
            int cleared = 0;
            for (int row = 0; row < TotalHeight; row++)
            {
                if (IsRowFull(row))
                {
                    RemoveRow(row);
                    cleared++;
                    row--; // re-check same row index since rows shifted down
                }
            }

            LinesCleared = cleared > 0;
            LastClearCount = cleared;

            if (cleared > 0)
                OnLinesCleared?.Invoke(cleared);

            return cleared;
        }

        private bool IsRowFull(int row)
        {
            for (int col = 0; col < Width; col++)
                if (Grid[row, col] == 0) return false;
            return true;
        }

        private void RemoveRow(int row)
        {
            // Shift all rows above down by one
            for (int r = row; r < TotalHeight - 1; r++)
                for (int c = 0; c < Width; c++)
                    Grid[r, c] = Grid[r + 1, c];

            // Clear top row
            for (int c = 0; c < Width; c++)
                Grid[TotalHeight - 1, c] = 0;
        }

        /// <summary>Get the height of the highest occupied cell in a column (0-based from bottom).</summary>
        public int ColumnHeight(int col)
        {
            for (int r = TotalHeight - 1; r >= 0; r--)
                if (Grid[r, col] != 0) return r + 1;
            return 0;
        }

        /// <summary>Count the total number of holes (empty cells below a filled cell) on the board.</summary>
        public int CountHoles()
        {
            int holes = 0;
            for (int col = 0; col < Width; col++)
            {
                bool foundFilled = false;
                for (int row = TotalHeight - 1; row >= 0; row--)
                {
                    if (Grid[row, col] != 0)
                        foundFilled = true;
                    else if (foundFilled)
                        holes++;
                }
            }
            return holes;
        }

        /// <summary>Get the maximum column height on the board.</summary>
        public int MaxHeight()
        {
            int max = 0;
            for (int col = 0; col < Width; col++)
            {
                int h = ColumnHeight(col);
                if (h > max) max = h;
            }
            return max;
        }

        /// <summary>Get the column index with the lowest height. Ties go to the leftmost column.</summary>
        public int LowestColumn()
        {
            int minH = int.MaxValue;
            int minCol = 0;
            for (int c = 0; c < Width; c++)
            {
                int h = ColumnHeight(c);
                if (h < minH) { minH = h; minCol = c; }
            }
            return minCol;
        }

        /// <summary>Sum of absolute height differences between adjacent columns (flatness measure).</summary>
        public int Bumpiness()
        {
            int bump = 0;
            int prevH = ColumnHeight(0);
            for (int c = 1; c < Width; c++)
            {
                int h = ColumnHeight(c);
                int diff = h - prevH;
                bump += diff < 0 ? -diff : diff;
                prevH = h;
            }
            return bump;
        }

        // ═══════════════════════════════════════════════════════════════
        // SIMULATION HELPERS — used by placement search
        // ═══════════════════════════════════════════════════════════════

        private void SimPlacePiece(int shape, int rot, int row, int col)
        {
            var cells = Tetrominos.Rotations[shape][rot];
            for (int i = 0; i < 4; i++)
            {
                int r = row + cells[i].row;
                int c = col + cells[i].col;
                if (r >= 0 && r < TotalHeight && c >= 0 && c < Width)
                    Grid[r, c] = shape + 1;
            }
        }

        private int SimClearLines()
        {
            int cleared = 0;
            for (int r = 0; r < TotalHeight; r++)
            {
                bool full = true;
                for (int c = 0; c < Width; c++)
                    if (Grid[r, c] == 0) { full = false; break; }
                if (full)
                {
                    for (int rr = r; rr < TotalHeight - 1; rr++)
                        for (int c = 0; c < Width; c++)
                            Grid[rr, c] = Grid[rr + 1, c];
                    for (int c = 0; c < Width; c++)
                        Grid[TotalHeight - 1, c] = 0;
                    cleared++;
                    r--;
                }
            }
            return cleared;
        }

        private int SimDropRow(int shape, int rot, int col)
        {
            int row = TotalHeight - 1;
            while (row > 0 && CanPlace(shape, rot, row - 1, col))
                row--;
            return row;
        }

        private float EvaluateBoard(int linesCleared)
        {
            int aggHeight = 0, holes = 0, bumpiness = 0, prevH = 0;
            for (int c = 0; c < Width; c++)
            {
                int h = ColumnHeight(c);
                aggHeight += h;
                if (c > 0)
                {
                    int diff = h - prevH;
                    bumpiness += diff < 0 ? -diff : diff;
                }
                prevH = h;
                bool found = false;
                for (int r = TotalHeight - 1; r >= 0; r--)
                {
                    if (Grid[r, c] != 0) found = true;
                    else if (found) holes++;
                }
            }
            return -0.51f * aggHeight + 0.76f * linesCleared
                   - 0.36f * holes - 0.18f * bumpiness;
        }

        // ═══════════════════════════════════════════════════════════════
        // AI — PLACEMENT SEARCH
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 1-ply: find the best (col, rot) placement for the given shape.
        /// Simulates every (rotation, column) combo, scores the resulting board.
        /// </summary>
        public (int col, int rot) FindBestPlacement(int shape)
        {
            int[,] backup = (int[,])Grid.Clone();
            int bestCol = Width / 2;
            int bestRot = 0;
            float bestScore = float.MinValue;

            for (int rot = 0; rot < 4; rot++)
            {
                for (int col = -2; col < Width + 2; col++)
                {
                    int row = SimDropRow(shape, rot, col);
                    if (!CanPlace(shape, rot, row, col)) continue;

                    SimPlacePiece(shape, rot, row, col);
                    int linesCleared = SimClearLines();
                    float score = EvaluateBoard(linesCleared);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCol = col;
                        bestRot = rot;
                    }

                    System.Array.Copy(backup, Grid, backup.Length);
                }
            }
            return (bestCol, bestRot);
        }

        /// <summary>
        /// 2-ply: find the best placement for currentShape considering nextShape.
        /// For each placement of the current piece, simulates the best response
        /// for the next piece and picks the combo with the highest board score.
        /// </summary>
        public (int col, int rot) FindBestPlacement2Ply(int currentShape, int nextShape)
        {
            int[,] original = (int[,])Grid.Clone();
            int[,] midState = new int[TotalHeight, Width];
            int bestCol = Width / 2;
            int bestRot = 0;
            float bestScore = float.MinValue;

            for (int rot1 = 0; rot1 < 4; rot1++)
            {
                for (int col1 = -2; col1 < Width + 2; col1++)
                {
                    System.Array.Copy(original, Grid, original.Length);
                    int row1 = SimDropRow(currentShape, rot1, col1);
                    if (!CanPlace(currentShape, rot1, row1, col1)) continue;

                    SimPlacePiece(currentShape, rot1, row1, col1);
                    int lines1 = SimClearLines();

                    // Save state after placing piece 1
                    System.Array.Copy(Grid, midState, Grid.Length);

                    // Find best resulting board after also placing next piece
                    float bestPly2 = float.MinValue;
                    for (int rot2 = 0; rot2 < 4; rot2++)
                    {
                        for (int col2 = -2; col2 < Width + 2; col2++)
                        {
                            System.Array.Copy(midState, Grid, midState.Length);
                            int row2 = SimDropRow(nextShape, rot2, col2);
                            if (!CanPlace(nextShape, rot2, row2, col2)) continue;

                            SimPlacePiece(nextShape, rot2, row2, col2);
                            int lines2 = SimClearLines();
                            float score = EvaluateBoard(lines1 + lines2);

                            if (score > bestPly2)
                                bestPly2 = score;
                        }
                    }

                    float totalScore = bestPly2 > float.MinValue
                        ? bestPly2
                        : EvaluateBoard(lines1);
                    if (totalScore > bestScore)
                    {
                        bestScore = totalScore;
                        bestCol = col1;
                        bestRot = rot1;
                    }
                }
            }

            System.Array.Copy(original, Grid, original.Length);
            return (bestCol, bestRot);
        }

        /// <summary>Find the column with the deepest well (lowest relative to neighbors).</summary>
        public int FindWellColumn()
        {
            int bestCol = 0;
            int bestDepth = 0;
            for (int c = 0; c < Width; c++)
            {
                int h = ColumnHeight(c);
                int leftH = c > 0 ? ColumnHeight(c - 1) : Height;
                int rightH = c < Width - 1 ? ColumnHeight(c + 1) : Height;
                int minNeighbor = leftH < rightH ? leftH : rightH;
                int depth = minNeighbor - h;
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    bestCol = c;
                }
            }
            return bestCol;
        }

        /// <summary>Get the value at a grid cell. 0 = empty, 1-7 = shape+1. Out-of-bounds = -1.</summary>
        public int GetCell(int row, int col)
        {
            if (row < 0 || row >= TotalHeight || col < 0 || col >= Width) return -1;
            return Grid[row, col];
        }
    }
}
