// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;
using CodeGamified.Quality;

namespace Tetris.Game
{
    /// <summary>
    /// Procedural block trail — spawns fading cubes at piece lock / hard drop positions.
    ///  • Ring buffer of small cubes that pulse HDR color and shrink over lifetime.
    ///  • Quality-responsive: more trail segments at higher quality tiers.
    /// </summary>
    public class TetrisBlockTrail : MonoBehaviour, IQualityResponsive
    {
        private int _trailLength;
        private Transform[] _trailParts;
        private Renderer[] _trailRenderers;
        private float[] _trailTimers;
        private Color[] _trailBaseColors;
        private int _writeIndex;

        private const float TRAIL_LIFETIME = 0.6f;

        public void Initialize()
        {
            _trailLength = QualityHints.TrailSegments(QualityBridge.CurrentTier);
            _trailLength = Mathf.Clamp(_trailLength, 16, 512);
            Build();
        }

        private void OnEnable()  => QualityBridge.Register(this);
        private void OnDisable() => QualityBridge.Unregister(this);

        public void OnQualityChanged(QualityTier tier)
        {
            int newLength = Mathf.Clamp(QualityHints.TrailSegments(tier), 16, 512);
            if (newLength == _trailLength) return;
            _trailLength = newLength;
            Cleanup();
            Build();
        }

        private void Build()
        {
            _trailParts = new Transform[_trailLength];
            _trailRenderers = new Renderer[_trailLength];
            _trailTimers = new float[_trailLength];
            _trailBaseColors = new Color[_trailLength];
            _writeIndex = 0;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");

            for (int i = 0; i < _trailLength; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"BlockTrail_{i}";
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * (TetrisBoardRenderer.CellSize * 0.4f);
                go.SetActive(false);

                var collider = go.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                var r = go.GetComponent<Renderer>();
                r.material = new Material(shader);

                _trailParts[i] = go.transform;
                _trailRenderers[i] = r;
                _trailTimers[i] = -1f;
            }
        }

        private void Cleanup()
        {
            if (_trailParts == null) return;
            for (int i = 0; i < _trailParts.Length; i++)
                if (_trailParts[i] != null)
                    Destroy(_trailParts[i].gameObject);
            _trailParts = null;
            _trailRenderers = null;
            _trailTimers = null;
            _trailBaseColors = null;
            _writeIndex = 0;
        }

        private void Update()
        {
            if (_trailParts == null) return;

            float dt = Time.deltaTime;
            for (int i = 0; i < _trailLength; i++)
            {
                if (_trailTimers[i] < 0f) continue;
                _trailTimers[i] += dt;

                float t = _trailTimers[i] / TRAIL_LIFETIME;
                if (t >= 1f)
                {
                    _trailParts[i].gameObject.SetActive(false);
                    _trailTimers[i] = -1f;
                    continue;
                }

                float fade = 1f - t;
                float scale = TetrisBoardRenderer.CellSize * 0.4f * fade;
                _trailParts[i].localScale = Vector3.one * scale;

                Color hdr = _trailBaseColors[i] * (fade * 3f);
                SetHDRColorMat(_trailRenderers[i].material, hdr);
            }
        }

        /// <summary>
        /// Spawn trail cubes at the given world positions with piece color.
        /// Called when a piece locks or hard-drops.
        /// </summary>
        public void SpawnTrailAt(Vector3[] positions, Color pieceColor)
        {
            if (_trailParts == null) return;

            for (int i = 0; i < positions.Length; i++)
            {
                var part = _trailParts[_writeIndex];
                part.position = positions[i];
                part.localScale = Vector3.one * (TetrisBoardRenderer.CellSize * 0.4f);
                part.gameObject.SetActive(true);

                _trailBaseColors[_writeIndex] = pieceColor;
                _trailTimers[_writeIndex] = 0f;

                Color hdr = pieceColor * 3f;
                SetHDRColorMat(_trailRenderers[_writeIndex].material, hdr);

                _writeIndex = (_writeIndex + 1) % _trailLength;
            }
        }

        public void ClearTrail()
        {
            if (_trailParts == null) return;
            for (int i = 0; i < _trailLength; i++)
            {
                _trailParts[i].gameObject.SetActive(false);
                _trailTimers[i] = -1f;
            }
            _writeIndex = 0;
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
    }
}
