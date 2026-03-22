// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CodeGamified.Procedural;
using CodeGamified.Quality;
using CodeGamified.Time;

namespace Tetris.Game
{
    /// <summary>
    /// Visual renderer for the Tetris board — builds 3D cell cubes via ProceduralAssembler.
    /// Renders locked grid cells, the active piece, and a ghost piece preview.
    /// Rebuilt each frame that the board changes (dirty flag).
    /// Glow system: emissivity-based (no point lights). Bloom picks up HDR emission.
    /// Background: procedural grid texture floor. Borders: neon emissive cushions.
    /// </summary>
    public class TetrisBoardRenderer : MonoBehaviour, IQualityResponsive
    {
        private TetrisBoard _board;
        private TetrisMatchManager _match;
        private ColorPalette _palette;

        // Cell GameObjects — pooled and reused
        private GameObject[,] _cellObjects;
        private GameObject[] _activeCellObjects;
        private GameObject[] _ghostCellObjects;

        // Frame/border + background
        private GameObject _frameObject;
        private GameObject _floorObject;
        private GameObject[] _borderWalls; // 0=L 1=R 2=B (neon cushions)

        // Dirty flag
        private bool _dirty = true;
        private bool _suppressDirty;  // true during line-clear pulse animation

        // ── Emissivity glow system (no point lights) ─────────────
        private const float EmissionMild    = 0.5f;   // baseline for locked cells (mild visible glow)
        private const float EmissionBright  = 1.2f;   // active piece cells
        private const float EmissionGhost   = 0.08f;  // ghost piece
        private const float EmissionDecay   = 4f;     // lerp speed toward target

        // Colors — neon retrowave border aesthetic
        private static readonly Color BorderColor    = new Color(0.015f, 0.02f, 0.04f, 0.5f);  // 50% alpha dark base
        private static readonly Color BorderEmission = new Color(0.06f, 0.12f, 0.3f);           // mild cyan-indigo glow
        private static readonly Color FloorColor     = new Color(0f, 0f, 0f);                   // pure black
        private static readonly Color GridLineColor  = new Color(0.04f, 0.18f, 0.3f);           // brighter cyan-neon grid

        private readonly List<(Renderer renderer, Color baseColor)> _flashedRenderers = new();
        private readonly Dictionary<GameObject, Coroutine> _cellGlowCoroutines = new();

        // Preview panels (Next + Hold)
        private GameObject[] _nextCells;
        private GameObject[] _holdCells;
        private GameObject _nextPanel;
        private GameObject _holdPanel;

        // Cell size in world units
        public const float CellSize = 0.5f;

        // Board world-space origin (bottom-left corner)
        public Vector3 BoardOrigin => transform.position;

        public void Initialize(TetrisBoard board, TetrisMatchManager match, ColorPalette palette)
        {
            _board = board;
            _match = match;
            _palette = palette;

            _cellObjects = new GameObject[_board.TotalHeight, TetrisBoard.Width];
            _activeCellObjects = new GameObject[4];
            _ghostCellObjects = new GameObject[4];

            // Pre-create all cell objects (inactive)
            for (int r = 0; r < _board.TotalHeight; r++)
            {
                for (int c = 0; c < TetrisBoard.Width; c++)
                {
                    var go = CreateCellObject($"Cell_{r}_{c}");
                    go.SetActive(false);
                    _cellObjects[r, c] = go;
                }
            }

            for (int i = 0; i < 4; i++)
            {
                _activeCellObjects[i] = CreateCellObject($"Active_{i}");
                _activeCellObjects[i].SetActive(false);

                _ghostCellObjects[i] = CreateGhostCellObject($"Ghost_{i}");
                _ghostCellObjects[i].SetActive(false);
            }

            BuildFrame();
            BuildPreviews();

            // Wire events
            _board.OnBoardChanged += () => _dirty = true;

            QualityBridge.Register(this);
        }

        private void OnDisable() => QualityBridge.Unregister(this);

        public void OnQualityChanged(QualityTier tier)
        {
            RebuildFloorTexture();
            _dirty = true;
        }

        private void LateUpdate()
        {
            DecayFlashedRenderers();

            // Always update active piece + ghost (they move every frame)
            UpdateActivePiece();
            UpdateGhostPiece();
            UpdatePreviews();

            if (!_dirty || _suppressDirty) return;
            _dirty = false;
            RenderGrid();
        }

        /// <summary>Mark board visuals as needing refresh.</summary>
        public void MarkDirty() => _dirty = true;

        // ═══════════════════════════════════════════════════════════════
        // GRID RENDERING
        // ═══════════════════════════════════════════════════════════════

        private void RenderGrid()
        {
            for (int r = 0; r < _board.TotalHeight; r++)
            {
                for (int c = 0; c < TetrisBoard.Width; c++)
                {
                    int val = _board.Grid[r, c];
                    var go = _cellObjects[r, c];

                    if (val == 0)
                    {
                        go.SetActive(false);
                    }
                    else
                    {
                        go.SetActive(r < TetrisBoard.Height); // hide buffer rows
                        go.transform.localPosition = CellToWorld(r, c);
                        SetCellColor(go, Tetrominos.Colors[val - 1], EmissionMild);
                    }
                }
            }
        }

        private void UpdateActivePiece()
        {
            if (_match.ActivePiece == null || _match.GameOver)
            {
                for (int i = 0; i < 4; i++)
                    _activeCellObjects[i].SetActive(false);
                return;
            }

            var cells = _match.ActivePiece.GetCells();
            Color color = Tetrominos.Colors[_match.ActivePiece.Shape];

            for (int i = 0; i < 4; i++)
            {
                var go = _activeCellObjects[i];
                int r = cells[i].row;
                int c = cells[i].col;
                bool visible = r >= 0 && r < TetrisBoard.Height && c >= 0 && c < TetrisBoard.Width;
                go.SetActive(visible);
                if (visible)
                {
                    go.transform.localPosition = CellToWorld(r, c);
                    SetCellColor(go, color, EmissionBright);
                }
            }
        }

        private void UpdateGhostPiece()
        {
            if (_match.ActivePiece == null || _match.GameOver)
            {
                for (int i = 0; i < 4; i++)
                    _ghostCellObjects[i].SetActive(false);
                return;
            }

            int ghostRow = _match.ActivePiece.GhostRow();
            var offsets = Tetrominos.Rotations[_match.ActivePiece.Shape][_match.ActivePiece.Rotation];
            Color ghostColor = Tetrominos.Colors[_match.ActivePiece.Shape];
            ghostColor.a = 0.5f;

            for (int i = 0; i < 4; i++)
            {
                int r = ghostRow + offsets[i].row;
                int c = _match.ActivePiece.PivotCol + offsets[i].col;
                var go = _ghostCellObjects[i];
                bool visible = r >= 0 && r < TetrisBoard.Height && c >= 0 && c < TetrisBoard.Width;
                go.SetActive(visible);
                if (visible)
                {
                    go.transform.localPosition = CellToWorld(r, c);
                    SetGhostColor(go, ghostColor);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PREVIEW PANELS — Next piece + Held piece (right of board)
        // ═══════════════════════════════════════════════════════════════

        private void BuildPreviews()
        {
            float boardW = TetrisBoard.Width * CellSize;
            float boardH = TetrisBoard.Height * CellSize;

            float panelW = CellSize * 5f;
            float panelH = CellSize * 4f;
            float gap = CellSize * 1.5f;
            float panelCenterX = boardW + gap + panelW * 0.5f;
            float nextCenterY = boardH - panelH * 0.5f - CellSize * 0.5f;
            float holdCenterY = nextCenterY - panelH - CellSize * 1.5f;

            _nextPanel = CreatePreviewPanel("NextPanel", panelCenterX, nextCenterY, panelW, panelH);
            _holdPanel = CreatePreviewPanel("HoldPanel", panelCenterX, holdCenterY, panelW, panelH);

            _nextCells = new GameObject[4];
            _holdCells = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                _nextCells[i] = CreateCellObject($"Next_{i}");
                _nextCells[i].SetActive(false);
                _holdCells[i] = CreateCellObject($"Hold_{i}");
                _holdCells[i].SetActive(false);
            }
        }

        private GameObject CreatePreviewPanel(string name, float cx, float cy, float w, float h)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(cx, cy, CellSize * 0.5f);
            go.transform.localScale = new Vector3(w, h, CellSize * 0.1f);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var mat = r.material;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", FloorColor);
                else
                    mat.color = FloorColor;
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", BorderEmission * 0.3f);
                }
            }
            return go;
        }

        private void UpdatePreviews()
        {
            UpdatePreviewCells(_nextCells, _match.NextShape, _nextPanel);

            int holdShape = _match.HeldShape;
            if (holdShape < 0)
            {
                for (int i = 0; i < 4; i++)
                    _holdCells[i].SetActive(false);
            }
            else
            {
                UpdatePreviewCells(_holdCells, holdShape, _holdPanel);
            }
        }

        private void UpdatePreviewCells(GameObject[] cells, int shape, GameObject panel)
        {
            if (shape < 0 || shape >= Tetrominos.ShapeCount)
            {
                for (int i = 0; i < 4; i++)
                    cells[i].SetActive(false);
                return;
            }

            Vector3 center = panel.transform.localPosition;
            center.z = 0f;

            var offsets = Tetrominos.Rotations[shape][0];
            Color color = Tetrominos.Colors[shape];

            // Compute piece bounding box to center it in the panel
            float minR = float.MaxValue, maxR = float.MinValue;
            float minC = float.MaxValue, maxC = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                if (offsets[i].row < minR) minR = offsets[i].row;
                if (offsets[i].row > maxR) maxR = offsets[i].row;
                if (offsets[i].col < minC) minC = offsets[i].col;
                if (offsets[i].col > maxC) maxC = offsets[i].col;
            }
            float centerR = (minR + maxR) * 0.5f;
            float centerC = (minC + maxC) * 0.5f;

            for (int i = 0; i < 4; i++)
            {
                var go = cells[i];
                go.SetActive(true);
                float localX = (offsets[i].col - centerC) * CellSize;
                float localY = (offsets[i].row - centerR) * CellSize;
                go.transform.localPosition = center + new Vector3(localX, localY, 0f);
                SetCellColor(go, color, EmissionBright * 0.6f);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CELL HELPERS
        // ═══════════════════════════════════════════════════════════════

        private Vector3 CellToWorld(int row, int col)
        {
            return new Vector3(
                col * CellSize + CellSize * 0.5f,
                row * CellSize + CellSize * 0.5f,
                0f);
        }

        private GameObject CreateCellObject(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * (CellSize * 0.9f);

            // Remove collider — visual only
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return go;
        }

        private GameObject CreateGhostCellObject(string name)
        {
            var go = CreateCellObject(name);

            // Configure material for transparency (URP Lit)
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var mat = r.material;
                // URP surface type: 0 = Opaque, 1 = Transparent
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);  // Alpha blend
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
            }

            // Scale slightly smaller for visual distinction
            go.transform.localScale = Vector3.one * (CellSize * 0.85f);

            return go;
        }

        private static void SetGhostColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var mat = renderer.material;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                Color emColor = color * EmissionGhost;
                emColor.a = color.a;
                mat.SetColor("_EmissionColor", emColor);
            }
        }

        private void SetCellColor(GameObject go, Color color, float emissionMultiplier = EmissionMild)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var mat = renderer.material;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emissionMultiplier);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GLOW / FLASH API — called by TetrisBootstrap event wiring
        // Emissivity-based (no point lights). Bloom picks up HDR emission.
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Flash all active piece cells + nearby locked cells with an HDR emission burst.</summary>
        public void FlashActiveEmission(float intensity, Color color)
        {
            Color hdr = color * intensity;
            for (int i = 0; i < _activeCellObjects.Length; i++)
            {
                var go = _activeCellObjects[i];
                if (go == null || !go.activeSelf) continue;
                var r = go.GetComponent<Renderer>();
                if (r == null) continue;
                Color baseCol = _match?.ActivePiece != null
                    ? Tetrominos.Colors[_match.ActivePiece.Shape]
                    : color;
                _flashedRenderers.RemoveAll(e => e.renderer == r);
                _flashedRenderers.Add((r, baseCol));
                SetHDRColorMat(r.material, hdr);
            }

            // Also flash border walls for peripheral glow
            if (_borderWalls != null)
            {
                Color borderHDR = new Color(color.r * intensity * 0.4f, color.g * intensity * 0.4f, color.b * intensity * 0.4f);
                foreach (var wall in _borderWalls)
                {
                    if (wall == null) continue;
                    FlashRenderer(wall, borderHDR, BorderEmission);
                }
            }
        }

        /// <summary>Flash a locked cell at (row, col) with an HDR color burst.</summary>
        public void FlashCellGlow(int row, int col, Color hdrColor, Color baseColor)
        {
            if (row < 0 || row >= _board.TotalHeight || col < 0 || col >= TetrisBoard.Width) return;
            var go = _cellObjects[row, col];
            if (go == null) return;
            FlashRenderer(go, hdrColor, baseColor);
        }

        /// <summary>Flash all active piece cells with an HDR burst on lock.</summary>
        public void FlashPieceLock(int shape, int rotation, int pivotRow, int pivotCol)
        {
            var offsets = Tetrominos.Rotations[shape][rotation];
            Color baseCol = Tetrominos.Colors[shape];
            Color hdr = new Color(baseCol.r * 4f, baseCol.g * 4f, baseCol.b * 4f);

            for (int i = 0; i < offsets.Length; i++)
            {
                int r = pivotRow + offsets[i].row;
                int c = pivotCol + offsets[i].col;
                FlashCellGlow(r, c, hdr, baseCol);
            }
        }

        /// <summary>Flash a whole row (line clear glow).</summary>
        public void FlashRow(int row, Color hdrColor)
        {
            if (row < 0 || row >= _board.TotalHeight) return;
            for (int c = 0; c < TetrisBoard.Width; c++)
            {
                var go = _cellObjects[row, c];
                if (go == null || !go.activeSelf) continue;
                int val = _board.Grid[row, c];
                Color baseCol = val > 0 ? Tetrominos.Colors[val - 1] : new Color(0.02f, 0.02f, 0.05f);
                FlashRenderer(go, hdrColor, baseCol);
            }
        }

        /// <summary>Flash entire visible board (game over, etc).</summary>
        public void FlashBoard(Color hdrColor)
        {
            for (int r = 0; r < TetrisBoard.Height; r++)
            {
                for (int c = 0; c < TetrisBoard.Width; c++)
                {
                    var go = _cellObjects[r, c];
                    if (go == null || !go.activeSelf) continue;
                    int val = _board.Grid[r, c];
                    Color baseCol = val > 0 ? Tetrominos.Colors[val - 1] : new Color(0.02f, 0.02f, 0.05f);
                    FlashRenderer(go, hdrColor, baseCol);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // LINE CLEAR PULSE — animates rows before they vanish
        // ═══════════════════════════════════════════════════════════════

        private Coroutine _linePulseCoroutine;

        /// <summary>
        /// Start a pulse animation on the given rows (called BEFORE rows are removed).
        /// Suppresses dirty re-render until the pulse completes.
        /// Duration is 1 second scaled to simulation time.
        /// </summary>
        public void PulseAndClearRows(int[] rows)
        {
            if (rows == null || rows.Length == 0) return;
            if (_linePulseCoroutine != null)
            {
                StopCoroutine(_linePulseCoroutine);
                _suppressDirty = false;
            }
            _linePulseCoroutine = StartCoroutine(LinePulseRoutine(rows));
        }

        private IEnumerator LinePulseRoutine(int[] rows)
        {
            _suppressDirty = true;
            bool isTetris = rows.Length >= 4;
            float duration = 1f;
            float elapsed = 0f;

            // Capture base colors of each cell in the clearing rows
            var captured = new Color[rows.Length, TetrisBoard.Width];
            for (int ri = 0; ri < rows.Length; ri++)
            {
                int r = rows[ri];
                for (int c = 0; c < TetrisBoard.Width; c++)
                {
                    int val = _board.Grid[r, c];
                    captured[ri, c] = val > 0 ? Tetrominos.Colors[val - 1] : Color.white;
                }
            }

            Color peakColor = isTetris
                ? new Color(8f, 8f, 4f) // gold HDR burst for Tetris
                : new Color(6f, 6f, 6f); // white HDR burst

            while (elapsed < duration)
            {
                float timeScale = CodeGamified.Time.SimulationTime.Instance?.timeScale ?? 1f;
                bool paused = CodeGamified.Time.SimulationTime.Instance != null
                    && CodeGamified.Time.SimulationTime.Instance.isPaused;
                if (!paused)
                    elapsed += Time.deltaTime * timeScale;

                // 0→1 triangle wave (up then down)
                float t = elapsed / duration;
                float pulse = t < 0.4f
                    ? t / 0.4f                 // ramp up fast
                    : 1f - (t - 0.4f) / 0.6f; // decay slower
                pulse = Mathf.Clamp01(pulse);

                for (int ri = 0; ri < rows.Length; ri++)
                {
                    int r = rows[ri];
                    if (r < 0 || r >= _board.TotalHeight) continue;
                    for (int c = 0; c < TetrisBoard.Width; c++)
                    {
                        var go = _cellObjects[r, c];
                        if (go == null) continue;
                        go.SetActive(true);
                        Color baseCol = captured[ri, c];
                        Color hdr = Color.Lerp(baseCol, peakColor, pulse);
                        SetHDRColorMat(go.GetComponent<Renderer>().material, hdr);
                    }
                }

                yield return null;
            }

            _suppressDirty = false;
            _dirty = true;
            _linePulseCoroutine = null;
        }

        /// <summary>Get the world positions of the active piece cells (for trail spawning).</summary>
        public Vector3[] GetActivePieceCellPositions()
        {
            if (_match.ActivePiece == null) return System.Array.Empty<Vector3>();
            var offsets = Tetrominos.Rotations[_match.ActivePiece.Shape][_match.ActivePiece.Rotation];
            var positions = new Vector3[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                int r = _match.ActivePiece.PivotRow + offsets[i].row;
                int c = _match.ActivePiece.PivotCol + offsets[i].col;
                positions[i] = transform.TransformPoint(CellToWorld(r, c));
            }
            return positions;
        }

        // ═══════════════════════════════════════════════════════════════
        // DECAY — runs every LateUpdate
        // ═══════════════════════════════════════════════════════════════

        private void DecayFlashedRenderers()
        {
            float decay = Mathf.Clamp01(EmissionDecay * Time.unscaledDeltaTime);
            for (int i = _flashedRenderers.Count - 1; i >= 0; i--)
            {
                var (fr, baseCol) = _flashedRenderers[i];
                if (fr == null) { _flashedRenderers.RemoveAt(i); continue; }
                var mat = fr.material;
                Color current = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
                Color next = Color.Lerp(current, baseCol, decay);
                SetHDRColorMat(mat, next);
                if (Mathf.Abs(next.r - baseCol.r) + Mathf.Abs(next.g - baseCol.g) + Mathf.Abs(next.b - baseCol.b) < 0.03f)
                {
                    SetHDRColorMat(mat, baseCol);
                    _flashedRenderers.RemoveAt(i);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HDR HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void FlashRenderer(GameObject go, Color hdrColor, Color baseColor)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            int idx = _flashedRenderers.FindIndex(e => e.renderer == r);
            Color origColor = baseColor;
            if (idx >= 0)
            {
                origColor = _flashedRenderers[idx].baseColor;
                _flashedRenderers.RemoveAt(idx);
            }
            _flashedRenderers.Add((r, origColor));
            SetHDRColorMat(r.material, hdrColor);
        }

        private static void SetHDRColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            SetHDRColorMat(renderer.material, color);
        }

        private static void SetHDRColorMat(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // FRAME / BORDER — neon aesthetic with emissive glow
        // ═══════════════════════════════════════════════════════════════

        private void BuildFrame()
        {
            if (_frameObject != null) Destroy(_frameObject);

            _frameObject = new GameObject("Frame");
            _frameObject.transform.SetParent(transform, false);

            float boardW = TetrisBoard.Width * CellSize;
            float boardH = TetrisBoard.Height * CellSize;
            float thickness = CellSize * 0.18f;
            float depth = CellSize;

            // ── Neon border walls (dark base + emissive glow) ──
            _borderWalls = new GameObject[3];

            // Left wall
            _borderWalls[0] = CreateNeonWall(_frameObject.transform, "Left",
                new Vector3(-thickness * 0.5f, boardH * 0.5f, 0f),
                new Vector3(thickness, boardH + thickness * 2, depth));

            // Right wall
            _borderWalls[1] = CreateNeonWall(_frameObject.transform, "Right",
                new Vector3(boardW + thickness * 0.5f, boardH * 0.5f, 0f),
                new Vector3(thickness, boardH + thickness * 2, depth));

            // Bottom wall
            _borderWalls[2] = CreateNeonWall(_frameObject.transform, "Bottom",
                new Vector3(boardW * 0.5f, -thickness * 0.5f, 0f),
                new Vector3(boardW + thickness * 2, thickness, depth));

            // Corner blocks — flush off rough edge joins
            float cornerY = boardH + thickness * 0.5f;
            Vector3 cornerScale = new Vector3(thickness, thickness, depth);
            CreateNeonWall(_frameObject.transform, "CornerTL",
                new Vector3(-thickness * 0.5f, cornerY, 0f), cornerScale);
            CreateNeonWall(_frameObject.transform, "CornerTR",
                new Vector3(boardW + thickness * 0.5f, cornerY, 0f), cornerScale);

            // ── Floor: procedural grid texture behind cells ──
            BuildFloor(boardW, boardH, depth);
        }

        private GameObject CreateNeonWall(Transform parent, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // 50% alpha transparent base with mild neon emission
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var mat = r.material;
                // Configure for transparency (URP)
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", BorderColor);
                else
                    mat.color = BorderColor;

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", BorderEmission);
                }
            }
            return go;
        }

        // ═══════════════════════════════════════════════════════════════
        // PROCEDURAL GRID TEXTURE — retrowave floor behind cells
        // ═══════════════════════════════════════════════════════════════

        private void BuildFloor(float boardW, float boardH, float depth)
        {
            if (_floorObject != null) Destroy(_floorObject);

            _floorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floorObject.name = "Floor";
            _floorObject.transform.SetParent(_frameObject.transform, false);
            _floorObject.transform.localPosition = new Vector3(boardW * 0.5f, boardH * 0.5f, depth * 0.5f);
            _floorObject.transform.localScale = new Vector3(boardW, boardH, depth * 0.1f);
            var col = _floorObject.GetComponent<Collider>();
            if (col != null) Destroy(col);

            ApplyFloorTexture(_floorObject, boardW, boardH);
        }

        private void RebuildFloorTexture()
        {
            if (_floorObject == null) return;
            float boardW = TetrisBoard.Width * CellSize;
            float boardH = TetrisBoard.Height * CellSize;
            ApplyFloorTexture(_floorObject, boardW, boardH);
        }

        private void ApplyFloorTexture(GameObject go, float boardW, float boardH)
        {
            var tier = QualityBridge.CurrentTier;

            // Low: plain dark floor, no grid
            if (tier == QualityTier.Low)
            {
                SetCellColor(go, FloorColor, 0f);
                return;
            }

            // Texture resolution scales with tier
            int texW, texH;
            switch (tier)
            {
                case QualityTier.Medium: texW = 512;  texH = 1024; break;
                case QualityTier.High:   texW = 1024; texH = 2048; break;
                default:                 texW = 2048; texH = 4096; break; // Ultra
            }

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[texW * texH];
            Color32 bg = new Color32(0, 0, 0, 0); // pure black / transparent
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

            bool isUltra = tier == QualityTier.Ultra;
            bool isHigh  = tier == QualityTier.High || isUltra;

            // ── Color palette ──
            Color32 lineGlow   = (Color32)new Color(GridLineColor.r * 3.5f, GridLineColor.g * 3.5f, GridLineColor.b * 3.5f);
            Color32 lineBright = isUltra
                ? (Color32)new Color(GridLineColor.r * 2.5f, GridLineColor.g * 2.5f, GridLineColor.b * 2.5f)
                : (Color32)new Color(GridLineColor.r * 1.5f, GridLineColor.g * 1.5f, GridLineColor.b * 1.5f);
            Color32 lineMid = isUltra ? (Color32)new Color(GridLineColor.r * 1.2f, GridLineColor.g * 1.2f, GridLineColor.b * 1.2f)
                : isHigh ? GridLineColor
                         : (Color32)new Color(GridLineColor.r * 0.7f,  GridLineColor.g * 0.7f,  GridLineColor.b * 0.7f);
            Color32 lineDim    = (Color32)new Color(GridLineColor.r * 0.4f, GridLineColor.g * 0.4f, GridLineColor.b * 0.4f);
            Color32 lineGhost  = (Color32)new Color(GridLineColor.r * 0.2f, GridLineColor.g * 0.2f, GridLineColor.b * 0.2f);

            // Pixels-per-cell conversions
            float ppwX = texW / boardW;
            float ppwY = texH / boardH;
            float cellSpX = ppwX * CellSize;
            float cellSpY = ppwY * CellSize;

            // Board center in tex coords
            float centerX = texW * 0.5f;
            float centerY = texH * 0.5f;

            // ── Ultra: sub-sub-cell background grid ──
            if (isUltra)
            {
                float fineSpX = cellSpX * 0.25f;
                float fineSpY = cellSpY * 0.25f;
                for (float gx = fineSpX; gx < texW; gx += fineSpX)
                    DrawGridVLine(pixels, texW, texH, Mathf.RoundToInt(gx), lineGhost, 1);
                for (float gy = fineSpY; gy < texH; gy += fineSpY)
                    DrawGridHLine(pixels, texW, texH, Mathf.RoundToInt(gy), lineGhost, 1);
            }

            // ── Sub-cell grid (half-cell reference lines) ──
            if (isHigh)
            {
                float subSpX = cellSpX * 0.5f;
                float subSpY = cellSpY * 0.5f;
                Color32 subCol = isUltra ? lineDim : lineDim;
                for (float gx = subSpX; gx < texW; gx += subSpX)
                    DrawGridVLine(pixels, texW, texH, Mathf.RoundToInt(gx), subCol, 1);
                for (float gy = subSpY; gy < texH; gy += subSpY)
                    DrawGridHLine(pixels, texW, texH, Mathf.RoundToInt(gy), subCol, 1);
            }

            // ── Cell grid lines (one per cell boundary) ──
            Color32 cellGridCol = isUltra ? lineBright : lineMid;
            for (float gx = 0f; gx <= texW; gx += cellSpX)
                DrawGridVLine(pixels, texW, texH, Mathf.RoundToInt(gx), cellGridCol, isUltra ? 2 : 1);
            for (float gy = 0f; gy <= texH; gy += cellSpY)
                DrawGridHLine(pixels, texW, texH, Mathf.RoundToInt(gy), cellGridCol, isUltra ? 2 : 1);

            // ── Center crosshair (vertical at column 5, horizontal at row 10) ──
            if (isUltra)
            {
                DrawGridVLineGlow(pixels, texW, texH, Mathf.RoundToInt(centerX), lineGlow, 5);
                DrawGridHLineGlow(pixels, texW, texH, Mathf.RoundToInt(centerY), lineGlow, 5);
            }
            else
            {
                DrawGridVLine(pixels, texW, texH, Mathf.RoundToInt(centerX), lineBright, isHigh ? 2 : 1);
                DrawGridHLine(pixels, texW, texH, Mathf.RoundToInt(centerY), lineBright, isHigh ? 2 : 1);
            }

            // ══════════════════════════════════
            // TETRIS-SPECIFIC MARKINGS
            // ══════════════════════════════════

            // ── Spawn zone line (top — row 20 boundary, where pieces enter) ──
            {
                int spawnLineY = texH - 1;
                if (isUltra)
                    DrawGridHLineGlow(pixels, texW, texH, spawnLineY, lineBright, 4);
                else if (isHigh)
                    DrawGridHLine(pixels, texW, texH, spawnLineY, lineMid, 2);
            }

            // ── Danger zone (top 4 rows — row 16-19) ──
            if (isHigh)
            {
                int dangerStartY = Mathf.RoundToInt(texH * 0.8f);
                Color32 dangerTint = (Color32)new Color(0.08f, 0.01f, 0.01f);
                for (int y = dangerStartY; y < texH; y++)
                    for (int x = 0; x < texW; x++)
                        pixels[y * texW + x] = MaxGridColor(pixels[y * texW + x], dangerTint);

                // Ultra: danger boundary glow line at row 16
                if (isUltra)
                {
                    Color32 dangerGlow = (Color32)new Color(0.25f, 0.04f, 0.04f);
                    DrawGridHLineGlow(pixels, texW, texH, dangerStartY, dangerGlow, 6);
                }
            }

            // ── Row markers at every 5 rows (subtle tick marks along left/right edges) ──
            if (isHigh)
            {
                int tickLen = isUltra ? 8 : 4;
                Color32 tickCol = isUltra ? lineBright : lineMid;
                for (int row = 5; row < TetrisBoard.Height; row += 5)
                {
                    int y = Mathf.RoundToInt(row * cellSpY);
                    // Left tick
                    for (int x = 0; x < tickLen && x < texW; x++)
                        if (y >= 0 && y < texH)
                            pixels[y * texW + x] = MaxGridColor(pixels[y * texW + x], tickCol);
                    // Right tick
                    for (int x = texW - tickLen; x < texW; x++)
                        if (x >= 0 && y >= 0 && y < texH)
                            pixels[y * texW + x] = MaxGridColor(pixels[y * texW + x], tickCol);
                }
            }

            // ── Column center dots along bottom (column axis markers) ──
            if (isHigh)
            {
                int dotRadius = isUltra ? 4 : 2;
                Color32 dotCol = isUltra ? lineBright : lineMid;
                for (int col = 0; col < TetrisBoard.Width; col++)
                {
                    float cx = (col + 0.5f) * cellSpX;
                    DrawGridFilledCircle(pixels, texW, texH, cx, 3f, dotRadius, dotCol);
                }
            }

            // ── Concentric circles around board center ──
            if (isUltra)
            {
                float circBase = ppwY * CellSize;
                float circleSpacing = circBase * 0.5f;
                float maxR = texH * 0.45f;
                for (float r = circleSpacing; r < maxR; r += circleSpacing)
                {
                    bool isPrimary = Mathf.Abs(r % (circBase * 2f)) < 1f;
                    Color32 cc = isPrimary ? lineMid : lineDim;
                    DrawGridCircleGlow(pixels, texW, texH, centerX, centerY, r, cc, isPrimary ? 3 : 2);
                }
            }
            else if (isHigh)
            {
                float circleSpacing = ppwY * CellSize * 2f;
                float maxR = texH * 0.45f;
                for (float r = circleSpacing; r < maxR; r += circleSpacing)
                    DrawGridCircle(pixels, texW, texH, centerX, centerY, r, lineDim);
            }

            // ── Ultra: diamond overlay around center ──
            if (isUltra)
            {
                float dSp = ppwY * CellSize;
                float maxD = texH * 0.4f;
                for (float s = dSp; s < maxD; s += dSp)
                {
                    DrawGridLine(pixels, texW, texH, centerX, centerY - s, centerX + s, centerY, lineDim);
                    DrawGridLine(pixels, texW, texH, centerX + s, centerY, centerX, centerY + s, lineDim);
                    DrawGridLine(pixels, texW, texH, centerX, centerY + s, centerX - s, centerY, lineDim);
                    DrawGridLine(pixels, texW, texH, centerX - s, centerY, centerX, centerY - s, lineDim);
                }
            }

            // ── Ultra: spawn zone crosshair (top center, where pieces appear) ──
            if (isUltra)
            {
                float spawnY = texH - cellSpY * 0.5f;
                int spotR = 6;
                DrawGridFilledCircle(pixels, texW, texH, centerX, spawnY, spotR, lineGlow);
                // Glow halo
                DrawGridFilledCircle(pixels, texW, texH, centerX, spawnY, spotR + 4, lineDim);
                DrawGridFilledCircle(pixels, texW, texH, centerX, spawnY, spotR, lineGlow);
            }

            // ── Ultra: column numbers as pip clusters (1 dot = col 1, 2 dots = col 2, etc.) ──
            if (isUltra)
            {
                int pipR = 2;
                Color32 pipCol = lineMid;
                for (int col = 0; col < TetrisBoard.Width; col++)
                {
                    float cx = (col + 0.5f) * cellSpX;
                    float by = cellSpY * 0.25f; // just above bottom
                    int pips = (col + 1) % 5 == 0 ? 1 : 0; // large dot at 5, 10
                    if (pips > 0)
                    {
                        DrawGridFilledCircle(pixels, texW, texH, cx, by, pipR + 2, lineBright);
                    }
                    else if ((col + 1) % 2 == 0)
                    {
                        // Small dot at even columns
                        DrawGridFilledCircle(pixels, texW, texH, cx, by, pipR, pipCol);
                    }
                }
            }

            // ── Ultra: diagonal scan lines (retrowave aesthetic) ──
            if (isUltra)
            {
                Color32 scanCol = lineGhost;
                float scanSpacing = cellSpY * 2f;
                for (float offset = 0f; offset < texH + texW; offset += scanSpacing)
                {
                    DrawGridLine(pixels, texW, texH,
                        0f, offset,
                        Mathf.Min(offset, texW), Mathf.Max(0f, offset - texW),
                        scanCol);
                }
            }

            // ── Ultra: row-10 kill line glow (halfway marker) ──
            if (isUltra)
            {
                Color32 halfGlow = (Color32)new Color(GridLineColor.r * 1.5f, GridLineColor.g * 1.5f, GridLineColor.b * 1.5f);
                DrawGridHLineGlow(pixels, texW, texH, Mathf.RoundToInt(centerY), halfGlow, 3);
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var mat = rend.material;
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", Color.white);

            // Emission — grid lines glow neon; stronger at higher tiers
            float emStrength = isUltra ? 1.2f : isHigh ? 0.5f : 0.25f;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(emStrength, emStrength, emStrength));
                if (mat.HasProperty("_EmissionMap"))
                    mat.SetTexture("_EmissionMap", tex);
            }
        }

        // ═════════════════════════════════════════════════════════════
        // GRID TEXTURE DRAWING HELPERS
        // ═════════════════════════════════════════════════════════════

        private static void DrawGridVLine(Color32[] px, int w, int h, int x, Color32 c, int thickness)
        {
            for (int d = 0; d < thickness; d++)
            {
                int xx = x + d - thickness / 2;
                if (xx < 0 || xx >= w) continue;
                for (int y = 0; y < h; y++)
                    px[y * w + xx] = MaxGridColor(px[y * w + xx], c);
            }
        }

        private static void DrawGridHLine(Color32[] px, int w, int h, int y, Color32 c, int thickness)
        {
            for (int d = 0; d < thickness; d++)
            {
                int yy = y + d - thickness / 2;
                if (yy < 0 || yy >= h) continue;
                for (int x = 0; x < w; x++)
                    px[yy * w + x] = MaxGridColor(px[yy * w + x], c);
            }
        }

        private static void DrawGridVLineGlow(Color32[] px, int w, int h, int x, Color32 core, int radius)
        {
            for (int d = -radius; d <= radius; d++)
            {
                int xx = x + d;
                if (xx < 0 || xx >= w) continue;
                float t = 1f - (float)Mathf.Abs(d) / (radius + 1);
                t *= t; // quadratic falloff
                Color32 c = LerpGridColor32(FloorColor, core, t);
                for (int y = 0; y < h; y++)
                    px[y * w + xx] = MaxGridColor(px[y * w + xx], c);
            }
        }

        private static void DrawGridHLineGlow(Color32[] px, int w, int h, int y, Color32 core, int radius)
        {
            for (int d = -radius; d <= radius; d++)
            {
                int yy = y + d;
                if (yy < 0 || yy >= h) continue;
                float t = 1f - (float)Mathf.Abs(d) / (radius + 1);
                t *= t;
                Color32 c = LerpGridColor32(FloorColor, core, t);
                for (int x = 0; x < w; x++)
                    px[yy * w + x] = MaxGridColor(px[yy * w + x], c);
            }
        }

        private static void DrawGridCircle(Color32[] px, int w, int h, float cx, float cy, float r, Color32 c)
        {
            int steps = Mathf.Max(64, Mathf.RoundToInt(r * 0.5f));
            for (int i = 0; i < steps; i++)
            {
                float angle = (float)i / steps * Mathf.PI * 2f;
                int px_ = Mathf.RoundToInt(cx + Mathf.Cos(angle) * r);
                int py_ = Mathf.RoundToInt(cy + Mathf.Sin(angle) * r);
                if (px_ >= 0 && px_ < w && py_ >= 0 && py_ < h)
                    px[py_ * w + px_] = MaxGridColor(px[py_ * w + px_], c);
            }
        }

        private static void DrawGridCircleGlow(Color32[] px, int w, int h,
            float cx, float cy, float r, Color32 core, int radius)
        {
            int steps = Mathf.Max(128, Mathf.RoundToInt(r));
            for (int i = 0; i < steps; i++)
            {
                float angle = (float)i / steps * Mathf.PI * 2f;
                float fx = cx + Mathf.Cos(angle) * r;
                float fy = cy + Mathf.Sin(angle) * r;
                DrawGridGlowDot(px, w, h, fx, fy, core, radius);
            }
        }

        private static void DrawGridGlowDot(Color32[] px, int w, int h,
            float fx, float fy, Color32 core, int radius)
        {
            int ix = Mathf.RoundToInt(fx);
            int iy = Mathf.RoundToInt(fy);
            int r2 = (radius + 1) * (radius + 1);
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int dist2 = dx * dx + dy * dy;
                    if (dist2 > r2) continue;
                    int px_ = ix + dx;
                    int py_ = iy + dy;
                    if (px_ < 0 || px_ >= w || py_ < 0 || py_ >= h) continue;
                    float t = 1f - Mathf.Sqrt((float)dist2) / (radius + 1);
                    t *= t;
                    Color32 c = LerpGridColor32(FloorColor, core, t);
                    px[py_ * w + px_] = MaxGridColor(px[py_ * w + px_], c);
                }
            }
        }

        private static void DrawGridFilledCircle(Color32[] px, int w, int h,
            float cx, float cy, int radius, Color32 c)
        {
            int ix = Mathf.RoundToInt(cx);
            int iy = Mathf.RoundToInt(cy);
            int r2 = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int px_ = ix + dx;
                    int py_ = iy + dy;
                    if (px_ >= 0 && px_ < w && py_ >= 0 && py_ < h)
                        px[py_ * w + px_] = MaxGridColor(px[py_ * w + px_], c);
                }
            }
        }

        private static void DrawGridLine(Color32[] px, int w, int h,
            float x0, float y0, float x1, float y1, Color32 c)
        {
            int steps = Mathf.Max(Mathf.Abs(Mathf.RoundToInt(x1 - x0)), Mathf.Abs(Mathf.RoundToInt(y1 - y0)));
            if (steps == 0) return;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                int px_ = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), 0, w - 1);
                int py_ = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), 0, h - 1);
                px[py_ * w + px_] = MaxGridColor(px[py_ * w + px_], c);
            }
        }

        private static Color32 LerpGridColor32(Color bg, Color32 fg, float t)
        {
            return new Color32(
                (byte)Mathf.Min(255, (int)(bg.r * 255 * (1f - t) + fg.r * t)),
                (byte)Mathf.Min(255, (int)(bg.g * 255 * (1f - t) + fg.g * t)),
                (byte)Mathf.Min(255, (int)(bg.b * 255 * (1f - t) + fg.b * t)),
                255);
        }

        private static Color32 MaxGridColor(Color32 a, Color32 b)
        {
            return new Color32(
                (byte)Mathf.Max(a.r, b.r),
                (byte)Mathf.Max(a.g, b.g),
                (byte)Mathf.Max(a.b, b.b),
                255);
        }
    }
}
