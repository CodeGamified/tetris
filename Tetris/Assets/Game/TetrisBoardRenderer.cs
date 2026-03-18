// Copyright CodeGamified 2025-2026
// MIT License — Tetris
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
