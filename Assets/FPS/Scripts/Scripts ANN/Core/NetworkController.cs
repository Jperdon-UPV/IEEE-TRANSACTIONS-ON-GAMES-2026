using System;
using System.Collections.Generic;
using Unity.FPS.Core;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;

namespace Coursework.Core
{
    public class NetworkController : BotBehaviour
    {
        // VARIABLES
        private Vector3 startPosition, startRotation;
        private NeuralNetwork network;
        private GameObject[] guardarPickups; // Para respawnear pickups
        private struct EnemyState { // Capturar estado de los enemigos
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
        private float NextActionTime_Interval = 2;
        private Vector2 m_LookInput;
        private float lookSharpness = 2;






        [Range(-1f,1f)]
        public float a,t,f;

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

        public int SENSORS {
            get {
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

        public int LapsDone { get; set; }
        public void FinishLap() => LapsDone++;

        private float wallSensorAvg {
            get {
                float answer = 0f;

                foreach (float f in wallSensors) {
                    answer += f;
                }

                answer /= wallSensors.Length;
                return answer;
            }
        }

        private float checkpointSensorAvg {
            get {
                float answer = 0f;

                foreach (float f in checkpointSensors) {
                    answer += f;
                }

                answer /= checkpointSensors.Length;
                return answer;
            }
        }

        

        private float holeSensorAvg {
            get {
                float answer = 0f;

                foreach (float f in holeSensors) {
                    answer += f;
                }

                answer /= holeSensors.Length;
                return answer;
            }
        }

        

        // EXECUTION FUNCTIONS
        public override void Initialize(PlayerCharacterController controller) 
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
                guardarEnemigos[i] = new EnemyState {
                    gameObject = enemiesInScene[i],
                    healthComponent = enemiesInScene[i].GetComponent<Health>(),
                    startPosition = enemiesInScene[i].transform.position,
                    startRotation = enemiesInScene[i].transform.rotation
                };
            }
        }

        private void OnCollisionEnter (Collision collision) {
            if (isStandalone) { return; } // Si no es parte del entrenamiento, no pasa nada por chocarse
            //if (timeSinceStart < 0.5f) return; // Escudo al spawnear
            if (collision.gameObject.CompareTag("Wall"))
            {
                //if(collision.gameObject.layer == WallLayer) A ver si se tiene que cambiar a esto?
                //overallFitness -= 40f;
                //Death();
            }
            // Descomentar cuando esté tratando con enemigos y una meta
            if (collision.gameObject.CompareTag("Enemy"))
            {
                //Death();
            }
        }

        private void OnTriggerEnter(Collider other) {

      
        }

        // FixedUpdate para actualizar con física
        public override void OnBotUpdate() 
        {
            timeSinceStart += Time.deltaTime;

            InputSensors();

            float[] finalArray = new float[0];

            if (wallSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, wallSensors).Clone();
            if (checkpointSensorsMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, checkpointSensors).Clone();
            if (dangerSensorsMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, dangerSensors).Clone();
            if (goalSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, goalSensors).Clone();
            if (pickupSensorMultiplier > 0){ 
                finalArray = (float[])CombineArrays(finalArray, pickupSensors).Clone(); // Pickups
                //healthSensors[0] = Health.CurrentHealth / Health.MaxHealth; // Vida
                healthSensors[0] = 1;
                finalArray = (float[])CombineArrays(finalArray, healthSensors).Clone(); // Vida 
            }
            if (holeSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray,holeSensors).Clone(); // Huecos
            if (targetSensorMultiplier > 0) finalArray = (float[])CombineArrays(finalArray, targetSensors).Clone(); // Apuntar enemigos


            if (finalArray.Length <= 0) {
                Debug.LogError("NetworkController::FixedUpdate() --- No sensors available. Please check the multipliers." );
                return;
            }

            if (Time.time >= m_NextActionTime)
            {
                (a, t, f) = network.RunNetwork(finalArray);
                m_NextActionTime = Time.time + NextActionTime_Interval;
                
            }
           

         

            UpdateLookInput();


           
            //CalculateFitness();
        }

        // METHODS
        public void ResetWithNetwork (NeuralNetwork net, bool isElite = false)
        {
            //Health.CurrentHealth = startHealth; // Reiniciar vida a 50 por probar
            network = net;
            eliteCheck = isElite;
            //Reset();
        }

       

        private void ResetWeapon(){
            if (weaponsManager != null)
            {
                WeaponController activeWeapon = weaponsManager.GetActiveWeapon();
                if (activeWeapon != null)
                {
                    activeWeapon.UseAmmo(-activeWeapon.MaxAmmo); // Munición negativa se resetea a full
                }
            }
        }


        

        private void InputSensors() {
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

                Vector3 origen = transform.position + transform.up/2;

                for (int i = 0; i < wallSensors.Length; i++) {
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
                            Debug.DrawLine(transform.position + transform.up/2, hit.point, Color.red);
                        else 
                            Debug.DrawLine(transform.position + transform.up/2, hit.point, Color.green);
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

                if (Physics.Raycast(goalRay, out goalHit, 300f, GoalLayer)) {
                    //goalSensors[0] = 1/goalHit.distance;
                    goalSensors[0] = Mathf.Clamp01(1f-(goalHit.distance / 300f)); // Suavizado, con clamp
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

                for (int i = 0; i < checkpointSensors.Length; i++) {
                    Ray r = new Ray(transform.position, checkpointDirections[i]);
                    RaycastHit hit;

                    if (Physics.Raycast(r, out hit, 10f, CheckpointLayer)) {
                        checkpointSensors[i] = 1/hit.distance;

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

                for (int i = 0; i < holeSensors.Length; i++) {
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

            if(targetSensorMultiplier > 0)
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
                        if (hit.collider.CompareTag("Enemy")){
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

        
       

        public void Move(float v, float h) {
            if (rb == null) return;

            v = Mathf.Max(0f, v);
            
            // Hacia dónde mira pero Y = 0
            Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
            
            // Dirección aplanada
            Vector3 moveDirection = flatForward * (v * 11.4f * Time.fixedDeltaTime);
            
            // Aplica a posición
            Vector3 targetPosition = rb.position + moveDirection;
            rb.MovePosition(targetPosition);

            // Rotación
            float turnSpeed = 90f; 
            Quaternion turnOffset = Quaternion.Euler(0, h * turnSpeed * Time.fixedDeltaTime, 0);
            rb.MoveRotation(rb.rotation * turnOffset);
    }

        public void Shoot(float f)
        {
            if (weaponsManager != null) // Tiene un arma
            {
                WeaponController activeWeapon = weaponsManager.GetActiveWeapon(); 

                // Cambio de arma
                if (activeWeapon != currentWeapon)
                {
                    if (currentWeapon != null) 
                        currentWeapon.OnShootProcessed -= OnWeaponFired;

                    currentWeapon = activeWeapon;

                    if (currentWeapon != null) 
                        currentWeapon.OnShootProcessed += OnWeaponFired;
                }
                
                if (currentWeapon != null)
                {
                    // Neurona > 0.5, dispara
                    bool shootInput = f > 0.5f;
                    
                    // Intenta disparar, si puede activa OnWeaponFired automáticamente
                    currentWeapon.HandleShootInputs(shootInput, shootInput, !shootInput);
                }
            }
        }

        private void OnWeaponFired() // Disparo de armas
        {
            if (currentWeapon != null && currentWeapon.WeaponMuzzle != null)
            {

                events -= shootPenalty; // Pierde puntos por disparar al azar
                // Raycast desde punta del arma
                Ray r = new Ray(currentWeapon.WeaponMuzzle.position, currentWeapon.WeaponMuzzle.forward);
                RaycastHit hit;

                LayerMask maskWallyTarget = WallLayer | EnemyLayer; // El rayo le da o a la pared o al enemigo

                // Si le da a un enemigo 
                if (Physics.Raycast(r, out hit, 100f, maskWallyTarget))
                {
                    if (hit.collider.CompareTag("Enemy"))
                    {
                        // Devuelve puntos
                        events += 100f + shootPenalty; // Recompensa plana por acertar un tiro (y se le devuelve los puntos)
                        // Ver disparos
                        Debug.DrawLine(currentWeapon.WeaponMuzzle.position, hit.point, Color.green, 0.5f);  
                    }
                }
            }
        }

        private float[] CombineArrays(float[] arr1, float[] arr2) {
            var answer = new List<float>();

            foreach (float f in arr1) {
                answer.Add(f);
            }

            foreach (float f in arr2) {
                answer.Add(f);
            }

            return answer.ToArray();
        }

        private void OnDrawGizmosSelected()
        {
            // Solo dibujamos el radio si el multiplicador indica que el sensor está activo
            if (pickupSensorMultiplier > 0)
            {
                // Amarillo
                Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.3f);
                
                // Esfera 
                Gizmos.DrawWireSphere(transform.position, pickupRadius);
            }
            if (dangerSensorsMultiplier > 0)
            {
                // Rojo
                Gizmos.color = new Color(1f, 0f, 0f, 0.2f);

                // Esfera
                Gizmos.DrawWireSphere(transform.position, dangerRadius);
            }
        }
        public override Vector3 GetMoveInput()
        {
            float forwardMove = Mathf.Max(0f, a); // Solo permitimos avanzar o frenar
            return new Vector3(0f, 0f, forwardMove);
        }

        /*
        public override Vector2 GetLookInputs()
        {
            return new Vector2(t, 0f);
        }
        */






        private void UpdateLookInput()
        {
            m_LookInput = Vector2.Lerp(m_LookInput, new Vector2(0,0), Time.deltaTime * lookSharpness);

        }




        public override Vector2 GetLookInputs() => m_LookInput;

        public override bool GetFireInputDown()
        {
            // Return true on the first frame of shooting
            if (f >= 0.5)
            {
                return true;
            }
            return false;
        }

        public override bool GetFireInputHeld()
        {
            // Return true while actively shooting (during the hold duration)
            if (f >= 0.5)
            {
                return true;
            }
            return false;
        }

        public override bool GetFireInputReleased()
        {
            // Return true when shooting stops
            if (f < 0.5)
            {
                return true;
            }
            return false;
        }

        // Jump methods return false
        public override bool GetJumpInputDown() => false;
        public override bool GetJumpInputHeld() => false;

        // Other input methods return false
        public override bool GetAimInputHeld() => false;
        public override bool GetSprintInputHeld() => false;
        public override bool GetCrouchInputDown() => false;
        public override bool GetCrouchInputReleased() => false;
        public override bool GetReloadButtonDown() => false;
        public override int GetSwitchWeaponInput() => 0;
        public override int GetSelectWeaponInput() => 0;
    }
}