using UnityEngine;
using System.Collections.Generic;
using System;
using System.IO;

using MathNet.Numerics.LinearAlgebra;
using Coursework.Utils;

using Random = UnityEngine.Random;
using UnityEngine.Animations;

namespace Coursework.Core
{
    // Clase envoltorio UnityJSON
    [System.Serializable]
    public class SerializedMatrix
    {
        public int rows;
        public int cols;
        public float[] data;
    }

    [System.Serializable]
    public class SerializedVector
    {
        public float[] data;
    }

    [System.Serializable]
    public class IndividualRecord
    {
        public int Generation;
        public int IndividualID;
        public float Fitness;
        public string CourseName;

        public List<SerializedMatrix> Weights;
        public List<SerializedVector> Biases;

        public IndividualRecord(NeuralNetwork network, int generation, int id, string courseName)
        {
            Generation = generation;
            IndividualID = id;
            Fitness = network.fitness;
            CourseName = courseName;

            Weights = new List<SerializedMatrix>();
            foreach (var weightMatrix in network.weights)
            {
                SerializedMatrix sm = new SerializedMatrix();
                sm.rows = weightMatrix.RowCount;
                sm.cols = weightMatrix.ColumnCount;
                sm.data = new float[sm.rows * sm.cols];
                
                // Aplanar 2D a 1D
                int idx = 0;
                for (int x = 0; x < sm.rows; x++) {
                    for (int y = 0; y < sm.cols; y++) {
                        sm.data[idx++] = weightMatrix[x, y];
                    }
                }
                Weights.Add(sm);
            }

            Biases = new List<SerializedVector>();
            foreach (var biasVector in network.biases)
            {
                SerializedVector sv = new SerializedVector();
                sv.data = biasVector.ToArray();
                Biases.Add(sv);
            }
        }
    }

    [System.Serializable]
    public class GenerationRecord
    {
        public List<IndividualRecord> PopulationData = new List<IndividualRecord>();
    }

    public class GeneticManager : MonoBehaviour
    {
        // VARIABLES
        [Header("References")]
        public NetworkController controller;

        [Header("Controls")]
        public int initialPopulation = 85;
        [Range(0.0f, 1.0f)]
        public float mutationRate = 0.055f;

        // Proteger mejores de generación de mutaciones
        [Range(0.0f, 1.0f)]
        public float elitismRate = 0.05f;

        [Range (0.0f, 1.0f)]
        public float randomInjectionRate = 0.05f;

        [Header("Crossover Controls")]
        //public int bestAgentSelection = 8;
        //public int worstAgentSelection = 3;
        private int numberToCrossover;

        private List<int> genePool = new List<int>();
        private float totalParentGenePool = 0f;

        private int naturallySelected;

        private NeuralNetwork[] population;

        [Header("Public View")]
        public int currentGeneration;
        public int currentGenome = 0;

        [Header("Guardar/Cargar datos")]
        // Activar si exportar datos a JSON
        public bool guardarDatos = true;
        // Activar para empezar desde datos guardados
        public bool cargarDatos = false;
        public String experimentoACargar;
        public int generacionACargar;

        public static GeneticManager Instance { get; private set; }

        // CAMBIO: Variables para gestionar las carpetas de guardado JSON
        private string experimentFolderPath;
        private DateTime generationStartTime;

        [Header("Debug")]
        public List<float> debugFitnessList = new List<float>();

        // EXECUTE FUNCTIONS
        private void Awake() => Instance = this;
        private void Start() 
        {
            if (guardarDatos)
            {   
                CreateExperimentFolder();
            }
            CreatePopulation();
        }

        // METHODS

        // Crear la carpeta principal del experimento
        private void CreateExperimentFolder()
        {
            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string basePath = Path.Combine(Application.dataPath, "Experiments");
            
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            experimentFolderPath = Path.Combine(basePath, $"Experiment_{timeStamp}");
            Directory.CreateDirectory(experimentFolderPath);
            
            Debug.Log($"Experiment folder created at: {experimentFolderPath}");
            
            generationStartTime = DateTime.Now;
        }

        private void CreatePopulation()
        {
            population = new NeuralNetwork[initialPopulation];

            if (cargarDatos)
            {
                LoadGenerationData();
                ResetToCurrentGenome();
            }
            else
            {
                FillPopulationWithRandomValues(population, 0);
                ResetToCurrentGenome();
            }
        }

        private void LoadGenerationData()
        {
            string basePath = Path.Combine(Application.dataPath, "Experiments");
            // Añade automáticamente el prefijo "Experiment_" a lo que pongas en Unity
            string experimentPath = Path.Combine(basePath, $"Experiment_{experimentoACargar}");
            string generationFolder = Path.Combine(experimentPath, $"Generation_{generacionACargar}");
            string filePath = Path.Combine(generationFolder, "GenerationData.json");

            // Comprobamos si el archivo unificado realmente existe
            if (!Directory.Exists(generationFolder) || !File.Exists(filePath))
            {
                Debug.LogError($"No se encontró {filePath}. Generando aleatorio.");
                FillPopulationWithRandomValues(population, 0);
                return;
            }

            Debug.Log($"Cargando generación {generacionACargar} de {experimentoACargar}...");

            // Leemos el disco duro 
            string json = File.ReadAllText(filePath);
            GenerationRecord genRecord = JsonUtility.FromJson<GenerationRecord>(json);

            for (int i = 0; i < initialPopulation; i++)
            {
                // Extraemos los datos desde la lista en memoria, no desde el disco
                if (i < genRecord.PopulationData.Count)
                {
                    IndividualRecord record = genRecord.PopulationData[i];

                    NeuralNetwork net = new NeuralNetwork(controller.SENSORS);
                    net.InitializeHidden(controller.LAYERS, controller.NEURONS);
                    
                    // RECONSTRUIR PESOS
                    net.weights = new List<Matrix<float>>();
                    foreach (var sm in record.Weights)
                    {
                        Matrix<float> mat = Matrix<float>.Build.Dense(sm.rows, sm.cols);
                        int idx = 0;
                        for (int x = 0; x < sm.rows; x++) {
                            for (int y = 0; y < sm.cols; y++) {
                                mat[x, y] = sm.data[idx++];
                            }
                        }
                        net.weights.Add(mat);
                    }

                    // RECONSTRUIR SESGOS
                    net.biases = new List<Vector<float>>();
                    foreach (var sv in record.Biases)
                    {
                        Vector<float> vec = Vector<float>.Build.DenseOfArray(sv.data);
                        net.biases.Add(vec);
                    }

                    net.fitness = record.Fitness;
                    population[i] = net;
                }
                else
                {
                    // Por si cambiaste initialPopulation a un número mayor desde la última vez
                    Debug.LogWarning($"Falta el individuo {i} en el JSON. Generando uno aleatorio.");
                    NeuralNetwork randomNet = new NeuralNetwork(controller.SENSORS);
                    randomNet.Initialize(controller.LAYERS, controller.NEURONS);
                    population[i] = randomNet;
                }
            }

            // Sumar 1 para que al guardar lo haga en la carpeta siguiente
            currentGeneration = generacionACargar + 1;
            Debug.Log($"Generación cargada. Iniciando simulación de la Generación {currentGeneration}...");
        }

        private void ResetToCurrentGenome()
        {
            int eliteCount = Mathf.RoundToInt(initialPopulation * elitismRate);
            bool isElite = currentGenome < eliteCount;
            controller.ResetWithNetwork(population[currentGenome], isElite);
        }

        private void FillPopulationWithRandomValues (NeuralNetwork[] newPopulation, int startingIndex)
        {
            while (startingIndex < initialPopulation)
            {
                newPopulation[startingIndex] = new NeuralNetwork(controller.SENSORS);
                newPopulation[startingIndex].Initialize(controller.LAYERS, controller.NEURONS);
                startingIndex++;
            }
        }

        public void Kill(float fitness, NeuralNetwork network)
        {
            // Guarda la puntuación obtenida antes de pasar al siguiente
            population[currentGenome].fitness = fitness;

            if (currentGenome < population.Length -1)
            {
                currentGenome++;
                ResetToCurrentGenome();
            }
            else 
            {
                if (guardarDatos)
                {
                    SaveGenerationData();
                }
                Repopulate();
            }
        }

        private void SaveGenerationData()
        {
            // Ordenar para saber quién fue el mejor antes de guardar
            SortingAlgorithms.BubbleSort(ref population);

            string generationFolder = Path.Combine(experimentFolderPath, $"Generation_{currentGeneration}");
            Directory.CreateDirectory(generationFolder);

            // Creamos la caja para toda la generación
            GenerationRecord currentGenData = new GenerationRecord();

            // Guardar a cada individuo DENTRO de la caja en vez de en el disco
            for (int i = 0; i < population.Length; i++)
            {
                IndividualRecord record = new IndividualRecord(population[i], currentGeneration, i, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                currentGenData.PopulationData.Add(record);
            }

            // Guardar la caja entera en 1 solo archivo de texto
            string json = JsonUtility.ToJson(currentGenData, true); 
            string filePath = Path.Combine(generationFolder, "GenerationData.json");
            File.WriteAllText(filePath, json);

            // Guardar resumen general del mejor en la raíz del experimento (Se mantiene igual)
            IndividualRecord bestRecord = new IndividualRecord(population[0], currentGeneration, 0, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            string bestJson = JsonUtility.ToJson(bestRecord, true);
            string summaryPath = Path.Combine(experimentFolderPath, "summary.json");
            
            float mejorPuntaje = population[0].fitness;
            float peorPuntaje = population[population.Length - 1].fitness;

            string summaryEntry = $"\n--- Generación {currentGeneration} ---\nMejor: {mejorPuntaje} | Peor: {peorPuntaje}\nDatos del Campeón:\n{bestJson}\n";
            File.AppendAllText(summaryPath, summaryEntry);

            // Guardar el tiempo que tardó la generación
            SaveExperimentTime();

            // Guardar el mejor
            try
            {
                string bestFileName = "BestAgent_Gen" + currentGeneration + ".json";
                string bestPath = Path.Combine(generationFolder, bestFileName);
                File.WriteAllText(bestPath, bestJson); // Guardar
                Debug.Log("Campeón guardado con éxito en: " + bestPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error al intentar guardar el campeón: " + e.Message);
            }
        }

        // Método para calcular y guardar la duración de la generación
        private void SaveExperimentTime()
        {
            TimeSpan duration = DateTime.Now - generationStartTime;
            string timePath = Path.Combine(experimentFolderPath, "time_log.txt");
            string logEntry = $"Generation {currentGeneration} Time: {duration.TotalSeconds:F2} seconds\n";
            File.AppendAllText(timePath, logEntry);
            
            // Reiniciar cronómetro para la próxima
            generationStartTime = DateTime.Now;
        }

        private void Repopulate()
        {
            genePool.Clear();
            currentGeneration++;
            naturallySelected = 0;
            
            // Ya ordernado, por seguridad estructural
            SortingAlgorithms.BubbleSort(ref population);

            debugFitnessList.Clear();
            for (int i = 0; i < population.Length; i++)
            {
                debugFitnessList.Add(population[i].fitness);
            }

            var newPopulation = PickBestPopulation();

            int randomCount = Mathf.RoundToInt(initialPopulation * randomInjectionRate);

            /*int eliteCount = Mathf.RoundToInt(initialPopulation * elitismRate);*/
            numberToCrossover = initialPopulation - naturallySelected - randomCount;

            if (numberToCrossover % 2 != 0){numberToCrossover --;}

            Crossover(newPopulation);
            Mutate(newPopulation);

            FillPopulationWithRandomValues(newPopulation, naturallySelected);

            population = newPopulation;

            currentGenome = 0;

            ResetToCurrentGenome();
        }

        private void Mutate(NeuralNetwork[] newPopulation)
        {
            // Agentes que no deben mutar
            int eliteCount = Mathf.RoundToInt(initialPopulation * elitismRate);

            // Empezamos a mutar después élite
            for (int i = eliteCount; i < naturallySelected; i++)
            {
                // Mutar Pesos
                for (int c = 0; c < newPopulation[i].weights.Count; c++)
                {
                    if (Random.Range(0.0f, 1.0f) < mutationRate)
                    {
                        // Usar mutación gaussiana //QUIZÁS CAMBIAR POR RANDOM PURO
                        newPopulation[i].weights[c] = MutateMatrixGaussian(newPopulation[i].weights[c]);
                    }
                }

                // Mutar Sesgos (Biases) - Ahora son vectores independientes
                for (int b = 0; b < newPopulation[i].biases.Count; b++)
                {
                    if (Random.Range(0.0f, 1.0f) < mutationRate)
                    {
                        newPopulation[i].biases[b] = MutateVectorGaussian(newPopulation[i].biases[b]);
                    }
                }
            }
        }

        // Mutación con distribución normal para matrices
        private Matrix<float> MutateMatrixGaussian(Matrix<float> A)
        {
            int randomPoints = Random.Range(1, (A.RowCount * A.ColumnCount) / 7);
            Matrix<float> C = A.Clone();

            for (int i = 0; i < randomPoints; i++)
            {
                int randomColumn = Random.Range(0, C.ColumnCount);
                int randomRow = Random.Range(0, C.RowCount);

                // Sumar pequeño valor basado en campana Gauss
                float change = NextGaussian(0, 0.15f); //0.15 para introducir cosas nuevas, //0.05 para fine-tuning
                C[randomRow, randomColumn] = Mathf.Clamp(C[randomRow, randomColumn] + change, -1f, 1f);
            }

            return C;
        }

        // Mutar Vectores de forma gaussiana 
        private Vector<float> MutateVectorGaussian(Vector<float> V)
        {
            int randomPoints = Random.Range(1, Mathf.Max(2, V.Count / 3));
            Vector<float> newV = V.Clone();

            for (int i = 0; i < randomPoints; i++)
            {
                int randomIdx = Random.Range(0, newV.Count);
                float change = NextGaussian(0, 0.15f);
                newV[randomIdx] = Mathf.Clamp(newV[randomIdx] + change, -1f, 1f);
            }

            return newV;
        }

        // Función números aleatorios distribución normal
        private float NextGaussian(float mean, float standard_deviation)
        {
            float u1 = 1.0f - Random.value; // Uniforme(0,1]
            float u2 = 1.0f - Random.value;
            float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
            return mean + standard_deviation * randStdNormal;
        }

        private int WheelSpin()
        {
            float spin = Random.Range(0f, totalParentGenePool); // Gira la ruleta
            float spinSum = 0f;

            float minFitness = population[population.Length-1].fitness; // Ancla

            foreach(int i in genePool)
            {
                float adjustedFitness = population[i].fitness;
                if(minFitness < 0) { adjustedFitness = Mathf.Abs(minFitness); }
                if(adjustedFitness < 1f){ adjustedFitness = 1f; }

                spinSum += adjustedFitness;
                if (spinSum >= spin) // Si con el fitness se colma el vaso
                {
                    return i; // Devuelve el que colmó el vaso
                }
            }
            return genePool[0]; // Si se rompiese, devolver el mejor
        }

        private void Crossover(NeuralNetwork[] newPopulation)
    {
        for (int i = 0; i < numberToCrossover; i += 2)
        {
            int aIndex = i;
            int bIndex = i + 1;

            // --- SELECCIÓN DE PADRES ---
            if (genePool.Count >= 2)
            {
                for (int l = 0; l < 100; l++)
                {
                    aIndex = WheelSpin();
                    bIndex = WheelSpin();

                    // Evitar que el agente se cruce consigo mismo (si hay suficientes opciones)
                    if (aIndex != bIndex)
                        break;
                }
            }

            // --- INICIALIZACIÓN DE LOS HIJOS ---
            NeuralNetwork child1 = new NeuralNetwork(controller.SENSORS);
            NeuralNetwork child2 = new NeuralNetwork(controller.SENSORS);

            child1.Initialize(controller.LAYERS, controller.NEURONS);
            child2.Initialize(controller.LAYERS, controller.NEURONS);

            child1.fitness = 0;
            child2.fitness = 0;

            // --- 1. CRUCE DE PESOS (Cruce Aritmético) ---
            for (int w = 0; w < child1.weights.Count; w++)
            {
                for (int x = 0; x < child1.weights[w].RowCount; x++)
                {
                    for (int y = 0; y < child1.weights[w].ColumnCount; y++)
                    {
                        float weightA = population[aIndex].weights[w][x, y];
                        float weightB = population[bIndex].weights[w][x, y];

                        // Generamos un porcentaje de mezcla aleatorio para cada gen
                        float alpha = Random.Range(0.0f, 1.0f);

                        // El Hijo 1 recibe la mezcla directa
                        child1.weights[w][x, y] = (weightA * alpha) + (weightB * (1f - alpha));
                        
                        // El Hijo 2 recibe la mezcla inversa
                        child2.weights[w][x, y] = (weightA * (1f - alpha)) + (weightB * alpha);
                    }
                }
            }

            // --- 2. CRUCE DE SESGOS (Cruce Aritmético) ---
            for (int b = 0; b < child1.biases.Count; b++)
            {
                for (int x = 0; x < child1.biases[b].Count; x++)
                {
                    float biasA = population[aIndex].biases[b][x];
                    float biasB = population[bIndex].biases[b][x];

                    // Generamos un porcentaje de mezcla aleatorio
                    float alpha = Random.Range(0.0f, 1.0f);

                    child1.biases[b][x] = (biasA * alpha) + (biasB * (1f - alpha));
                    child2.biases[b][x] = (biasA * (1f - alpha)) + (biasB * alpha);
                }
            }

            // --- AÑADIR HIJOS A LA NUEVA POBLACIÓN (CON SEGURIDAD) ---
            
            // Si hay hueco, metemos al Hijo 1
            if (naturallySelected < newPopulation.Length)
            {
                newPopulation[naturallySelected] = child1;
                naturallySelected++;
            }

            // Si TODAVÍA hay hueco, metemos al Hijo 2
            if (naturallySelected < newPopulation.Length)
            {
                newPopulation[naturallySelected] = child2;
                naturallySelected++;
            }
        }
    }

        private NeuralNetwork[] PickBestPopulation()
        {
            var newPopulation = new NeuralNetwork[initialPopulation];

            // Encontrar el peor fitness 
            float minFitness = population[population.Length - 1].fitness;

            genePool.Clear();
            totalParentGenePool = 0f;

            int eliteCount = Mathf.RoundToInt(initialPopulation * elitismRate);
            // Élite (como la serie)
            for (int i = 0; i < eliteCount; i++)
            {
                // Clonar agente élite de frente
                newPopulation[naturallySelected] = population[i].InitializeCopy(controller.LAYERS, controller.NEURONS);
                newPopulation[naturallySelected].fitness = 0;
                naturallySelected++;

            }

            // Agregar al todos al genepool
            for (int i = 0; i < population.Length; i++)
            {
                // Evitar negativos
                float adjustedFitness = population[i].fitness;
                if (minFitness < 0){ adjustedFitness += Mathf.Abs(minFitness); }
                // Mínimo 1
                if (adjustedFitness <1f){ adjustedFitness = 1f; }

                genePool.Add(i);
                totalParentGenePool += adjustedFitness;
            }

            return newPopulation;
        }
    }
}