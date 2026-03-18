// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CodeGamified.Camera;
using CodeGamified.Procedural;
using CodeGamified.Time;
using CodeGamified.Settings;
using CodeGamified.Quality;
using CodeGamified.Bootstrap;
using Tetris.Game;
using Tetris.Scripting;
using Tetris.UI;

namespace Tetris.Core
{
    /// <summary>
    /// Bootstrap for Tetris — code-controlled block stacker.
    ///
    /// Architecture (same pattern as Pong / SeaRäuber / BitNaughts):
    ///   - Instantiate managers → wire cross-references → configure scene
    ///   - .engine submodule gives us TUI + Code Execution for free
    ///   - Players don't press arrow keys — they WRITE CODE to place pieces
    ///   - "Unit test" your stacking AI by watching it play at 100x speed
    ///
    /// Attach to a GameObject. Press Play → Tetris appears.
    /// </summary>
    public class TetrisBootstrap : GameBootstrap, IQualityResponsive
    {
        protected override string LogTag => "TETRIS";

        // =================================================================
        // INSPECTOR
        // =================================================================

        [Header("Board")]
        [Tooltip("Base drop interval at level 1 (sim-seconds)")]
        public float baseDropInterval = 1.0f;

        [Tooltip("Lock delay after piece lands (sim-seconds)")]
        public float lockDelay = 0.5f;

        [Header("Match")]
        [Tooltip("Auto-restart after game over")]
        public bool autoRestart = true;

        [Tooltip("Delay before restarting (sim-seconds)")]
        public float restartDelay = 3f;

        [Header("Time")]
        [Tooltip("Enable time scale modulation for fast testing")]
        public bool enableTimeScale = true;

        [Header("Scripting")]
        [Tooltip("Enable code execution (.engine)")]
        public bool enableScripting = true;

        [Header("Camera")]
        public bool configureCamera = true;

        // =================================================================
        // RUNTIME REFERENCES
        // =================================================================

        private TetrisBoard _board;
        private TetrisBoardRenderer _boardRenderer;
        private TetrisMatchManager _match;
        private TetrisProgram _playerProgram;

        // Trail
        private TetrisBlockTrail _blockTrail;

        // TUI
        private TetrisTUIManager _tuiManager;

        // Camera
        private CameraAmbientMotion _cameraSway;

        // Post-processing
        private Bloom _bloom;
        private Volume _postProcessVolume;

        // =================================================================
        // UPDATE
        // =================================================================

        private void Update()
        {
            UpdateBloomScale();
        }

        private void UpdateBloomScale()
        {
            if (_bloom == null || !_bloom.active) return;
            var cam = Camera.main;
            if (cam == null) return;
            float dist = Vector3.Distance(cam.transform.position, BoardCenter());
            float defaultDist = 12f;
            float scale = Mathf.Clamp01(defaultDist / Mathf.Max(dist, 0.01f));
            _bloom.intensity.value = Mathf.Lerp(0.5f, 1.0f, scale);
        }

        // =================================================================
        // BOOTSTRAP
        // =================================================================

        private void Start()
        {
            Log("🧱 Tetris Bootstrap starting...");

            SettingsBridge.Load();
            QualityBridge.SetTier((QualityTier)SettingsBridge.QualityLevel);
            QualityBridge.Register(this);
            Log($"Settings loaded (Quality={SettingsBridge.QualityLevel}, Font={SettingsBridge.FontSize}pt)");

            SetupSimulationTime();
            SetupCamera();
            CreateBoard();
            CreateMatchManager();
            CreateBoardRenderer();
            CreateBlockTrail();
            CreateInputProvider();

            if (enableScripting) CreatePlayerProgram();

            CreateTUI();
            WireEvents();
            StartCoroutine(RunBootSequence());
        }

        public void OnQualityChanged(QualityTier tier)
        {
            Log($"Quality changed → {tier}");
        }

        // =================================================================
        // SIMULATION TIME
        // =================================================================

        private void SetupSimulationTime()
        {
            EnsureSimulationTime<TetrisSimulationTime>();
        }

        // =================================================================
        // CAMERA — perspective 3D view of the board
        // =================================================================

        private Vector3 BoardCenter()
        {
            float boardW = TetrisBoard.Width * TetrisBoardRenderer.CellSize;
            float boardH = TetrisBoard.Height * TetrisBoardRenderer.CellSize;
            return new Vector3(boardW * 0.5f, boardH * 0.5f, 0f);
        }

        private void SetupCamera()
        {
            if (!configureCamera) return;

            var cam = EnsureCamera();

            cam.orthographic = false;
            cam.fieldOfView = 60f;
            var center = BoardCenter();
            cam.transform.position = center + new Vector3(0f, 0f, -12f);
            cam.transform.LookAt(center, Vector3.up);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            // Ambient sway
            _cameraSway = cam.gameObject.AddComponent<CameraAmbientMotion>();
            _cameraSway.lookAtTarget = center;

            // Post-processing: bloom
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
                camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            var volumeGO = new GameObject("PostProcessVolume");
            _postProcessVolume = volumeGO.AddComponent<Volume>();
            _postProcessVolume.isGlobal = true;
            _postProcessVolume.priority = 1;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _bloom = profile.Add<Bloom>();
            _bloom.threshold.overrideState = true;
            _bloom.threshold.value = 0.8f;
            _bloom.intensity.overrideState = true;
            _bloom.intensity.value = 1.0f;
            _bloom.scatter.overrideState = true;
            _bloom.scatter.value = 0.5f;
            _bloom.clamp.overrideState = true;
            _bloom.clamp.value = 20f;
            _bloom.highQualityFiltering.overrideState = true;
            _bloom.highQualityFiltering.value = true;
            _postProcessVolume.profile = profile;

            Log("Camera: perspective, FOV=60, 3D view + sway + bloom");
        }

        // =================================================================
        // BOARD
        // =================================================================

        private void CreateBoard()
        {
            var go = new GameObject("Board");
            _board = go.AddComponent<TetrisBoard>();
            _board.Initialize();
            Log($"Created Board ({TetrisBoard.Width}×{TetrisBoard.Height})");
        }

        // =================================================================
        // BOARD RENDERER
        // =================================================================

        private void CreateBoardRenderer()
        {
            var palette = CreatePalette();
            _boardRenderer = _board.gameObject.AddComponent<TetrisBoardRenderer>();
            _boardRenderer.Initialize(_board, _match, palette);
            _boardRenderer.CreatePieceLight();

            // Center the board in the scene
            float boardW = TetrisBoard.Width * TetrisBoardRenderer.CellSize;
            _board.transform.position = new Vector3(-boardW * 0.5f, -TetrisBoard.Height * TetrisBoardRenderer.CellSize * 0.5f, 0f);

            Log("Created BoardRenderer (3D cubes, ghost piece, frame + piece glow)");
        }

        private ColorPalette CreatePalette()
        {
            var colors = new Dictionary<string, Color>
            {
                { "frame",      new Color(0.3f, 0.3f, 0.4f) },
                { "floor",      new Color(0.02f, 0.02f, 0.05f) },
                { "ghost",      new Color(1f, 1f, 1f, 0.25f) },
                { "I",          new Color(0f, 1f, 1f) },        // cyan
                { "O",          new Color(1f, 1f, 0f) },        // yellow
                { "T",          new Color(0.8f, 0f, 1f) },      // purple
                { "S",          new Color(0f, 1f, 0f) },        // green
                { "Z",          new Color(1f, 0f, 0f) },        // red
                { "J",          new Color(0f, 0.4f, 1f) },      // blue
                { "L",          new Color(1f, 0.5f, 0f) },      // orange
            };
            return ColorPalette.CreateRuntime(colors);
        }

        // =================================================================
        // MATCH MANAGER
        // =================================================================

        private void CreateMatchManager()
        {
            var go = new GameObject("MatchManager");
            _match = go.AddComponent<TetrisMatchManager>();
            _match.Initialize(_board, baseDropInterval, lockDelay);
            Log($"Created MatchManager (drop={baseDropInterval}s, lock={lockDelay}s)");
        }

        // =================================================================
        // INPUT PROVIDER
        // =================================================================

        private void CreateInputProvider()
        {
            var go = new GameObject("InputProvider");
            go.AddComponent<TetrisInputProvider>();
            Log("Created TetrisInputProvider (Unity Input System)");
        }

        // =================================================================
        // PLAYER SCRIPTING (.engine powered)
        // =================================================================

        private void CreatePlayerProgram()
        {
            var go = new GameObject("PlayerProgram");
            _playerProgram = go.AddComponent<TetrisProgram>();
            _playerProgram.Initialize(_match, _board);
            Log("Created PlayerProgram (code-controlled Tetris AI)");
        }

        // =================================================================
        // BLOCK TRAIL
        // =================================================================

        private void CreateBlockTrail()
        {
            var go = new GameObject("BlockTrail");
            _blockTrail = go.AddComponent<TetrisBlockTrail>();
            _blockTrail.Initialize();
            Log("Created BlockTrail (piece lock trail)");
        }

        // =================================================================
        // TUI (.engine powered)
        // =================================================================

        private void CreateTUI()
        {
            var go = new GameObject("TetrisTUI");
            _tuiManager = go.AddComponent<TetrisTUIManager>();
            _tuiManager.Initialize(_match, _playerProgram);
            Log("Created TUI (left debugger + right status panel)");
        }

        // =================================================================
        // EVENT WIRING
        // =================================================================

        private void WireEvents()
        {
            if (SimulationTime.Instance != null)
            {
                SimulationTime.Instance.OnTimeScaleChanged += s => Log($"Time scale → {s:F0}x");
                SimulationTime.Instance.OnPausedChanged += p => Log(p ? "⏸ PAUSED" : "▶ RESUMED");
            }

            if (_match != null)
            {
                _match.OnMatchStarted += () =>
                {
                    Log("MATCH STARTED");
                    _boardRenderer?.MarkDirty();
                    _blockTrail?.ClearTrail();
                };

                _match.OnPointsScored += (points, lines) =>
                {
                    string label = lines == 4 ? "TETRIS!" : $"{lines} line{(lines > 1 ? "s" : "")}";
                    Log($"{label} │ +{points} pts │ Score: {_match.Score}");
                    _boardRenderer?.MarkDirty();

                    // Line clear glow — flash bright white
                    Color lineGlow = new Color(5f, 5f, 5f);
                    float boost = lines == 4 ? 2f : 1f;
                    lineGlow *= boost;
                    _boardRenderer?.FlashPieceLight(2f * boost, Color.white);
                };

                _match.OnLevelUp += level =>
                {
                    Log($"LEVEL UP → {level} │ Drop: {_match.CurrentDropInterval:F2}s");
                    // Level up glow
                    _boardRenderer?.FlashPieceLight(3f, new Color(1f, 1f, 0f));
                };

                _match.OnGameOver += () =>
                {
                    Log($"GAME OVER │ Score: {_match.Score} │ Lines: {_match.LinesTotal} │ Level: {_match.Level}");

                    // Death flash — board goes red
                    _boardRenderer?.FlashBoard(new Color(4f, 0.2f, 0.2f));
                    _boardRenderer?.FlashPieceLight(5f, Color.red);
                    _blockTrail?.ClearTrail();

                    if (autoRestart)
                        StartCoroutine(RestartAfterDelay());
                };

                _match.OnPieceLocked += () =>
                {
                    _boardRenderer?.MarkDirty();

                    // Flash the locked cells and spawn trail
                    if (_match.ActivePiece != null)
                    {
                        // ActivePiece is about to be replaced, but positions were already recorded
                    }
                    _boardRenderer?.FlashPieceLight(1.5f, new Color(1f, 1f, 1f));
                };

                _match.OnLinesCleared += _ => _boardRenderer?.MarkDirty();

                _match.OnPieceSpawned += () =>
                {
                    // Subtle glow on new piece
                    if (_match.ActivePiece != null)
                    {
                        Color pieceCol = Tetrominos.Colors[_match.ActivePiece.Shape];
                        _boardRenderer?.FlashPieceLight(0.8f, pieceCol);
                    }
                };

                _match.OnHardDrop += rows =>
                {
                    // Hard drop flash + trail
                    if (_match.ActivePiece != null && _boardRenderer != null)
                    {
                        var positions = _boardRenderer.GetActivePieceCellPositions();
                        Color pieceCol = Tetrominos.Colors[_match.ActivePiece.Shape];
                        _blockTrail?.SpawnTrailAt(positions, pieceCol);
                        _boardRenderer.FlashPieceLight(2f + rows * 0.05f, pieceCol);
                    }
                };
            }
        }

        // =================================================================
        // BOOT SEQUENCE
        // =================================================================

        private IEnumerator RunBootSequence()
        {
            yield return null; // wait one frame for all managers to initialize
            yield return null;

            LogDivider();
            Log("🧱 TETRIS — Code Your Stacker");
            LogDivider();
            LogStatus("BOARD", $"{TetrisBoard.Width}×{TetrisBoard.Height}");
            LogStatus("DROP", $"{baseDropInterval}s (Level 1)");
            LogStatus("LOCK", $"{lockDelay}s");
            LogEnabled("SCRIPTING", enableScripting);
            LogEnabled("TIME SCALE", enableTimeScale);
            LogEnabled("AUTO RESTART", autoRestart);
            LogDivider();

            // Start the first match
            _match.StartMatch();
            Log("First match started — GO!");
        }

        private IEnumerator RestartAfterDelay()
        {
            float waited = 0f;
            while (waited < restartDelay)
            {
                if (SimulationTime.Instance != null && !SimulationTime.Instance.isPaused)
                    waited += Time.deltaTime * (SimulationTime.Instance?.timeScale ?? 1f);
                yield return null;
            }

            _match.StartMatch();
            Log("Match restarted");
        }

        private void OnDestroy()
        {
            QualityBridge.Unregister(this);
        }
    }
}
