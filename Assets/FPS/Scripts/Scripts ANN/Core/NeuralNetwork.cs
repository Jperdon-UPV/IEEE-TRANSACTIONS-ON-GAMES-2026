using UnityEngine;
using System.Collections.Generic;

using System;
using MathNet.Numerics.LinearAlgebra;

using Random = UnityEngine.Random;

namespace Coursework.Core
{
    public class NeuralNetwork
    {
        // VARIABLES
        private const int NUMBER_OF_INPUTS = 15;
        public Matrix<float> inputLayer = Matrix<float>.Build.Dense(1, NUMBER_OF_INPUTS);

        public List<Matrix<float>> hiddenLayers = new List<Matrix<float>>();

        public Matrix<float> outputLayer = Matrix<float>.Build.Dense(1, 3); // Cambiar para disparo a 3

        public List<Matrix<float>> weights = new List<Matrix<float>>();

        // Vector de floats (un sesgo por neurona)
        public List<Vector<float>> biases = new List<Vector<float>>();

        public float fitness;

        private int numberOfInputs;

        // EXECUTION FUNCTIONS
        public NeuralNetwork(int numberOfInputs) { 
            this.numberOfInputs = numberOfInputs;
            inputLayer = Matrix<float>.Build.Dense(1, numberOfInputs); 
        }

        // METHODS
        public void Initialize (int hiddenLayerCount, int hiddenNeuronCount)
        {
            inputLayer.Clear();
            hiddenLayers.Clear();
            outputLayer.Clear();
            weights.Clear();
            biases.Clear();

            for (int i = 0; i < hiddenLayerCount + 1; i++)
            {

                Matrix<float> f = Matrix<float>.Build.Dense(1, hiddenNeuronCount);

                hiddenLayers.Add(f);

                // Vector tamaño exacto de neuronas en capa
                Vector<float> hiddenBias = Vector<float>.Build.Dense(hiddenNeuronCount, _ => Random.Range(-1f, 1f));
                biases.Add(hiddenBias);

                //WEIGHTS
                if (i == 0)
                {
                    Matrix<float> inputToH1 = Matrix<float>.Build.Dense(numberOfInputs, hiddenNeuronCount);
                    weights.Add(inputToH1);
                }
                else // Añado 'else' para evitar crear matrices redundantes
                {
                    Matrix<float> HiddenToHidden = Matrix<float>.Build.Dense(hiddenNeuronCount, hiddenNeuronCount);
                    weights.Add(HiddenToHidden);
                }
            }

            Matrix<float> OutputWeight = Matrix<float>.Build.Dense(hiddenNeuronCount, 3); // Salida disparo
            weights.Add(OutputWeight);
            
            // Vector para tres neuronas salida
            Vector<float> outputBias = Vector<float>.Build.Dense(3, _ => Random.Range(-1f, 1f)); // Salida disparo
            biases.Add(outputBias);

            RandomiseWeights();

        }

        public NeuralNetwork InitializeCopy (int hiddenLayerCount, int hiddenNeuronCount)
        {
            NeuralNetwork n = new NeuralNetwork(numberOfInputs);

            List<Matrix<float>> newWeights = new List<Matrix<float>>();

            for (int i = 0; i < this.weights.Count; i++)
            {
                Matrix<float> currentWeight = Matrix<float>.Build.Dense(weights[i].RowCount, weights[i].ColumnCount);

                for (int x = 0; x < currentWeight.RowCount; x++)
                {
                    for (int y = 0; y < currentWeight.ColumnCount; y++)
                    {
                        currentWeight[x, y] = weights[i][x, y];
                    }
                }

                newWeights.Add(currentWeight);
            }

            // No copiar floats, sino clonar (Clone()) de vectores
            List<Vector<float>> newBiases = new List<Vector<float>>();
            for (int i = 0; i < this.biases.Count; i++)
            {
                newBiases.Add(this.biases[i].Clone());
            }

            n.weights = newWeights;
            n.biases = newBiases;

            n.InitializeHidden(hiddenLayerCount, hiddenNeuronCount);

            return n;
        }

        public void InitializeHidden (int hiddenLayerCount, int hiddenNeuronCount)
        {
            inputLayer.Clear();
            hiddenLayers.Clear();
            outputLayer.Clear();

            for (int i = 0; i < hiddenLayerCount + 1; i ++)
            {
                Matrix<float> newHiddenLayer = Matrix<float>.Build.Dense(1, hiddenNeuronCount);
                hiddenLayers.Add(newHiddenLayer);
            }

        }

        public void RandomiseWeights()
        {
            for (int i = 0; i < weights.Count; i++)
            {
                for (int x = 0; x < weights[i].RowCount; x++)
                {
                    for (int y = 0; y < weights[i].ColumnCount; y++)
                    {
                        weights[i][x, y] = Random.Range(-1f, 1f);
                    }
                }
            }
        }

        public (float, float, float) RunNetwork (float[] sensors) // Salida disparo tercer float
        {
            for (int i = 0; i < sensors.Length; i++) {
                inputLayer[0, i] = sensors[i];
            }

            inputLayer = inputLayer.PointwiseTanh();

            for (int i = 0; i < hiddenLayers.Count; i++) {
                var layer = (i == 0) ? inputLayer : hiddenLayers[i - 1];
                // Convertir vector a fila de matriz para sumar
                hiddenLayers[i] = ((layer * weights[i]) + biases[i].ToRowMatrix()).PointwiseTanh();
            }

            // Mismo en capa de salida
            outputLayer = ((hiddenLayers[hiddenLayers.Count-1]*weights[weights.Count-1]) + biases[biases.Count-1].ToRowMatrix())/*.PointwiseTanh()*/;

            //First output is acceleration and second output is steering and third output is shooting
            return (Sigmoid(outputLayer[0,0]), (float)Math.Tanh(outputLayer[0,1]), Sigmoid(outputLayer[0,2]));
        }

        private float Sigmoid (float s) => (1 / (1 + Mathf.Exp(-s)));

        // Usar un agente pre-entrenado
        public void LoadFromRecord(IndividualRecord record)
        {
            // Recuperar pesos
            for (int i = 0; i < weights.Count; i++)
            {
                int index = 0;
                for (int r = 0; r < weights[i].RowCount; r++)
                {
                    for (int c = 0; c < weights[i].ColumnCount; c++)
                    {
                        // Entra a la lista de números (SerializedMatrix)
                        weights[i][r, c] = record.Weights[i].data[index]; 
                        index++;
                    }
                }
            }

            // Recuperar biases
            for (int i = 0; i < biases.Count; i++)
            {
                for (int r = 0; r < biases[i].Count; r++)
                {
                    // (Serialized vector data también)
                    biases[i][r] = record.Biases[i].data[r]; 
                }
            }
        }
    }
}