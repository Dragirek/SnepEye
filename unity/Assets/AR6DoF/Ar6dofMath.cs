using System;

namespace AR6DoF
{
    /// <summary>Petites fonctions en double précision (quaternions (w,x,y,z), matrices 3x3 en ligne : 9 valeurs).</summary>
    public static class Ar6dofMath
    {
        public static double[] QuatMul(double[] a, double[] b) => new[]
        {
            a[0] * b[0] - a[1] * b[1] - a[2] * b[2] - a[3] * b[3],
            a[0] * b[1] + a[1] * b[0] + a[2] * b[3] - a[3] * b[2],
            a[0] * b[2] - a[1] * b[3] + a[2] * b[0] + a[3] * b[1],
            a[0] * b[3] + a[1] * b[2] - a[2] * b[1] + a[3] * b[0]
        };

        public static double[] Normalize(double[] q)
        {
            double n = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            if (n < 1e-12) return new[] { 1.0, 0.0, 0.0, 0.0 };
            return new[] { q[0] / n, q[1] / n, q[2] / n, q[3] / n };
        }

        /// <summary>Quaternion de la rotation de vecteur (x,y,z) (axe * angle en radians).</summary>
        public static double[] QuatFromRotVec(double x, double y, double z)
        {
            double th = Math.Sqrt(x * x + y * y + z * z);
            if (th < 1e-12) return new[] { 1.0, 0.0, 0.0, 0.0 };
            double s = Math.Sin(th * 0.5) / th;
            return new[] { Math.Cos(th * 0.5), x * s, y * s, z * s };
        }

        public static double[] QuatToMat(double[] q)
        {
            double w = q[0], x = q[1], y = q[2], z = q[3];
            return new[]
            {
                1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y),
                2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x),
                2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)
            };
        }

        public static double[] Slerp(double[] a, double[] b, double t)
        {
            double dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
            double[] bb = b;
            if (dot < 0) { dot = -dot; bb = new[] { -b[0], -b[1], -b[2], -b[3] }; }   // chemin le plus court
            if (dot > 0.9995)  // quasi identiques : interpolation linéaire
                return Normalize(new[] { a[0] + t * (bb[0] - a[0]), a[1] + t * (bb[1] - a[1]), a[2] + t * (bb[2] - a[2]), a[3] + t * (bb[3] - a[3]) });
            double th = Math.Acos(dot), sn = Math.Sin(th);
            double wa = Math.Sin((1 - t) * th) / sn, wb = Math.Sin(t * th) / sn;
            return new[] { wa * a[0] + wb * bb[0], wa * a[1] + wb * bb[1], wa * a[2] + wb * bb[2], wa * a[3] + wb * bb[3] };
        }

        public static double[] Mul3(double[] a, double[] b)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
            return r;
        }

        public static double[] Transpose3(double[] a) =>
            new[] { a[0], a[3], a[6], a[1], a[4], a[7], a[2], a[5], a[8] };
    }
}
