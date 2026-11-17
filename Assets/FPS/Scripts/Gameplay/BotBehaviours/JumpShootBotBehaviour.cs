using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public class JuanBotBehaviour : BotBehaviour
    {
        [Header("Cycle Timings")]
        [SerializeField] private float m_MovementPhaseDuration = 3.0f; // Walk, jump, and shoot phase
        [SerializeField] private float m_RotationPhaseDuration = 0.2f; // Time spent turning 90 degrees

        [Header("Combat & Jump Settings")]
        [SerializeField] private float m_JumpInterval = 1.0f;          // Jump every 1 second
        [SerializeField] private float m_ShootHoldDuration = 0.5f;     // Shoot for 0.5 seconds after jumping
        [SerializeField] private float m_TurnSpeed = 0.01f;             // Look sensitivity/speed for turning

        // Timers & State Flags
        private float m_PhaseTimer;
        private float m_JumpTimer;
        private float m_ShootHoldTimer;
        private bool m_IsShooting;
        private bool m_WishJump;
        private bool m_IsRotating;

        public override void Initialize(PlayerCharacterController controller)
        {
            m_PhaseTimer = 0f;
            m_JumpTimer = 0f;
            m_ShootHoldTimer = 0f;
            m_IsShooting = false;
            m_WishJump = false;
            m_IsRotating = false;

            Debug.Log("[JumpShootBot] Initialized! Moving for 3s (jumping every 1s + shooting 0.5s), then turning.");
        }

        public override void OnBotUpdate()
        {
            m_PhaseTimer += Time.deltaTime;

            // --- PHASE 1: MOVE, JUMP & SHOOT (0.0s to 3.0s) ---
            if (m_PhaseTimer < m_MovementPhaseDuration)
            {
                m_IsRotating = false;
                m_JumpTimer += Time.deltaTime;

                // Trigger a jump and shoot sequence every 1 second
                if (m_JumpTimer >= m_JumpInterval)
                {
                    m_WishJump = true;
                    m_JumpTimer = 0f; // Reset jump interval clock

                    // Trigger immediate shooting sequence alongside the jump
                    m_IsShooting = true;
                    m_ShootHoldTimer = 0f;
                    Debug.Log("[JumpShootBot] JUMP & SHOOT action triggered!");
                }

                // Handle active shooting duration
                if (m_IsShooting)
                {
                    m_ShootHoldTimer += Time.deltaTime;
                    if (m_ShootHoldTimer >= m_ShootHoldDuration)
                    {
                        m_IsShooting = false;
                    }
                }
            }
            // --- PHASE 2: ROTATION (~90 Degrees) (3.0s to 3.5s) ---
            else if (m_PhaseTimer < (m_MovementPhaseDuration + m_RotationPhaseDuration))
            {
                m_IsRotating = true;
                m_IsShooting = false; // Stop shooting while turning
            }
            // --- CYCLE RESET ---
            else
            {
                m_PhaseTimer = 0f;
                m_JumpTimer = 0f;
                m_IsRotating = false;
                Debug.Log("[JumpShootBot] Cycle reset! Restarting forward movement.");
            }
        }

        // --- MOVEMENT INPUT ---
        public override Vector3 GetMoveInput()
        {
            // Move forward only during the movement phase
            if (!m_IsRotating)
            {
                return Vector3.forward;
            }
            return Vector3.zero;
        }

        // --- LOOK / ROTATION INPUT ---
        public override Vector2 GetLookInputs()
        {
            // Apply horizontal turn input only during the rotation phase
            if (m_IsRotating)
            {
                // Returns (X = Horizontal turn, Y = Vertical pitch)
                return new Vector2(m_TurnSpeed, 0f);
            }
            return Vector2.zero;
        }

        // --- JUMP INPUTS ---
        public override bool GetJumpInputDown()
        {
            if (m_WishJump)
            {
                m_WishJump = false; // Consumed
                return true;
            }
            return false;
        }

        public override bool GetJumpInputHeld() => false;

        // --- FIRE INPUTS ---
        public override bool GetFireInputDown()
        {
            // Start firing on the frame the shooting sequence begins
            if (m_IsShooting && m_ShootHoldTimer < Time.deltaTime)
            {
                return true;
            }
            return false;
        }

        public override bool GetFireInputHeld()
        {
            // Hold trigger down during the shooting window
            return m_IsShooting && m_ShootHoldTimer < m_ShootHoldDuration;
        }

        public override bool GetFireInputReleased()
        {
            // Release trigger when the timer expires
            if (!m_IsShooting && m_ShootHoldTimer >= m_ShootHoldDuration && m_ShootHoldTimer > 0)
            {
                return true;
            }
            return false;
        }

        // --- UNUSED INPUTS ---
        public override bool GetAimInputHeld() => false;
        public override bool GetSprintInputHeld() => false;
        public override bool GetCrouchInputDown() => false;
        public override bool GetCrouchInputReleased() => false;
        public override bool GetReloadButtonDown() => false;
        public override int GetSwitchWeaponInput() => 0;
        public override int GetSelectWeaponInput() => 0;
    }
}