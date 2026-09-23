// Assets/TacSim/PipeGenerator/Scripts/ArUcoCodeGenerator.cs
using System.Collections.Generic;
using UnityEngine;
using Rand = System.Random;          // <- never bare 'Random' next to UnityEngine

namespace TAC.Pipeline
{
    /// Generates ArUco payload codes + marker IDs.
    /// Unique IDs (sampling without replacement), unique codes, and a Hamming distance
    /// between every pair of codes in ALL 4 rotations (yaw-ambiguity protection).
    public static class ArUcoCodeGenerator
    {
        public const int MinBits = 4;
        public const int MaxBits = 7;

        public static int[] PickUniqueIds(int count, int idMin, int idMax, uint seed)
        {
            int lo = Mathf.Max(0, Mathf.Min(idMin, idMax));
            int hi = Mathf.Max(lo, Mathf.Max(idMin, idMax));
            int poolSize = hi - lo + 1;

            var pool = new List<int>(poolSize);
            for (int i = 0; i < poolSize; i++) pool.Add(lo + i);

            Shuffle(pool, seed);
            int n = Mathf.Clamp(count, 0, poolSize);

            var ids = new int[n];
            System.Array.Copy(pool.ToArray(), ids, n);
            System.Array.Sort(ids);
            return ids;
        }

        public static System.Collections.Generic.Dictionary<int, ulong> GenerateCodes(
            int[] ids, int bits, uint seed, int minRotationDistance = 3)
        {
            bits = Mathf.Clamp(bits, MinBits, MaxBits);
            int payload = bits * bits;
            ulong mask = payload >= 64 ? ulong.MaxValue : (1UL << payload) - 1UL;

            var rng = new Rand(unchecked((int)(seed * 0x9E3779B1u ^ (uint)(bits * 2654435761u))));
            var table = new System.Collections.Generic.Dictionary<int, ulong>(ids.Length);
            var accepted = new List<ulong>(ids.Length);
            int minDist = Mathf.Max(1, minRotationDistance);
            int guard = 0;

            foreach (int id in ids)
            {
                ulong code = 0UL;
                bool found = false;

                while (guard++ < 4_000_000)
                {
                    code = NextUInt64(rng) & mask;
                    if (IsUsable(code, bits, accepted, minDist))
                    {
                        accepted.Add(code);
                        found = true;
                        break;
                    }
                }

                if (!found)
                    Debug.LogError($"[ArUco] No code found for id {id}. " +
                                   $"Lower minRotationDistance or markerBits.");

                table[id] = code;
            }
            return table;
        }

        static bool IsUsable(ulong code, int bits, List<ulong> accepted, int minDist)
        {
            for (int r = 1; r < 4; r++)
                if (Hamming(code, RotateCW(code, bits, r)) < minDist) return false;

            for (int i = 0; i < accepted.Count; i++)
                for (int r = 0; r < 4; r++)
                    if (Hamming(code, RotateCW(accepted[i], bits, r)) < minDist) return false;

            return true;
        }

        public static ulong RotateCW(ulong code, int bits, int times)
        {
            times = ((times % 4) + 4) % 4;
            for (int t = 0; t < times; t++)
            {
                ulong next = 0UL;
                for (int r = 0; r < bits; r++)
                for (int c = 0; c < bits; c++)
                {
                    if (((code >> (r * bits + c)) & 1UL) == 0UL) continue;
                    int rr = c, cc = bits - 1 - r;
                    next |= 1UL << (rr * bits + cc);
                }
                code = next;
            }
            return code;
        }

        public static int Hamming(ulong a, ulong b)
        {
            int count = 0;
            ulong v = a ^ b;
            while (v != 0UL) { v &= v - 1UL; count++; }
            return count;
        }

        public static bool GetBit(ulong code, int bits, int row, int col)
        {
            int i = Mathf.Clamp(row, 0, bits - 1) * bits + Mathf.Clamp(col, 0, bits - 1);
            return ((code >> i) & 1UL) != 0UL;
        }

        static ulong NextUInt64(Rand r)
        {
            return ((ulong)(uint)r.Next() << 32) | (uint)r.Next();
        }

        static void Shuffle<T>(IList<T> list, uint seed)
        {
            var rng = new Rand(unchecked((int)(seed * 2654435761u ^ 0x85EBCA6Bu)));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
