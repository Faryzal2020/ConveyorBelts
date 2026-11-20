using System;
using System.Collections.Generic;

namespace ConveyorBelts.Sim
{
    public static class BeltPath
    {
        public static List<(Cell Cell, Dir Dir)> Compute(Cell start, Cell end, bool zFirst, Dir singleCellDir)
        {
            var result = new List<(Cell, Dir)>();
            int dx = end.X - start.X, dz = end.Z - start.Z;
            if (dx == 0 && dz == 0)
            {
                result.Add((start, singleCellDir));
                return result;
            }
            Dir xDir = dx >= 0 ? Dir.East : Dir.West;
            Dir zDir = dz >= 0 ? Dir.North : Dir.South;
            int ax = Math.Abs(dx), az = Math.Abs(dz);

            Dir firstDir = zFirst ? zDir : xDir, secondDir = zFirst ? xDir : zDir;
            int firstLen = zFirst ? az : ax, secondLen = zFirst ? ax : az;

            var cur = start;
            for (int i = 0; i < firstLen; i++)
            {
                result.Add((cur, firstDir));
                cur = cur.Step(firstDir);
            }
            // cur is now the corner; with no second leg it keeps the first-leg direction.
            Dir cornerDir = secondLen == 0 ? firstDir : secondDir;
            result.Add((cur, cornerDir));
            for (int i = 0; i < secondLen; i++)
            {
                cur = cur.Step(secondDir);
                result.Add((cur, secondDir));
            }
            return result;
        }
    }
}
