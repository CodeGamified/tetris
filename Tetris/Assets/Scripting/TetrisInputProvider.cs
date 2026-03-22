// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tetris.Scripting
{
    /// <summary>
    /// Captures keyboard/gamepad input for Tetris player scripts.
    /// Pong-style continuous axes — no buffering needed.
    ///   get_input_x() → horizontal: Left/A = -1, Right/D = +1
    ///   get_input_y() → rotation:   Up/W = +1 (CW), Down/S = -1 (CCW)
    ///   get_input_q() → hold:       C/Shift/Q = 1
    ///   get_action()  → drop:       Space = 1
    /// </summary>
    public class TetrisInputProvider : MonoBehaviour
    {
        public static TetrisInputProvider Instance { get; private set; }

        /// <summary>Horizontal axis: -1 (left), 0, +1 (right).</summary>
        public float InputX { get; private set; }

        /// <summary>Rotation axis: +1 (CW), -1 (CCW).</summary>
        public float InputY { get; private set; }

        /// <summary>Hold button: 1 (pressed), 0 (released).</summary>
        public float InputQ { get; private set; }

        /// <summary>Action/drop button: 1 (pressed), 0 (released).</summary>
        public float Action { get; private set; }

        private InputAction _moveXAction;
        private InputAction _rotateYAction;
        private InputAction _holdAction;
        private InputAction _actionAction;

        private void Awake()
        {
            Instance = this;

            _moveXAction = new InputAction("MoveX", InputActionType.Value);
            _moveXAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            _moveXAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            _moveXAction.Enable();

            _rotateYAction = new InputAction("RotateY", InputActionType.Value);
            _rotateYAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/downArrow")
                .With("Positive", "<Keyboard>/upArrow");
            _rotateYAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/s")
                .With("Positive", "<Keyboard>/w");
            _rotateYAction.Enable();

            _holdAction = new InputAction("Hold", InputActionType.Button);
            _holdAction.AddBinding("<Keyboard>/c");
            _holdAction.AddBinding("<Keyboard>/leftShift");
            _holdAction.AddBinding("<Keyboard>/q");
            _holdAction.Enable();

            _actionAction = new InputAction("Action", InputActionType.Button);
            _actionAction.AddBinding("<Keyboard>/space");
            _actionAction.Enable();
        }

        private void Update()
        {
            InputX = _moveXAction.ReadValue<float>();
            InputY = _rotateYAction.ReadValue<float>();
            InputQ = _holdAction.IsPressed() ? 1f : 0f;
            Action = _actionAction.IsPressed() ? 1f : 0f;
        }

        private void OnDestroy()
        {
            _moveXAction?.Disable(); _moveXAction?.Dispose();
            _rotateYAction?.Disable(); _rotateYAction?.Dispose();
            _holdAction?.Disable(); _holdAction?.Dispose();
            _actionAction?.Disable(); _actionAction?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
