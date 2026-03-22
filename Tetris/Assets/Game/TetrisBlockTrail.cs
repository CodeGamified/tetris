// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CodeGamified.Quality;
using CodeGamified.Time;

namespace Tetris.Game
{
    /// <summary>
    /// Drop-path trail — LineRenderer traces the active piece's descent path.
    /// Resets when the piece locks or a new piece spawns.
    /// HDR emissive line that bloom picks up (same aesthetic as Pool/Pong ball trails).
    /// </summary>
    public class TetrisBlockTrail : MonoBehaviour, IQualityResponsive
    {
        private LineRenderer _line;
        private Material _lineMaterial;
        private List<Vector3> _points;
        private TetrisMatchManager _match;
        private TetrisBoardRenderer _renderer;
        private Color _currentColor;
        private bool _active;

        private Coroutine _fadeCoroutine;
        private const float FADE_DURATION = 0.3f;
        private const float MIN_POINT_DIST_SQR = 0.005f;
        private const float LINE_START_WIDTH = 0.10f;
        private const float LINE_END_WIDTH = 0.03f;

        public void Initialize(TetrisMatchManager match, TetrisBoardRenderer renderer)
        {
            _match = match;
            _renderer = renderer;
            _points = new List<Vector3>(128);
            Build();
        }

        private void OnEnable()  => QualityBridge.Register(this);
        private void OnDisable() => QualityBridge.Unregister(this);

        public void OnQualityChanged(QualityTier tier) { }

        private void Build()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit");
            _lineMaterial = new Material(shader);
            _lineMaterial.SetFloat("_Surface", 0);
            _lineMaterial.SetColor("_BaseColor", Color.white);

            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 0;
            _line.startWidth = LINE_START_WIDTH;
            _line.endWidth = LINE_END_WIDTH;
            _line.useWorldSpace = true;
            _line.numCornerVertices = 3;
            _line.numCapVertices = 3;
            _line.material = new Material(_lineMaterial);

            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            _line.colorGradient = grad;
        }

        private void Update()
        {
            if (!_active || _match == null || _renderer == null) return;
            if (_match.ActivePiece == null || _match.GameOver)
            {
                _active = false;
                return;
            }

            Vector3 pieceWorld = GetPieceWorldCenter();

            if (_points.Count == 0 ||
                Vector3.SqrMagnitude(pieceWorld - _points[_points.Count - 1]) > MIN_POINT_DIST_SQR)
            {
                _points.Add(pieceWorld);
                _line.positionCount = _points.Count;
                _line.SetPosition(_points.Count - 1, pieceWorld);
            }
        }

        private Vector3 GetPieceWorldCenter()
        {
            var piece = _match.ActivePiece;
            Vector3 boardOrigin = _renderer.BoardOrigin;
            return boardOrigin + new Vector3(
                piece.PivotCol * TetrisBoardRenderer.CellSize + TetrisBoardRenderer.CellSize * 0.5f,
                piece.PivotRow * TetrisBoardRenderer.CellSize + TetrisBoardRenderer.CellSize * 0.5f,
                -0.05f);
        }

        /// <summary>Start tracking a new piece (called on piece spawn).</summary>
        public void BeginTrail(Color pieceColor)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            _currentColor = pieceColor;
            _points.Clear();
            _line.positionCount = 0;

            Color hdr = pieceColor * 2.5f;
            var mat = _line.material;
            mat.SetColor("_BaseColor", hdr);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", hdr);
            }

            _active = true;
        }

        /// <summary>End the trail (called on piece lock). Fades out then clears.</summary>
        public void EndTrail()
        {
            _active = false;
            float scale = SimulationTime.Instance?.timeScale ?? 1f;
            if (scale < 10f && _points.Count > 1)
            {
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(FadeOut());
            }
            else
            {
                ClearImmediate();
            }
        }

        public void ClearTrail()
        {
            _active = false;
            ClearImmediate();
        }

        private void ClearImmediate()
        {
            if (_fadeCoroutine != null) { StopCoroutine(_fadeCoroutine); _fadeCoroutine = null; }
            _points?.Clear();
            if (_line != null) _line.positionCount = 0;
        }

        private IEnumerator FadeOut()
        {
            Color originalBase = _line.material.HasProperty("_BaseColor")
                ? _line.material.GetColor("_BaseColor")
                : Color.white;

            float elapsed = 0f;
            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / FADE_DURATION;
                Color faded = Color.Lerp(originalBase, Color.black, t);
                var mat = _line.material;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", faded);
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", faded);
                yield return null;
            }

            ClearImmediate();
            _fadeCoroutine = null;
        }
    }
}
