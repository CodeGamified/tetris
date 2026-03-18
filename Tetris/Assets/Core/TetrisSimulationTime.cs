// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;

namespace Tetris.Core
{
    /// <summary>
    /// Tetris-specific simulation time — subclasses the engine's abstract SimulationTime.
    /// All time scale management, pause, presets, and input handling are inherited.
    /// Tetris defines: max scale (100x) and time formatting (MM:SS).
    /// </summary>
    public class TetrisSimulationTime : CodeGamified.Time.SimulationTime
    {
        protected override float MaxTimeScale => 100f;

        protected override void OnInitialize()
        {
            timeScalePresets = new float[]
                { 0f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 50f, 100f };
            currentPresetIndex = 3; // Start at 1x
        }

        public override string GetFormattedTime()
        {
            int minutes = (int)(simulationTime / 60.0);
            int seconds = (int)(simulationTime % 60.0);
            return $"{minutes:D2}:{seconds:D2}";
        }
    }
}
