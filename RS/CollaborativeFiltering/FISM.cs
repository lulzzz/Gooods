﻿using RS.Data.Utility;
using RS.DataType;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RS.CollaborativeFiltering
{
    /// <summary>
    /// KDD2013-p659-Kabbur
    /// FISM: Factored Item Similarity Models for Top-N Recommender Systems
    /// </summary>
    public class FISM
    {
        protected int p = 0;   // Number of Users
        protected int q = 0;   // Number of Items
        protected int f = 10;  // Number of features

        // $$P * Q^T$$ denotes item-item similarity matrix
        public double[,] P { get; protected set; }  // Matrix consists of latent item features, left side
        public double[,] Q { get; protected set; }  // Matrix consists of latent item features, right side

        public double[] bu { get; protected set; }  // user biases
        public double[] bi { get; protected set; }  // item biases


        protected double[,] X { get; set; }   // each row in this matrix presents the weighted sum of item features in P.


        public FISM() { }

        public FISM(int p, int q, int f = 10)
        {
            InitializeModel(p, q, f);
        }

        public virtual void InitializeModel(int p, int q, int f)
        {
            this.p = p;
            this.q = q;
            this.f = f;

            bu = new double[p];
            bi = new double[q];

            P = MathUtility.RandomUniform(q, f, -0.001, 0.001); // latent item matrix
            Q = MathUtility.RandomUniform(q, f, -0.001, 0.001); // latent item matrix
            X = new double[p, f];
        }

        /// <summary>
        /// The pdf says: $$\hat(r_{ui}) = b_u + b_i + q_i^T x$$, see Equation (7).
        /// </summary>
        /// <param name="userId">user ID</param>
        /// <param name="itemId">item ID</param>
        /// <returns></returns>
        public virtual double Predict(int userId, int itemId)
        {
            double _r = 0.0;
            for (int i = 0; i < f; i++)
            {
                _r += X[userId, i] * Q[itemId, i];
            }
            return _r + bu[userId] + bi[itemId];
        }


        /// <summary>
        /// update x for each user
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="neighbors"></param>
        /// <param name="excludeItemId"></param>
        /// <param name="factor"></param>
        protected void UpdateX(int userId, List<Rating> neighbors, int excludeItemId, double factor)
        {
            foreach (Rating r in neighbors)
            {
                if (r.ItemId != excludeItemId)
                {
                    for (int i = 0; i < f; i++)
                    {
                        X[userId, i] += Q[r.ItemId, i];
                    }
                }
            }
            for (int i = 0; i < f; i++)
            {
                X[userId, i] *= factor;
            }
        }


        /// <summary>
        /// Loss function for FISMrmse, see Equation
        /// </summary>
        /// <param name="ratings">entries of R, both rated and unrated</param>
        /// <param name="beta">regularization parameter for P and Q</param>
        /// <param name="lambda">regularization parameter for bu</param>
        /// <param name="gamma">regularization parameter for bi</param>
        /// <returns></returns>
        protected virtual double Loss(List<Rating> ratings, double beta, double lambda, double gamma)
        {
            double loss = 0.0;
            foreach (Rating r in ratings)
            {
                double eui = r.Score - Predict(r.UserId, r.ItemId);
                loss += eui * eui;

                double sum_p_i = 0.0;
                double sum_q_j = 0.0;

                for (int i = 0; i < f; i++)
                {
                    sum_p_i += P[r.UserId, i] * P[r.UserId, i];
                    sum_q_j += Q[r.ItemId, i] * Q[r.ItemId, i];
                }

                loss += beta * (sum_p_i + sum_q_j);
                loss += lambda * (bu[r.UserId] * bu[r.UserId]);
                loss += gamma * (bi[r.ItemId] * bi[r.ItemId]);
                loss *= 0.5;
            }
            return loss;
        }


        protected Tuple<double, double> EvaluateMaeRmse(List<Rating> ratings, double minimumRating = 1.0, double maximumRating = 5.0)
        {
            double mae = 0.0;
            double rmse = 0;

            foreach (Rating r in ratings)
            {
                double pui = Predict(r.UserId, r.ItemId);
                if (pui < minimumRating)
                {
                    pui = minimumRating;
                }
                else if (pui > maximumRating)
                {
                    pui = maximumRating;
                }
                double eui = r.Score - pui;

                mae += Math.Abs(eui);
                rmse += eui * eui;
            }

            if (ratings.Count > 0)
            {
                mae /= ratings.Count;
                rmse = Math.Sqrt(rmse / ratings.Count);
            }
            return Tuple.Create(mae, rmse);
        }

        protected void PrintParameters(List<Rating> train, List<Rating> test, int epochs, int rho,
            double yita, double decay, double alpha, double beta, double lambda, double gamma, 
            double maximumRating, double minimumRating)
        {
            Console.WriteLine(GetType().Name);
            Console.WriteLine("train,{0}", train.Count);
            Console.WriteLine("test,{0}", test.Count);
            Console.WriteLine("p,{0},q,{1},f,{2}", p, q, f);
            Console.WriteLine("epochs,{0}", epochs);
            Console.WriteLine("rho,{0}", rho);
            Console.WriteLine("yita,{0}", yita);
            Console.WriteLine("decay,{0}", decay);
            Console.WriteLine("alpha,{0}", alpha);
            Console.WriteLine("beta,{0}", beta);
            Console.WriteLine("lambda,{0}", lambda);
            Console.WriteLine("gamma,{0}", gamma);
            Console.WriteLine("maximumRating,{0}", maximumRating);
            Console.WriteLine("minimumRating,{0}", minimumRating);
        }


        public void TrySGDForRMSE(List<Rating> train, List<Rating> test, int epochs = 100, int rho = 3, 
            double yita = 0.01, double decay = 1.0, double alpha = 1, double beta = 2e-4, double lambda = 0.01, double gamma = 0.01)
        {
            var sampledRatings = Tools.RandomSelectNegativeSamples(train, rho, false);
            var scoreBounds = Tools.GetMaxAndMinScore(sampledRatings);

            PrintParameters(sampledRatings, test, epochs, rho, yita, decay, 
                alpha, beta, lambda, gamma, scoreBounds.Item1, scoreBounds.Item2);


            Console.WriteLine("epoch,loss#train,mae#test,rmse#test");

            for (int epoch = 1; epoch <= epochs; epoch++)
            {
                Console.WriteLine("{0}", epoch);
                // double factorOfX =

            }


        }


    }
}
