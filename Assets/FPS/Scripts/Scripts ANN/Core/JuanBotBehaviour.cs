using System;
using System.Collections.Generic;
using Unity.FPS.Core;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;


namespace Coursework.Core
{
    public class JuanBotBehaviour : BotBehaviour
    {
        //////////*************************************
        // VARIABLES
        private Vector3 startPosition, startRotation;
        private NeuralNetwork network;
        private GameObject[] guardarPickups; // Para respawnear pickups
        private struct EnemyState
        { // Capturar estado de los enemigos
            public GameObject gameObject;
            public Health healthComponent;
            public Vector3 startPosition;
            public Quaternion startRotation;
            public bool isDead;
        }
        private EnemyState[] guardarEnemigos; // Para respawnear enemigos
        private Health Health; // Para ver su vida
        private PlayerWeaponsManager weaponsManager; // Para poder usar armas
        private WeaponController currentWeapon; // El arma que esté usando

        public int startHealth = 50;

        //NUEVO
        public bool TouchFinish = false;

        private bool eliteCheck = false;





        private float m_NextActionTime = 0;
        public float NextActionTime_Interval = 2;
        public float MoveSpeed = 0;
        public float TurnSpeed = 0;

        private Vector2 m_LookInput;
        private float lookSharpness = 2;

        private Vector2 targetLookInput;




        [Range(-1f, 1f)]
        public float a, t, f;

        public float timeSinceStart = 0f;

        [Header("Standalone Mode")]
        public bool isStandalone = false;
        public TextAsset standaloneBrainFile; // Arrastrar JSON

        [Header("Fitness")]
        public float overallFitness;
        public float events;

        public float turnPenalty = 0.02f;
        public float maxTurnPenalty = 30f;

        private float currentTurnPenalty = 0f;
        public float distanceStartMultiplier = 0.3f; // Antes era 1.4
        public float distanceMultiplier = 0.3f;
        public float avgSpeedMultiplier = 0.2f;
        [Space(10)]
        public float wallSensorMultiplier = 0.2f;
        public float checkpointSensorsMultiplier = 0.1f;
        public float dangerSensorsMultiplier = 0.4f; // Detectar enemigos
        public float goalSensorMultiplier = 0.1f;
        public float pickupSensorMultiplier = 0.5f; // Pickups
        public float holeSensorMultiplier = 0.5f; // Huecos
        public float targetSensorMultiplier = 1f; // Enemigos en frente


        [Header("Network Options")]
        public int LAYERS = 1; // Ver de cambiar a 2?
        public int NEURONS = 15;
        public LayerMask WallLayer;
        public LayerMask CheckpointLayer;
        public LayerMask EnemyLayer;
        public LayerMask GoalLayer;
        public LayerMask PickupLayer; //Pickups

        [SerializeField] private Transform goal = null;

        public float healthDecay = 1f;
        public float shootPenalty = 2f;
        public bool pickupBonus = false;
        public float pickupRadius = 20f;
        public float dangerRadius = 30f;
        public bool survivalBonus = false;

        private Vector3 lastPosition;

        private Rigidbody rb;
        private float totalDistanceTravelled;
        private float maxDistanceFromStart = 0f;
        private float avgSpeed;

        // SENSORES LISTADO: 15 SENSORES
        private float[] wallSensors = new float[7];

        private float[] dangerSensors = new float[2]; // Era 8, cambia a 2 por distancia y ángulo

        private float[] pickupSensors = new float[2]; // Pickups distancia y ángulo
        private float[] healthSensors = new float[1]; // Cuánta vida le queda
        private float[] holeSensors = new float[2]; // Huecos para que no caiga
        private float[] targetSensors = new float[1]; // Si está apuntando a un enemigo

        // SENSORES SIN USAR
        private float[] goalSensors = new float[1];
        private float[] checkpointSensors = new float[2];








        /// ////////**********************************



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

        public int SENSORS
        {
            get
            {
                int answer = 0;

                if (wallSensorMultiplier > 0) answer += 7;
                if (dangerSensorsMultiplier > 0) answer += 2;
                if (pickupSensorMultiplier > 0) answer += 3; // 2 para buscar pickups, +1 para la vida
                if (holeSensorMultiplier > 0) answer += 2; // Huecos
                if (targetSensorMultiplier > 0) answer += 1; // Apuntar a disparo
                // SIN USAR
                if (goalSensorMultiplier > 0) answer++;
                if (checkpointSensorsMultiplier > 0) answer += 2;

                // Neuronas automáticamente
                NEURONS = answer * 2;

                return answer;
            }
        }

        private float wallSensorAvg
        {
            get
            {
                float answer = 0f;

                foreach (float f in wallSensors)
                {
                    answer += f;
                }

                answer /= wallSensors.Length;
                return answer;
            }
        }

        private float checkpointSensorAvg
        {
            get
            {
                float answer = 0f;

                foreach (float f in checkpointSensors)
                {
                    answer += f;
                }

                answer /= checkpointSensors.Length;
                return answer;
            }
        }



        private float holeSensorAvg
        {
            get
            {
                float answer = 0f;

                foreach (float f in holeSensors)
                {
                    answer += f;
                }

                answer /= holeSensors.Length;
                return answer;
            }
        }

        public void ResetWithNetwork(NeuralNetwork net, bool isElite = false)
        {
            //Health.CurrentHealth = startHealth; // Reiniciar vida a 50 por probar
            network = net;
            eliteCheck = isElite;
            //Reset();
        }


        private void InitializeNetwork()
        {
            startPosition = transform.position;
            startRotation = transform.eulerAngles;
            network = new NeuralNetwork(SENSORS);

            if (isStandalone && standaloneBrainFile != null)
            {
                IndividualRecord brain = JsonUtility.FromJson<IndividualRecord>(standaloneBrainFile.text);
                network.Initialize(LAYERS, NEURONS);
                network.LoadFromRecord(brain);
            }

            // Reiniciar pickups
            guardarPickups = GameObject.FindGameObjectsWithTag("Pickup");

            // Vida
            Health = GetComponent<Health>();
            //healthSensors[0] = Health.CurrentHealth / Health.MaxHealth;
            healthSensors[0] = 1;

            // PlayerWeaponsManager del agente
            weaponsManager = GetComponent<PlayerWeaponsManager>();

            // Reiniciar enemigos
            GameObject[] enemiesInScene = GameObject.FindGameObjectsWithTag("Enemy");
            guardarEnemigos = new EnemyState[enemiesInScene.Length];

            for (int i = 0; i < enemiesInScene.Length; i++)
            {
                guardarEnemigos[i] = new EnemyState
                {
                    gameObject = enemiesInScene[i],
                    healthComponent = enemiesInScene[i].GetComponent<Health>(),
                    startPosition = enemiesInScene[i].transform.position,
                    startRotation = enemiesInScene[i].transform.rotation
                };
            }
        }


        private void InputSensors()
        {
            // WALL SENSORS
            if (wallSensorMultiplier > 0)
            {
                Vector3[] directions = new Vector3[] {
                    transform.forward + (transform.right * 0.6f), // Diagonal derecha
                    transform.forward + (transform.right * 0.25f), // Cerrado derecha
                    transform.forward,  // Centro
                    transform.forward - (transform.right*0.25f), // Cerrado izquierda
                    transform.forward - (transform.right*0.6f), // Diagonal izquierda
                    transform.right, // Bigotono derecha
                    -transform.right // Bigotono izquierda
                };
                Debug.Log("direcciones: " + directions);

                Vector3 origen = transform.position + transform.up / 2;

                for (int i = 0; i < wallSensors.Length; i++)
                {
                    Ray r = new Ray(origen, directions[i]);
                    RaycastHit hit;
                    Color hitColor = Color.green;

                    float lengthRayo = (i >= 5) ? 10f : 18f; // Length de rayos, bigotes de lado 10, resto 18

                    // Disparamos el rayo
                    if (Physics.Raycast(r, out hit, lengthRayo, WallLayer))
                    {
                        wallSensors[i] = Mathf.Clamp01(hit.distance / lengthRayo); // Tolerancia (a distancia, era 20 al inicio)

                        // Rojo si está cerca, Verde si está lejos
                        if (wallSensors[i] < 0.5f)
                            Debug.DrawLine(transform.position + transform.up / 2, hit.point, Color.red);
                        else
                            Debug.DrawLine(transform.position + transform.up / 2, hit.point, Color.green);
                    }
                    else
                    {
                        // Si no choca con nada, el camino es 100% seguro.
                        wallSensors[i] = 1f;

                        // Dibujamos el rayo al máximo alcance para visualizarlo nosotros
                        Debug.DrawRay(origen, directions[i] * lengthRayo, Color.green);
                    }
                }
            }

            // GOAL SENSOR
            if (goalSensorMultiplier > 0)
            {
                Ray goalRay = new Ray(transform.position, goal.position - transform.position);
                RaycastHit goalHit;
                Color goalHitColor = Color.blue;

                if (Physics.Raycast(goalRay, out goalHit, 300f, GoalLayer))
                {
                    //goalSensors[0] = 1/goalHit.distance;
                    goalSensors[0] = Mathf.Clamp01(1f - (goalHit.distance / 300f)); // Suavizado, con clamp
                    Debug.DrawLine(goalRay.origin, goalHit.point, goalHitColor);
                }
                else
                {
                    goalSensors[0] = 0; //No lo encuentra
                }
            }


            // CHECKPOINT SENSORS
            if (checkpointSensorsMultiplier > 0)
            {
                Vector3[] checkpointDirections = new Vector3[] {
                    transform.forward + transform.right * 0.05f,
                    transform.forward - transform.right * 0.05f
                };

                for (int i = 0; i < checkpointSensors.Length; i++)
                {
                    Ray r = new Ray(transform.position, checkpointDirections[i]);
                    RaycastHit hit;

                    if (Physics.Raycast(r, out hit, 10f, CheckpointLayer))
                    {
                        checkpointSensors[i] = 1 / hit.distance;

                        Debug.DrawLine(r.origin, hit.point, Color.blue);
                    }
                    else checkpointSensors[i] = 0f;
                }
            }


            // DANGER SENSORS
            if (dangerSensorsMultiplier > 0)
            {
                // Enemigos dentro del área
                Collider[] enemiesInRange = Physics.OverlapSphere(transform.position, dangerRadius, EnemyLayer);

                Transform closestVisibleEnemy = null;
                float minDistance = Mathf.Infinity;

                if (enemiesInRange.Length > 0)
                {
                    Vector3 origenRayo = transform.position + transform.up / 2f;

                    foreach (Collider enemy in enemiesInRange)
                    {
                        Vector3 destinoRayo = enemy.transform.position + transform.up / 2f;
                        Vector3 directionToEnemy = destinoRayo - origenRayo;
                        float distance = directionToEnemy.magnitude;

                        // No hay pared en medio
                        if (!Physics.Raycast(origenRayo, directionToEnemy.normalized, distance, WallLayer))
                        {
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                closestVisibleEnemy = enemy.transform;
                            }
                        }
                    }
                }

                // distancia + ángulo si no lo tapa una pared
                if (closestVisibleEnemy != null)
                {
                    // distancia (1 más cerca, 0 más lejos)
                    dangerSensors[0] = Mathf.Clamp01(1f - (minDistance / dangerRadius));

                    // ángulo de -1 izq a 1 der
                    Vector3 directionToEnemy = (closestVisibleEnemy.position - transform.position).normalized;
                    float angle = Vector3.SignedAngle(transform.forward, directionToEnemy, Vector3.up);
                    dangerSensors[1] = angle / 180f;

                    // mantiene la línea amarilla de antes
                    Debug.DrawLine(transform.position + transform.up / 2f, closestVisibleEnemy.position, Color.yellow);
                }
                else
                {
                    // si no hay enemigos 0 a todo
                    dangerSensors[0] = 0f;
                    dangerSensors[1] = 0f;
                }
            }

            // PICKUP SENSORS
            if (pickupSensorMultiplier > 0)
            {
                // Pickups en la esfera
                Collider[] pickupsInRange = Physics.OverlapSphere(transform.position, pickupRadius, PickupLayer);

                Transform closestVisiblePickup = null;
                float minDistance = Mathf.Infinity;

                if (pickupsInRange.Length > 0)
                {
                    // Origen
                    Vector3 origenRayo = transform.position + transform.up / 2f;

                    foreach (Collider pickup in pickupsInRange)
                    {
                        Vector3 destinoRayo = pickup.transform.position + transform.up / 2f;
                        Vector3 directionToPickup = destinoRayo - origenRayo;
                        float distance = directionToPickup.magnitude;

                        // Si raycast no choca con pared
                        if (!Physics.Raycast(origenRayo, directionToPickup.normalized, distance, WallLayer))
                        {
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                closestVisiblePickup = pickup.transform;
                            }
                        }
                    }
                }

                // Distancia + ángulo sólo si es visible
                if (closestVisiblePickup != null)
                {
                    pickupSensors[0] = 1f - (minDistance / pickupRadius);

                    Vector3 directionToPickup = (closestVisiblePickup.position - transform.position).normalized;
                    float angle = Vector3.SignedAngle(transform.forward, directionToPickup, Vector3.up);
                    pickupSensors[1] = angle / 180f;

                    // Línea cyan solo cuando realmente lo está viendo
                    Debug.DrawLine(transform.position + transform.up / 2f, closestVisiblePickup.position, Color.cyan);
                }
                else
                {
                    // 0 en ambos, ignora esta entrada porque hay una pared
                    pickupSensors[0] = 0f;
                    pickupSensors[1] = 0f;
                }
            }

            // HOLE SENSORS
            if (holeSensorMultiplier > 0)
            {
                Vector3 origen = transform.position + (1.5f * transform.up); // nivel de los ojos

                Vector3[] directions = new Vector3[] {
                    transform.forward - (transform.up * 1.5f),  // 0: Cercano
                    transform.forward - (transform.up / 2f)   // 1: Lejano
                };

                for (int i = 0; i < holeSensors.Length; i++)
                {
                    Ray r = new Ray(origen, directions[i]);
                    RaycastHit hit;

                    if (Physics.Raycast(r, out hit, 10f)) //agregar ,Layer si fuese necesario
                    {
                        // Suelo: Camino seguro.
                        holeSensors[i] = 1f;
                        Debug.DrawLine(origen, hit.point, Color.blue);
                    }
                    else
                    {
                        // Precipicio: Silencio neuronal (0).
                        holeSensors[i] = 0f;
                        Debug.DrawRay(origen, directions[i] * 10f, Color.red);
                    }
                }
            }

            if (targetSensorMultiplier > 0)
            {
                targetSensors[0] = 0f;
                if (currentWeapon != null && currentWeapon.WeaponMuzzle != null)
                {
                    LayerMask maskWallyTarget = WallLayer | EnemyLayer; // El rayo le da o a la pared o al enemigo
                    Vector3 origenApunta = new Vector3( // Origen distinto porque el arma puede atravesar paredes
                        transform.position.x,
                        currentWeapon.WeaponMuzzle.position.y,
                        transform.position.z
                    );
                    Ray r = new Ray(origenApunta, currentWeapon.WeaponMuzzle.forward);
                    RaycastHit hit;

                    if (Physics.Raycast(r, out hit, 100f, maskWallyTarget))
                    {
                        if (hit.collider.CompareTag("Enemy"))
                        {
                            targetSensors[0] = 1f;
                            Debug.DrawLine(r.origin, hit.point, Color.magenta);
                        }
                        else
                        {
                            Debug.DrawLine(r.origin, hit.point, Color.gray);
                        }

                    }
                }
            }
        }

        private float[] CombineArrays(float[] arr1, float[] arr2)
        {
            var answer = new List<float>();

            foreach (float f in arr1)
            {
                answer.Add(f);
            }

            foreach (float f in arr2)
            {
                answer.Add(f);
            }

            return answer.ToArray();
        }





        public override void Initialize(PlayerCharacterController controller)
        {
            m_PhaseTimer = 0f;
            m_JumpTimer = 0f;
            m_ShootHoldTimer = 0f;
            m_IsShooting = false;
            m_WishJump = false;
            m_IsRotating = false;

            InitializeNetwork();

            Debug.Log("[JumpShootBot] Initialized! Moving for 3s (jumping every 1s + shooting 0.5s), then turning.");
        }



        private void UpdateNetwork()
        {
            InputSensors();

            float[] finalArray = new float[0];

            if (wallSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, wallSensors).Clone();
            if (checkpointSensorsMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, checkpointSensors).Clone();
            if (dangerSensorsMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, dangerSensors).Clone();
            if (goalSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, goalSensors).Clone();
            if (pickupSensorMultiplier > 0)
            {
                finalArray = (float[])CombineArrays(finalArray, pickupSensors).Clone(); // Pickups
                //healthSensors[0] = Health.CurrentHealth / Health.MaxHealth; // Vida
                healthSensors[0] = 1;
                finalArray = (float[])CombineArrays(finalArray, healthSensors).Clone(); // Vida 
            }
            if (holeSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, holeSensors).Clone(); // Huecos
            if (targetSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, targetSensors).Clone(); // Apuntar enemigos


            if (finalArray.Length <= 0)
            {
                Debug.LogError("NetworkController::FixedUpdate() --- No sensors available. Please check the multipliers.");
                return;
            }


           

            if (Time.time >= m_NextActionTime)
            {
                

                (a, t, f) = network.RunNetwork(finalArray);
               
            }
            
        }




        public override void OnBotUpdate()
        {
            m_PhaseTimer += Time.deltaTime;



            UpdateNetwork();
            //UpdateLookInput();




            // --- PHASE 1: MOVE, JUMP & SHOOT (0.0s to 3.0s) ---
            if (m_PhaseTimer < m_MovementPhaseDuration)
            {
                m_IsRotating = false;
                m_JumpTimer += Time.deltaTime;
                Debug.Log("m_JumpTimer " + m_JumpTimer);


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
            //float forwardMove = Mathf.Max(0f, a); // Solo permitimos avanzar o frenar
            
            // Move forward only during the movement phase
            if (!m_IsRotating)
            {
                //return Vector3.forward;
                
                return new Vector3(0f, 0f, a * MoveSpeed);
            }
            //return Vector3.zero;
            return new Vector3(0f, 0f, a * MoveSpeed);
        }

        // --- LOOK / ROTATION INPUT ---

        
        private void UpdateLookInput()
        {
            targetLookInput = new Vector2(t, 0f);
            // Suaviza el giro desde el valor actual hacia el objetivo
            
        }


        public override Vector2 GetLookInputs()
        {

            
            // Apply horizontal turn input only during the rotation phase
            if (m_IsRotating)
            {
                //return m_LookInput = Vector2.Lerp(m_LookInput, targetLookInput, Time.deltaTime * lookSharpness);
                // Returns (X = Horizontal turn, Y = Vertical pitch)
                //return new Vector2(m_TurnSpeed, 0f);
                return new Vector2(t * Time.deltaTime * TurnSpeed , 0f);
                

            }
            //return Vector2.zero;
            return new Vector2(t * Time.deltaTime * TurnSpeed, 0f);
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
                //return true;
                // Return true on the first frame of shooting
                if (f >= 0.81)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        public override bool GetFireInputHeld()
        {
            // Hold trigger down during the shooting window
            //return m_IsShooting && m_ShootHoldTimer < m_ShootHoldDuration;
            if (f >= 0.81)
            {
                return true;
            }
            return false;
        }

        public override bool GetFireInputReleased()
        {
            // Release trigger when the timer expires
            if (!m_IsShooting && m_ShootHoldTimer >= m_ShootHoldDuration && m_ShootHoldTimer > 0)
            {
                //return true;
                if (f < 0.81)
                {
                    return true;
                }
                return false;
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