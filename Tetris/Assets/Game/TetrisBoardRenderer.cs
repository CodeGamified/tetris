// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CodeGamified.Procedural;
using CodeGamified.Quality;

namespace Tetris.Game
{
    /// <summary>
    /// Visual renderer for the Tetris board — builds 3D cell cubes via ProceduralAssembler.
    /// Renders locked grid cells, the active piece, and a ghost piece preview.
    /// Rebuilt each frame that the board changes (dirty flag).
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

        // Frame/border
        private GameObject _frameObject;

        // Dirty flag
        private bool _dirty = true;

        // ── Glow system ──────────────────────────────────────────
        private Light _pieceLight;
        private const float PieceLightBaseIntensity = 0.3f;
        private const float PieceLightDecay = 3f;

        private readonly List<(Renderer renderer, Color baseColor)> _flashedRenderers = new();
        private readonly Dictionary<GameObject, Coroutine> _cellGlowCoroutines = new();

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

                _ghostCellObjects[i] = CreateCellObject($"Ghost_{i}");
                _ghostCellObjects[i].SetActive(false);
            }

            BuildFrame();

            // Wire events
            _board.OnBoardChanged += () => _dirty = true;

            QualityBridge.Register(this);
        }

        private void OnDisable() => QualityBridge.Unregister(this);

        public void OnQualityChanged(QualityTier tier) => _dirty = true;

        private void LateUpdate()
        {
            DecayPieceLight();
            DecayFlashedRenderers();

            // Always update active piece + ghost (they move every frame)
            UpdateActivePiece();
            UpdateGhostPiece();

            if (!_dirty) return;
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
                        SetCellColor(go, Tetrominos.Colors[val - 1]);
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
                    SetCellColor(go, color);
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
            ghostColor.a = 0.25f;

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
                    SetCellColor(go, ghostColor);
                }
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

        private void SetCellColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var mat = renderer.material;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;
        }

        // ═══════════════════════════════════════════════════════════════
        // GLOW / FLASH API — called by TetrisBootstrap event wiring
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Create a point light that follows the active piece.</summary>
        public void CreatePieceLight()
        {
            if (_pieceLight != null) return;
            var lightGO = new GameObject("PieceGlow");
            lightGO.transform.SetParent(transform, false);
            _pieceLight = lightGO.AddComponent<Light>();
            _pieceLight.type = LightType.Point;
            _pieceLight.range = 3f;
            _pieceLight.intensity = PieceLightBaseIntensity;
            _pieceLight.color = new Color(0f, 1f, 1f);
            _pieceLight.shadows = LightShadows.None;
        }

        /// <summary>Flash the piece light to a high intensity + color.</summary>
        public void FlashPieceLight(float intensity, Color color)
        {
            if (_pieceLight == null) return;
            _pieceLight.intensity = intensity;
            _pieceLight.color = color;
            _pieceLight.range = 3f + intensity * 0.4f;
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

        private void DecayPieceLight()
        {
            if (_pieceLight == null) return;
            float decay = Mathf.Clamp01(PieceLightDecay * Time.unscaledDeltaTime);
            _pieceLight.intensity = Mathf.Lerp(_pieceLight.intensity, PieceLightBaseIntensity, decay);
            _pieceLight.color = Color.Lerp(_pieceLight.color, new Color(0f, 1f, 1f), decay);
            _pieceLight.range = Mathf.Lerp(_pieceLight.range, 3f, decay);

            // Move light to active piece position
            if (_match?.ActivePiece != null)
            {
                int r = _match.ActivePiece.PivotRow;
                int c = _match.ActivePiece.PivotCol;
                _pieceLight.transform.localPosition = CellToWorld(r, c) + new Vector3(0, 0, -CellSize);
            }
        }

        private void DecayFlashedRenderers()
        {
            float decay = Mathf.Clamp01(PieceLightDecay * Time.unscaledDeltaTime);
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
        // FRAME / BORDER
        // ═══════════════════════════════════════════════════════════════

        private void BuildFrame()
        {
            if (_frameObject != null) Destroy(_frameObject);

            _frameObject = new GameObject("Frame");
            _frameObject.transform.SetParent(transform, false);

            float boardW = TetrisBoard.Width * CellSize;
            float boardH = TetrisBoard.Height * CellSize;
            float thickness = CellSize * 0.15f;
            Color frameColor = new Color(0.3f, 0.3f, 0.4f);

            // Left wall
            CreateWall(_frameObject.transform, "Left",
                new Vector3(-thickness * 0.5f, boardH * 0.5f, 0f),
                new Vector3(thickness, boardH + thickness * 2, CellSize),
                frameColor);

            // Right wall
            CreateWall(_frameObject.transform, "Right",
                new Vector3(boardW + thickness * 0.5f, boardH * 0.5f, 0f),
                new Vector3(thickness, boardH + thickness * 2, CellSize),
                frameColor);

            // Bottom wall
            CreateWall(_frameObject.transform, "Bottom",
                new Vector3(boardW * 0.5f, -thickness * 0.5f, 0f),
                new Vector3(boardW + thickness * 2, thickness, CellSize),
                frameColor);

            // Floor (dark background behind cells)
            Color floorColor = new Color(0.02f, 0.02f, 0.05f);
            CreateWall(_frameObject.transform, "Floor",
                new Vector3(boardW * 0.5f, boardH * 0.5f, CellSize * 0.5f),
                new Vector3(boardW, boardH, CellSize * 0.1f),
                floorColor);
        }

        private void CreateWall(Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            SetCellColor(go, color);
        }
    }
}
