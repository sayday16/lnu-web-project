namespace LNU_Web.Services
{
    public class GaussResult
    {
        public double[] FirstValues { get; set; } = Array.Empty<double>();
        public double MaxError { get; set; }
    }

    public class GaussSolver
    {
        private const int EliminationShare = 98;
        private const int PreviewLength = 10;

        public GaussResult Solve(int size, int seed, Action<int, string> reportProgress, CancellationToken cancellationToken)
        {
            var random = new Random(seed);
            var matrix = new double[size][];
            var expected = new double[size];

            for (var i = 0; i < size; i++)
            {
                expected[i] = random.NextDouble() * 10 - 5;
            }

            for (var i = 0; i < size; i++)
            {
                var row = new double[size];
                for (var j = 0; j < size; j++)
                {
                    row[j] = random.NextDouble() * 2 - 1;
                }
                row[i] += size;
                matrix[i] = row;
            }

            var rightSide = new double[size];
            for (var i = 0; i < size; i++)
            {
                var row = matrix[i];
                var sum = 0.0;
                for (var j = 0; j < size; j++)
                {
                    sum += row[j] * expected[j];
                }
                rightSide[i] = sum;
            }

            var lastPercent = -1;

            for (var k = 0; k < size; k++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pivotRow = k;
                var pivotAbs = Math.Abs(matrix[k][k]);
                for (var i = k + 1; i < size; i++)
                {
                    var candidate = Math.Abs(matrix[i][k]);
                    if (candidate > pivotAbs)
                    {
                        pivotAbs = candidate;
                        pivotRow = i;
                    }
                }

                if (pivotAbs < 1e-12)
                {
                    throw new InvalidOperationException("The matrix is singular.");
                }

                if (pivotRow != k)
                {
                    (matrix[k], matrix[pivotRow]) = (matrix[pivotRow], matrix[k]);
                    (rightSide[k], rightSide[pivotRow]) = (rightSide[pivotRow], rightSide[k]);
                }

                var rowK = matrix[k];
                var pivot = rowK[k];

                for (var i = k + 1; i < size; i++)
                {
                    var rowI = matrix[i];
                    var factor = rowI[k] / pivot;
                    if (factor == 0)
                    {
                        continue;
                    }

                    rowI[k] = 0;
                    for (var j = k + 1; j < size; j++)
                    {
                        rowI[j] -= factor * rowK[j];
                    }
                    rightSide[i] -= factor * rightSide[k];
                }

                var remaining = (size - k - 1) / (double)size;
                var done = 1 - remaining * remaining * remaining;
                var percent = (int)(done * EliminationShare);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    reportProgress(percent, "Forward elimination");
                }
            }

            var solution = new double[size];
            for (var i = size - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = matrix[i];
                var sum = rightSide[i];
                for (var j = i + 1; j < size; j++)
                {
                    sum -= row[j] * solution[j];
                }
                solution[i] = sum / row[i];

                var percent = EliminationShare + (int)((size - i) / (double)size * (100 - EliminationShare));
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    reportProgress(percent, "Back substitution");
                }
            }

            var maxError = 0.0;
            for (var i = 0; i < size; i++)
            {
                maxError = Math.Max(maxError, Math.Abs(solution[i] - expected[i]));
            }

            return new GaussResult
            {
                FirstValues = solution.Take(PreviewLength).ToArray(),
                MaxError = maxError
            };
        }
    }
}
