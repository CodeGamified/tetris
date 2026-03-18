// Copyright CodeGamified 2025-2026
// MIT License — Tetris
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tetris.Scripting
{
    /// <summary>
    /// Captures keyboard/gamepad input for use by Tetris player scripts.
    /// Encodes current input as a single float readable by the bytecode engine:
    ///   1 = left, 2 = right, 3 = soft drop, 4 = hard drop,
    ///   5 = rotate CW, 6 = rotate CCW, 7 = hold, 0 = none
    /// </summary>
    public class TetrisInputProvider : MonoBehaviour
    {
        public static TetrisInputProvider Instance { get; private set; }

        // Input codes (match documentation for player scripts)
        public const float INPUT_NONE      = 0f;
        public const float INPUT_LEFT      = 1f;
        public const float INPUT_RIGHT     = 2f;
        public const float INPUT_SOFT_DROP = 3f;
        public const float INPUT_HARD_DROP = 4f;
        public const float INPUT_ROTATE_CW = 5f;
        public const float INPUT_ROTATE_CCW = 6f;
        public const float INPUT_HOLD      = 7f;

        /// <summary>Current input code this frame.</summary>
        public float CurrentInput { get; private set; }

        private InputAction _leftAction;
        private InputAction _rightAction;
        private InputAction _softDropAction;
        private InputAction _hardDropAction;
        private InputAction _rotateCWAction;
        private InputAction _rotateCCWAction;
        private InputAction _holdAction;

        private void Awake()
        {
            Instance = this;

            _leftAction = new InputAction("Left", InputActionType.Button);
            _leftAction.AddBinding("<Keyboard>/leftArrow");
            _leftAction.AddBinding("<Keyboard>/a");
            _leftAction.Enable();

            _rightAction = new InputAction("Right", InputActionType.Button);
            _rightAction.AddBinding("<Keyboard>/rightArrow");
            _rightAction.AddBinding("<Keyboard>/d");
            _rightAction.Enable();

            _softDropAction = new InputAction("SoftDrop", InputActionType.Button);
            _softDropAction.AddBinding("<Keyboard>/downArrow");
            _softDropAction.AddBinding("<Keyboard>/s");
            _softDropAction.Enable();

            _hardDropAction = new InputAction("HardDrop", InputActionType.Button);
            _hardDropAction.AddBinding("<Keyboard>/space");
            _hardDropAction.Enable();

            _rotateCWAction = new InputAction("RotateCW", InputActionType.Button);
            _rotateCWAction.AddBinding("<Keyboard>/upArrow");
            _rotateCWAction.AddBinding("<Keyboard>/x");
            _rotateCWAction.Enable();

            _rotateCCWAction = new InputAction("RotateCCW", InputActionType.Button);
            _rotateCCWAction.AddBinding("<Keyboard>/z");
            _rotateCCWAction.AddBinding("<Keyboard>/leftCtrl");
            _rotateCCWAction.Enable();

            _holdAction = new InputAction("Hold", InputActionType.Button);
            _holdAction.AddBinding("<Keyboard>/c");
            _holdAction.AddBinding("<Keyboard>/leftShift");
            _holdAction.Enable();
        }

        private void Update()
        {
            // Priority: hard drop > rotate > hold > horizontal > soft drop
            if (_hardDropAction.WasPressedThisFrame())
                CurrentInput = INPUT_HARD_DROP;
            else if (_rotateCWAction.WasPressedThisFrame())
                CurrentInput = INPUT_ROTATE_CW;
            else if (_rotateCCWAction.WasPressedThisFrame())
                CurrentInput = INPUT_ROTATE_CCW;
            else if (_holdAction.WasPressedThisFrame())
                CurrentInput = INPUT_HOLD;
            else if (_leftAction.WasPressedThisFrame())
                CurrentInput = INPUT_LEFT;
            else if (_rightAction.WasPressedThisFrame())
                CurrentInput = INPUT_RIGHT;
            else if (_softDropAction.IsPressed())
                CurrentInput = INPUT_SOFT_DROP;
            else
                CurrentInput = INPUT_NONE;
        }

        private void OnDestroy()
        {
            _leftAction?.Disable(); _leftAction?.Dispose();
            _rightAction?.Disable(); _rightAction?.Dispose();
            _softDropAction?.Disable(); _softDropAction?.Dispose();
            _hardDropAction?.Disable(); _hardDropAction?.Dispose();
            _rotateCWAction?.Disable(); _rotateCWAction?.Dispose();
            _rotateCCWAction?.Disable(); _rotateCCWAction?.Dispose();
            _holdAction?.Disable(); _holdAction?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
