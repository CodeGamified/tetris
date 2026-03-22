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
        private bool _cameraFollowPiece;  // true = camera tracks active piece
        private Vector3 _defaultCameraPos;
        private float _followElevation = 6f;
        private float _followOffset = 4f;
        private const float FollowLerpSpeed = 5f;
        private const float LookLerpSpeed = 3f;
        private const float ZoomSpeed = 2f;
        private const float MinZoom = 2f;
        private const float MaxZoom = 20f;
        private const float ClickDepth = 0.5f; // Z depth for click ray intersection

        // Smooth look target (lerped to prevent jarring snaps)
        private Vector3 _smoothLookTarget;
        private bool _smoothLookInitialized;

        // Post-processing
        private Bloom _bloom;
        private Volume _postProcessVolume;

        // =================================================================
        // UPDATE
        // =================================================================

        private void Update()
        {
            HandleScrollZoom();
            UpdateCameraFollow();
            HandleCameraClick();
            HandleCameraEscape();
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
        // CAMERA — click-to-follow + scroll zoom + Escape to default
        // =================================================================

        private void HandleScrollZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.001f) return;

            if (_cameraFollowPiece)
            {
                // Follow mode — zoom adjusts elevation
                _followElevation -= scroll * ZoomSpeed;
                _followElevation = Mathf.Clamp(_followElevation, MinZoom, MaxZoom);
                _followOffset = _followElevation * 0.65f;
            }
            else if (_cameraSway != null && _cameraSway.enabled)
            {
                // Sway mode — zoom adjusts base Z distance
                float currentZ = _defaultCameraPos.z;
                float newZ = currentZ + scroll * ZoomSpeed;
                newZ = Mathf.Clamp(newZ, -MaxZoom, -MinZoom);
                _defaultCameraPos.z = newZ;
                _cameraSway.SetBasePosition(_defaultCameraPos);
            }
        }

        private void HandleCameraClick()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            // Ray-plane intersection at Z = 0 (board plane)
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return;
            float t = -ray.origin.z / ray.direction.z;
            if (t < 0f) return;

            Vector3 hitPoint = ray.origin + t * ray.direction;

            // Check if hit is within board bounds (world space)
            float boardW = TetrisBoard.Width * TetrisBoardRenderer.CellSize;
            float boardH = TetrisBoard.Height * TetrisBoardRenderer.CellSize;
            Vector3 boardOrigin = _boardRenderer != null ? _boardRenderer.BoardOrigin : Vector3.zero;
            float localX = hitPoint.x - boardOrigin.x;
            float localY = hitPoint.y - boardOrigin.y;

            if (localX < -ClickDepth || localX > boardW + ClickDepth
                || localY < -ClickDepth || localY > boardH + ClickDepth)
                return;

            // Enter follow mode
            StopAllCoroutines();
            if (_cameraSway != null) _cameraSway.enabled = false;
            _cameraFollowPiece = true;
            _smoothLookInitialized = false;
            _followElevation = 6f;
            _followOffset = 4f;
            Log("Camera → following active piece");
        }

        private void UpdateCameraFollow()
        {
            if (!_cameraFollowPiece) return;

            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            if (_match == null || _match.ActivePiece == null || _match.GameOver)
            {
                // No active piece — hold position, don't release
                return;
            }

            // Compute piece world position
            int pivotRow = _match.ActivePiece.PivotRow;
            int pivotCol = _match.ActivePiece.PivotCol;
            Vector3 boardOrigin = _boardRenderer != null ? _boardRenderer.BoardOrigin : Vector3.zero;
            Vector3 pieceWorld = boardOrigin + new Vector3(
                pivotCol * TetrisBoardRenderer.CellSize + TetrisBoardRenderer.CellSize * 0.5f,
                pivotRow * TetrisBoardRenderer.CellSize + TetrisBoardRenderer.CellSize * 0.5f,
                0f);

            // Look target: ghost piece position (where piece will land)
            int ghostRow = _match.ActivePiece.GhostRow();
            Vector3 landingPos = boardOrigin + new Vector3(
                pivotCol * TetrisBoardRenderer.CellSize + TetrisBoardRenderer.CellSize * 0.5f,
                ghostRow * TetrisBoardRenderer.CellSize + TetrisBoardRenderer.CellSize * 0.5f,
                0f);

            // Smooth the look target
            Vector3 rawLookTarget = Vector3.Lerp(pieceWorld, landingPos, 0.4f);
            if (!_smoothLookInitialized)
            {
                _smoothLookTarget = rawLookTarget;
                _smoothLookInitialized = true;
            }
            else
            {
                _smoothLookTarget = Vector3.Lerp(
                    _smoothLookTarget, rawLookTarget,
                    Time.unscaledDeltaTime * LookLerpSpeed);
            }

            // Position camera in front of and above the piece
            Vector3 desiredCamPos = new Vector3(
                pieceWorld.x,
                pieceWorld.y + _followElevation * 0.3f,
                pieceWorld.z - _followOffset);

            Vector3 frameLookTarget = Vector3.Lerp(pieceWorld, _smoothLookTarget, 0.5f);

            cam.transform.position = Vector3.Lerp(
                cam.transform.position, desiredCamPos,
                Time.unscaledDeltaTime * FollowLerpSpeed);

            Quaternion desiredRot = Quaternion.LookRotation(
                frameLookTarget - cam.transform.position, Vector3.up);
            cam.transform.rotation = Quaternion.Slerp(
                cam.transform.rotation, desiredRot,
                Time.unscaledDeltaTime * FollowLerpSpeed);
        }

        private void HandleCameraEscape()
        {
            if (!_cameraFollowPiece) return;
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            _cameraFollowPiece = false;
            _smoothLookInitialized = false;
            StartCoroutine(LerpToDefaultView());
        }

        private IEnumerator LerpToDefaultView()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) yield break;

            if (_cameraSway != null) _cameraSway.enabled = false;

            Vector3 startPos = cam.transform.position;
            Quaternion startRot = cam.transform.rotation;
            Quaternion targetRot = Quaternion.LookRotation(
                BoardCenter() - _defaultCameraPos, Vector3.up);

            float duration = 0.6f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / duration;
                t = 1f - Mathf.Pow(1f - t, 3f); // cubic ease-out

                cam.transform.position = Vector3.Lerp(startPos, _defaultCameraPos, t);
                cam.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }

            cam.transform.position = _defaultCameraPos;
            cam.transform.LookAt(BoardCenter(), Vector3.up);

            if (_cameraSway != null)
            {
                _cameraSway.SetBasePosition(_defaultCameraPos);
                _cameraSway.enabled = true;
            }
            Log("Camera → default sway");
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
            // Board is centered at the origin by CreateBoardRenderer()
            return Vector3.zero;
        }

        private void SetupCamera()
        {
            if (!configureCamera) return;

            var cam = EnsureCamera();

            cam.orthographic = false;
            cam.fieldOfView = 60f;
            var center = BoardCenter();
            // Shift view right to accommodate Next/Hold preview panels
            var viewCenter = center + new Vector3(1f, 0f, 0f);
            cam.transform.position = viewCenter + new Vector3(0f, 0f, -12f);
            cam.transform.LookAt(viewCenter, Vector3.up);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            // Store default position for Escape return
            _defaultCameraPos = cam.transform.position;

            // Ambient sway
            _cameraSway = cam.gameObject.AddComponent<CameraAmbientMotion>();
            _cameraSway.lookAtTarget = viewCenter;

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

            Log("Camera: perspective, FOV=60, 3D view + sway + bloom + click-follow");
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

            // Center the board in the scene
            float boardW = TetrisBoard.Width * TetrisBoardRenderer.CellSize;
            _board.transform.position = new Vector3(-boardW * 0.5f, -TetrisBoard.Height * TetrisBoardRenderer.CellSize * 0.5f, 0f);

            Log("Created BoardRenderer (3D cubes, ghost piece, neon frame + emissive glow + preview panels)");
        }

        private ColorPalette CreatePalette()
        {
            var colors = new Dictionary<string, Color>
            {
                { "frame",      new Color(0.3f, 0.3f, 0.4f) },
                { "floor",      new Color(0.02f, 0.02f, 0.05f) },
                { "ghost",      new Color(1f, 1f, 1f, 0.5f) },
                { "I",          new Color(0f, 1f, 1f) },           // electric cyan
                { "O",          new Color(1f, 1f, 0.1f) },         // neon yellow
                { "T",          new Color(0.75f, 0.1f, 1f) },      // neon purple
                { "S",          new Color(0.15f, 1f, 0.4f) },      // electric lime
                { "Z",          new Color(1f, 0.08f, 0.55f) },     // neon rose
                { "J",          new Color(0.25f, 0.35f, 1f) },     // electric indigo
                { "L",          new Color(1f, 0.4f, 0.05f) },      // neon orange
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
            _blockTrail.Initialize(_match, _boardRenderer);
            Log("Created BlockTrail (drop-path LineRenderer trail)");
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
                    _boardRenderer?.FlashActiveEmission(2f * boost, Color.white);
                };

                _match.OnLevelUp += level =>
                {
                    Log($"LEVEL UP → {level} │ Drop: {_match.CurrentDropInterval:F2}s");
                    // Level up glow
                    _boardRenderer?.FlashActiveEmission(3f, new Color(1f, 1f, 0f));
                };

                _match.OnGameOver += () =>
                {
                    Log($"GAME OVER │ Score: {_match.Score} │ Lines: {_match.LinesTotal} │ Level: {_match.Level}");

                    // Death flash — board goes red
                    _boardRenderer?.FlashBoard(new Color(4f, 0.2f, 0.2f));
                    _boardRenderer?.FlashActiveEmission(5f, Color.red);
                    _blockTrail?.ClearTrail();

                    if (autoRestart)
                        StartCoroutine(RestartAfterDelay());
                };

                _match.OnPieceLocked += () =>
                {
                    _boardRenderer?.MarkDirty();
                    _blockTrail?.EndTrail();
                    _boardRenderer?.FlashActiveEmission(1.5f, new Color(1f, 1f, 1f));
                };

                _match.OnLinesCleared += _ => _boardRenderer?.MarkDirty();

                _match.OnRowsClearing += rows =>
                {
                    _boardRenderer?.PulseAndClearRows(rows);
                };

                _match.OnPieceSpawned += () =>
                {
                    // Start drop-path trail + subtle glow on new piece
                    if (_match.ActivePiece != null)
                    {
                        Color pieceCol = Tetrominos.Colors[_match.ActivePiece.Shape];
                        _blockTrail?.BeginTrail(pieceCol);
                        _boardRenderer?.FlashActiveEmission(0.8f, pieceCol);
                    }
                };

                _match.OnHardDrop += rows =>
                {
                    // Hard drop flash
                    if (_match.ActivePiece != null && _boardRenderer != null)
                    {
                        Color pieceCol = Tetrominos.Colors[_match.ActivePiece.Shape];
                        _boardRenderer.FlashActiveEmission(2f + rows * 0.05f, pieceCol);
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
