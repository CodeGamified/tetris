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

        /// <summary>Get the value at a grid cell. 0 = empty, 1-7 = shape+1. Out-of-bounds = -1.</summary>
        public int GetCell(int row, int col)
        {
            if (row < 0 || row >= TotalHeight || col < 0 || col >= Width) return -1;
            return Grid[row, col];
        }
    }
}
