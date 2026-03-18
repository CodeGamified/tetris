// Copyright CodeGamified 2025-2026
// MIT License — Tetris

namespace Tetris.Game
{
    /// <summary>
    /// Standard Tetromino shapes: I, O, T, S, Z, J, L.
    /// Each shape is a set of 4 rotations, each rotation is 4 (row, col) offsets
    /// relative to the pivot cell.
    /// </summary>
    public static class Tetrominos
    {
        public const int ShapeCount = 7;

        // Shape indices (stable, used in opcodes and serialization)
        public const int I = 0;
        public const int O = 1;
        public const int T = 2;
        public const int S = 3;
        public const int Z = 4;
        public const int J = 5;
        public const int L = 6;

        /// <summary>
        /// Rotation data: [shape][rotation][cell] = (row, col) offset from pivot.
        /// Rotation 0 = spawn orientation. CW: 0→1→2→3→0.
        /// Row increases downward (toward the bottom of the board).
        /// </summary>
        public static readonly (int row, int col)[][][] Rotations = new[]
        {
            // I
            new[]
            {
                new[] { (0, -1), (0, 0), (0, 1), (0, 2) },
                new[] { (-1, 0), (0, 0), (1, 0), (2, 0) },
                new[] { (0, -1), (0, 0), (0, 1), (0, 2) },
                new[] { (-1, 0), (0, 0), (1, 0), (2, 0) },
            },
            // O
            new[]
            {
                new[] { (0, 0), (0, 1), (1, 0), (1, 1) },
                new[] { (0, 0), (0, 1), (1, 0), (1, 1) },
                new[] { (0, 0), (0, 1), (1, 0), (1, 1) },
                new[] { (0, 0), (0, 1), (1, 0), (1, 1) },
            },
            // T
            new[]
            {
                new[] { (0, -1), (0, 0), (0, 1), (1, 0) },
                new[] { (-1, 0), (0, 0), (1, 0), (0, 1) },
                new[] { (-1, 0), (0, -1), (0, 0), (0, 1) },
                new[] { (0, -1), (-1, 0), (0, 0), (1, 0) },
            },
            // S
            new[]
            {
                new[] { (0, 0), (0, 1), (1, -1), (1, 0) },
                new[] { (-1, 0), (0, 0), (0, 1), (1, 1) },
                new[] { (0, 0), (0, 1), (1, -1), (1, 0) },
                new[] { (-1, 0), (0, 0), (0, 1), (1, 1) },
            },
            // Z
            new[]
            {
                new[] { (0, -1), (0, 0), (1, 0), (1, 1) },
                new[] { (-1, 1), (0, 0), (0, 1), (1, 0) },
                new[] { (0, -1), (0, 0), (1, 0), (1, 1) },
                new[] { (-1, 1), (0, 0), (0, 1), (1, 0) },
            },
            // J
            new[]
            {
                new[] { (0, -1), (0, 0), (0, 1), (1, -1) },
                new[] { (-1, 0), (0, 0), (1, 0), (1, 1) },
                new[] { (-1, 1), (0, -1), (0, 0), (0, 1) },
                new[] { (-1, -1), (-1, 0), (0, 0), (1, 0) },
            },
            // L
            new[]
            {
                new[] { (0, -1), (0, 0), (0, 1), (1, 1) },
                new[] { (-1, 0), (0, 0), (1, 0), (-1, 1) },
                new[] { (-1, -1), (0, -1), (0, 0), (0, 1) },
                new[] { (1, -1), (-1, 0), (0, 0), (1, 0) },
            },
        };

        /// <summary>Display names for each shape.</summary>
        public static readonly string[] Names = { "I", "O", "T", "S", "Z", "J", "L" };

        /// <summary>Colors for each shape (classic NES-inspired).</summary>
        public static readonly UnityEngine.Color[] Colors = new[]
        {
            new UnityEngine.Color(0f, 1f, 1f),       // I — cyan
            new UnityEngine.Color(1f, 1f, 0f),       // O — yellow
            new UnityEngine.Color(0.8f, 0f, 1f),     // T — purple
            new UnityEngine.Color(0f, 1f, 0f),       // S — green
            new UnityEngine.Color(1f, 0f, 0f),       // Z — red
            new UnityEngine.Color(0f, 0f, 1f),       // J — blue
            new UnityEngine.Color(1f, 0.5f, 0f),     // L — orange
        };
    }
}
