// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;
using CodeGamified.Time;

namespace Tetris.Game
{
    /// <summary>
    /// Match manager — gravity, spawning, scoring, level progression, game over.
    /// The player's CODE controls piece placement. This drives the clock.
    ///
    /// Gravity model: piece falls one row every `dropInterval` sim-seconds.
    /// Level increases every 10 lines, speeding up gravity.
    ///
    /// Lock delay: after the piece lands, it lingers for `lockDelay` sim-seconds
    /// before locking, giving the player's code time to slide or rotate.
    /// </summary>
    public class TetrisMatchManager : MonoBehaviour
    {
        private TetrisBoard _board;
        private TetrisPiece _activePiece;

        // Config
        private float _baseDropInterval;
        private float _lockDelay;

        // State
        public int Score { get; private set; }
        public int Level { get; private set; }
        public int LinesTotal { get; private set; }
        public int PiecesPlaced { get; private set; }
        public bool GameOver { get; private set; }
        public bool MatchInProgress { get; private set; }

        // Piece bag (7-bag randomizer)
        private int[] _bag;
        private int _bagIndex;

        // Next piece preview
        public int NextShape { get; private set; }

        // Hold
        public int HeldShape { get; private set; } = -1;
        private bool _holdUsedThisTurn;

        // Gravity timing
        private float _dropTimer;
        private float _lockTimer;
        private bool _isLocking;

        // The active piece reference (accessible for I/O handler)
        public TetrisPiece ActivePiece => _activePiece;
        public TetrisBoard Board => _board;

        // Events
        public System.Action<int, int> OnPointsScored;      // (points, lines)
        public System.Action<int> OnLevelUp;                 // new level
        public System.Action OnGameOver;
        public System.Action OnPieceSpawned;
        public System.Action OnPieceLocked;
        public System.Action<int> OnLinesCleared;            // count
        public System.Action OnHoldUsed;
        public System.Action OnMatchStarted;
        public System.Action<int> OnHardDrop;                // rows dropped

        public void Initialize(TetrisBoard board, float baseDropInterval = 1.0f, float lockDelay = 0.5f)
        {
            _board = board;
            _baseDropInterval = baseDropInterval;
            _lockDelay = lockDelay;
        }

        public void StartMatch()
        {
            _board.Clear();
            Score = 0;
            Level = 1;
            LinesTotal = 0;
            PiecesPlaced = 0;
            GameOver = false;
            MatchInProgress = true;
            HeldShape = -1;
            _holdUsedThisTurn = false;

            ShuffleBag();
            NextShape = DrawFromBag();

            OnMatchStarted?.Invoke();
            SpawnNextPiece();
        }

        private void Update()
        {
            if (!MatchInProgress || GameOver) return;
            if (SimulationTime.Instance == null || SimulationTime.Instance.isPaused) return;

            float dt = Time.deltaTime * (SimulationTime.Instance?.timeScale ?? 1f);
            UpdateGravity(dt);
        }

        private void UpdateGravity(float dt)
        {
            if (_activePiece == null) return;

            if (_isLocking)
            {
                _lockTimer -= dt;
                if (_lockTimer <= 0f)
                {
                    LockActivePiece();
                }
                else if (_activePiece.MoveDown())
                {
                    // Piece moved down off the landing — cancel lock
                    _isLocking = false;
                }
                return;
            }

            _dropTimer -= dt;
            if (_dropTimer <= 0f)
            {
                _dropTimer = CurrentDropInterval;

                if (!_activePiece.MoveDown())
                {
                    // Piece can't fall — start lock delay
                    _isLocking = true;
                    _lockTimer = _lockDelay;
                }
            }
        }

        /// <summary>Current drop interval based on level.</summary>
        public float CurrentDropInterval =>
            Mathf.Max(0.05f, _baseDropInterval * Mathf.Pow(0.8f, Level - 1));

        // ═══════════════════════════════════════════════════════════════
        // PIECE SPAWNING
        // ═══════════════════════════════════════════════════════════════

        private void SpawnNextPiece()
        {
            int shape = NextShape;
            NextShape = DrawFromBag();

            _activePiece = new TetrisPiece(_board);
            if (!_activePiece.Spawn(shape))
            {
                // Can't spawn — game over
                GameOver = true;
                MatchInProgress = false;
                OnGameOver?.Invoke();
                return;
            }

            _dropTimer = CurrentDropInterval;
            _isLocking = false;
            _holdUsedThisTurn = false;
            OnPieceSpawned?.Invoke();
        }

        private void LockActivePiece()
        {
            if (_activePiece == null) return;

            bool inBounds = _activePiece.Lock();
            PiecesPlaced++;
            _isLocking = false;

            OnPieceLocked?.Invoke();

            if (!inBounds)
            {
                // Piece locked above visible area — game over
                GameOver = true;
                MatchInProgress = false;
                OnGameOver?.Invoke();
                return;
            }

            // Clear lines
            int cleared = _board.ClearLines();
            if (cleared > 0)
            {
                LinesTotal += cleared;
                int points = CalculateScore(cleared, Level);
                Score += points;
                OnLinesCleared?.Invoke(cleared);
                OnPointsScored?.Invoke(points, cleared);

                // Level up every 10 lines
                int newLevel = (LinesTotal / 10) + 1;
                if (newLevel > Level)
                {
                    Level = newLevel;
                    OnLevelUp?.Invoke(Level);
                }
            }

            SpawnNextPiece();
        }

        private int CalculateScore(int lines, int level)
        {
            // Classic NES scoring
            return lines switch
            {
                1 => 40 * level,
                2 => 100 * level,
                3 => 300 * level,
                4 => 1200 * level, // Tetris!
                _ => 0
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // PLAYER ACTIONS (called by IOHandler / compiler opcodes)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Move active piece left. Returns true if successful.</summary>
        public bool MoveLeft()
        {
            if (_activePiece == null) return false;
            bool ok = _activePiece.MoveLeft();
            if (ok && _isLocking) _lockTimer = _lockDelay; // reset lock delay on move
            return ok;
        }

        /// <summary>Move active piece right. Returns true if successful.</summary>
        public bool MoveRight()
        {
            if (_activePiece == null) return false;
            bool ok = _activePiece.MoveRight();
            if (ok && _isLocking) _lockTimer = _lockDelay;
            return ok;
        }

        /// <summary>Soft drop — move one row down. Returns true if successful.</summary>
        public bool SoftDrop()
        {
            if (_activePiece == null) return false;
            bool ok = _activePiece.MoveDown();
            if (ok)
            {
                Score += 1; // 1 point per soft drop row
                _dropTimer = CurrentDropInterval;
                if (_isLocking) _isLocking = false;
            }
            return ok;
        }

        /// <summary>Hard drop — instantly lock piece at lowest position.</summary>
        public int DoHardDrop()
        {
            if (_activePiece == null) return 0;
            int rows = _activePiece.HardDrop();
            Score += rows * 2; // 2 points per hard drop row
            OnHardDrop?.Invoke(rows);
            LockActivePiece();
            return rows;
        }

        /// <summary>Rotate active piece clockwise. Returns true if successful.</summary>
        public bool RotateCW()
        {
            if (_activePiece == null) return false;
            bool ok = _activePiece.RotateCW();
            if (ok && _isLocking) _lockTimer = _lockDelay;
            return ok;
        }

        /// <summary>Rotate active piece counter-clockwise. Returns true if successful.</summary>
        public bool RotateCCW()
        {
            if (_activePiece == null) return false;
            bool ok = _activePiece.RotateCCW();
            if (ok && _isLocking) _lockTimer = _lockDelay;
            return ok;
        }

        /// <summary>Hold the current piece. Returns true if successful.</summary>
        public bool Hold()
        {
            if (_activePiece == null || _holdUsedThisTurn) return false;

            int currentShape = _activePiece.Shape;
            _holdUsedThisTurn = true;

            if (HeldShape < 0)
            {
                // First hold — draw next piece
                HeldShape = currentShape;
                SpawnNextPiece();
            }
            else
            {
                // Swap with held
                int swapShape = HeldShape;
                HeldShape = currentShape;

                _activePiece = new TetrisPiece(_board);
                if (!_activePiece.Spawn(swapShape))
                {
                    GameOver = true;
                    MatchInProgress = false;
                    OnGameOver?.Invoke();
                    return false;
                }
                _dropTimer = CurrentDropInterval;
                _isLocking = false;
            }

            OnHoldUsed?.Invoke();
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // 7-BAG RANDOMIZER
        // ═══════════════════════════════════════════════════════════════

        private void ShuffleBag()
        {
            _bag = new int[Tetrominos.ShapeCount];
            for (int i = 0; i < _bag.Length; i++)
                _bag[i] = i;

            // Fisher-Yates shuffle
            for (int i = _bag.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
            }
            _bagIndex = 0;
        }

        private int DrawFromBag()
        {
            if (_bag == null || _bagIndex >= _bag.Length)
                ShuffleBag();
            return _bag[_bagIndex++];
        }
    }
}
