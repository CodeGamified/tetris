// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;

namespace Tetris.Game
{
    /// <summary>
    /// The active (falling) Tetris piece.
    /// Holds current shape, rotation, and pivot position.
    /// All movement validation goes through TetrisBoard.
    /// </summary>
    public class TetrisPiece
    {
        public int Shape { get; private set; }
        public int Rotation { get; private set; }
        public int PivotRow { get; private set; }
        public int PivotCol { get; private set; }

        private readonly TetrisBoard _board;

        public TetrisPiece(TetrisBoard board)
        {
            _board = board;
        }

        /// <summary>Spawn a new piece at the top of the board. Returns false if blocked (game over).</summary>
        public bool Spawn(int shape)
        {
            Shape = shape;
            Rotation = 0;
            PivotRow = TetrisBoard.Height; // top of visible area (in buffer)
            PivotCol = TetrisBoard.Width / 2;

            if (!_board.CanPlace(Shape, Rotation, PivotRow, PivotCol))
            {
                // Try one row higher
                PivotRow++;
                return _board.CanPlace(Shape, Rotation, PivotRow, PivotCol);
            }
            return true;
        }

        /// <summary>Move piece left. Returns true if successful.</summary>
        public bool MoveLeft()
        {
            if (_board.CanPlace(Shape, Rotation, PivotRow, PivotCol - 1))
            {
                PivotCol--;
                return true;
            }
            return false;
        }

        /// <summary>Move piece right. Returns true if successful.</summary>
        public bool MoveRight()
        {
            if (_board.CanPlace(Shape, Rotation, PivotRow, PivotCol + 1))
            {
                PivotCol++;
                return true;
            }
            return false;
        }

        /// <summary>Move piece down by one row. Returns true if successful.</summary>
        public bool MoveDown()
        {
            if (_board.CanPlace(Shape, Rotation, PivotRow - 1, PivotCol))
            {
                PivotRow--;
                return true;
            }
            return false;
        }

        /// <summary>Rotate piece clockwise with basic wall kick. Returns true if successful.</summary>
        public bool RotateCW()
        {
            int newRot = (Rotation + 1) % 4;
            return TryRotation(newRot);
        }

        /// <summary>Rotate piece counter-clockwise with basic wall kick. Returns true if successful.</summary>
        public bool RotateCCW()
        {
            int newRot = (Rotation + 3) % 4;
            return TryRotation(newRot);
        }

        private bool TryRotation(int newRot)
        {
            // Try in place
            if (_board.CanPlace(Shape, newRot, PivotRow, PivotCol))
            {
                Rotation = newRot;
                return true;
            }

            // Basic wall kicks: try left, right, up
            int[] kicksCol = { -1, 1, -2, 2 };
            int[] kicksRow = { 0, 0, 0, 0, 1, 1, 1 };

            foreach (int dc in kicksCol)
            {
                if (_board.CanPlace(Shape, newRot, PivotRow, PivotCol + dc))
                {
                    PivotCol += dc;
                    Rotation = newRot;
                    return true;
                }
            }

            // Try nudging up (for I-piece flush against bottom)
            if (_board.CanPlace(Shape, newRot, PivotRow + 1, PivotCol))
            {
                PivotRow++;
                Rotation = newRot;
                return true;
            }

            return false;
        }

        /// <summary>Hard drop — instantly move piece to the lowest valid row.</summary>
        public int HardDrop()
        {
            int dropped = 0;
            while (_board.CanPlace(Shape, Rotation, PivotRow - 1, PivotCol))
            {
                PivotRow--;
                dropped++;
            }
            return dropped;
        }

        /// <summary>Calculate where the piece would land (ghost row).</summary>
        public int GhostRow()
        {
            int row = PivotRow;
            while (_board.CanPlace(Shape, Rotation, row - 1, PivotCol))
                row--;
            return row;
        }

        /// <summary>Get the 4 cell positions of the current piece in (row, col) format.</summary>
        public (int row, int col)[] GetCells()
        {
            var offsets = Tetrominos.Rotations[Shape][Rotation];
            var cells = new (int row, int col)[4];
            for (int i = 0; i < 4; i++)
                cells[i] = (PivotRow + offsets[i].row, PivotCol + offsets[i].col);
            return cells;
        }

        /// <summary>Lock the current piece into the board.</summary>
        public bool Lock()
        {
            return _board.LockPiece(Shape, Rotation, PivotRow, PivotCol);
        }
    }
}
