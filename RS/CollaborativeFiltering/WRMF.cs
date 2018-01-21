﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RS.Data.Utility;
using RS.DataType;
using RS.Evaluation;

namespace RS.CollaborativeFiltering
{
    /// <summary>
    /// ICDM 2008, Yifan Hu and Yehuda Koren and Chris Volinsky, p263
    /// Collaborative Filtering for Implicit Feedback Datasets
    /// Weighted Regularized Matrix Factorization
    /// </summary>
    public class WRMF : MatrixFactorization
    {
        public WRMF() { }

        public WRMF(int p, int q, int f = 10, string fillMethod = "uniform")
        {
            base.InitializeModel(p, q, f, fillMethod);
        }

        protected void PrintParameters(List<Rating> train, List<Rating> test, int epochs = 100, double lambda = 0.01, double alpha = 40)
        {
            Console.WriteLine(GetType().Name);
            Console.WriteLine("train,{0}", train.Count);
            Console.WriteLine("test,{0}", test.Count);
            Console.WriteLine("p,{0},q,{1},f,{2}", p, q, f);
            Console.WriteLine("epochs,{0}", epochs);
            Console.WriteLine("lambda,{0}", lambda);
            Console.WriteLine("alpha,{0}", alpha);
        }

        protected void PStep(Hashtable userRatingsTable, double lambda, double alpha = 40, string weightedMethod = "linear")
        {
            foreach (int uId in userRatingsTable.Keys)
            {
                List<Rating> ratings = (List<Rating>)userRatingsTable[uId];

                // Au = Y^T * Y
                double[,] Au = new double[f, f];

                // du
                double[] du = new double[f];

                foreach (Rating r in ratings)    // O(Nu * K^2), rating number of user u
                {
                    double cui = MathUtility.WeightedRating(r.Score, alpha, weightedMethod);
                    for (int i = 0; i < f; i++)
                    {
                        for (int j = 0; j < f; j++)
                        {
                            Au[i, j] += Q[r.ItemId, i] * Q[r.ItemId, j] * cui; 
                        }
                        du[i] += r.Score * Q[r.ItemId, i] * cui;
                    }                    
                }               

                // lamda * I + A
                for (int i = 0; i < f; i++)
                {
                    //Au[i, i] += lamda;
                    Au[i, i] += lambda * ratings.Count;
                }

                double[,] AuReverse = MathUtility.Inverse(Au); // O(K^3)

                // Update Pu
                for (int i = 0; i < f; i++)
                {
                    double vij = 0;
                    for (int j = 0; j < f; j++)
                    {
                        vij += AuReverse[i, j] * du[j];
                        //P[uId, i] += AuReverse[i, j] * du[j];
                    }
                    P[uId, i] = vij;
                }
            }

        }

        protected void QStep(Hashtable itemRatingsTable, double lambda, double alpha = 40, string weightedMethod = "linear")
        {
            foreach (int iId in itemRatingsTable.Keys)
            {
                List<Rating> ratings = (List<Rating>)itemRatingsTable[iId];
                // Au
                double[,] Ai = new double[f, f];
                // du
                double[] di = new double[f];

                foreach (Rating r in ratings)    // O(Nu * K^2), Nu denotes the rating number of user u
                {
                    double cui = MathUtility.WeightedRating(r.Score, alpha, weightedMethod);
                    for (int i = 0; i < f; i++)
                    {
                        for (int j = 0; j < f; j++)
                        {
                            Ai[i, j] += P[r.UserId, i] * P[r.UserId, j] * cui;
                        }
                        di[i] += r.Score * P[r.UserId, i] * cui;
                    }
                }

                // lamda * I + A
                for (int i = 0; i < f; i++)
                {
                    // Ai[i, i] += lamda;
                    Ai[i, i] += lambda * ratings.Count;
                }
                double[,] AiReverse = MathUtility.Inverse(Ai); // O(K^3)

                // Update Qi
                for (int i = 0; i < f; i++)
                {
                    double vij = 0;
                    for (int j = 0; j < f; j++)
                    {
                        vij += AiReverse[i, j] * di[j];
                    }
                    Q[iId, i] = vij;
                }
            }
        }


        public void TryALSForTopN(List<Rating> train, List<Rating> test, int epochs = 100, double lambda = 0.01, double alpha = 40)
        {
            PrintParameters(train, test, epochs, lambda, alpha);
            Console.WriteLine("epoch,train:loss,N,P,R,Coverage,Popularity");
            double loss = Loss(train, lambda);
            int[] K = { 1, 5, 10, 15, 20, 25, 30 };  // recommdation list

            Hashtable userRatingsTable = Tools.GetUserItemsTable(train);
            Hashtable itemRatingsTable = Tools.GetItemUsersTable(train);
            MyTable ratingTable = Tools.GetRatingTable(train);

            for (int epoch = 1; epoch <= epochs; epoch++)
            {
                PStep(userRatingsTable, lambda, alpha, "linear");
                QStep(itemRatingsTable, lambda, alpha, "linear");

                double lastLoss = Loss(train, lambda);
                if (epoch % 2 == 0)
                {
                    Console.Write("{0}#{1}", epoch, lastLoss);

                    List<Rating> recommendations = GetRecommendations(ratingTable, K[K.Length - 1], true);   // note that, the max K
                    foreach (int k in K)
                    {
                        Console.Write(",{0}", k);
                        List<Rating> subset = Tools.GetSubset(recommendations, k);
                        var pr = Metrics.PrecisionAndRecall(subset, test);
                        var cp = Metrics.CoverageAndPopularity(subset, train);
                        var map = Metrics.MAP(subset, test, k);
                        Console.WriteLine(",{0},{1},{2},{3},{4}", pr.Item1, pr.Item2, cp.Item1, cp.Item2, map);
                    }
                }

                if (lastLoss < loss)
                {
                    loss = lastLoss;
                }
                else
                {
                    break;
                }
            }

        }
    }
}