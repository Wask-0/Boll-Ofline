using System;
using System.Collections.Generic;

namespace Boll.Core.Rules
{
    /// <summary>Генератор SplitMix64. Состояние открыто для сохранения партии.</summary>
    public sealed class Rng
    {
        public ulong State;
        public Rng(ulong seed) { State = seed; }

        public ulong Next()
        {
            unchecked
            {
                State += 0x9E3779B97F4A7C15UL;
                ulong z = State;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int Next(int n) => (int)(Next() % (ulong)n);
        public double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Нормальное N(0,1), Бокс–Мюллер; обрезано ±3.</summary>
        public double Gauss()
        {
            double u1 = Math.Max(1e-12, NextDouble()), u2 = NextDouble();
            double z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            return Math.Max(-3, Math.Min(3, z));
        }

        public bool Toss() => (Next() & 1UL) == 1UL;

        public void Shuffle<T>(IList<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T t = l[i]; l[i] = l[j]; l[j] = t;
            }
        }
    }
}
