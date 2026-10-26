using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using PolygonEditor.Model;

namespace PolygonEditor.Rendering
{
    public struct ArcGeometry
    {
        public bool IsLineDegenerate;
        public PointF Center;
        public float Radius;
        public float StartAngleRad;
        public float SweepAngleRad;
    }

    public static class Rasterizer
    {
        /// <summary>
        /// Pełna, całkowitoliczbowa implementacja algorytmu Bresenhama dla wszystkich 8 oktantów.
        /// Zapisuje piksele bezpośrednio do tablicy DirectBitmap.Bits.
        /// </summary>
        public static void DrawLineBresenham(DirectBitmap bmp, int x0, int y0, int x1, int y1, Color color)
        {
            int argb = color.ToArgb();
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;

            if (dx >= dy)
            {
                int err = 2 * dy - dx;
                for (int i = 0; i <= dx; i++)
                {
                    bmp.SetPixel(x0, y0, argb);
                    if (err > 0)
                    {
                        y0 += sy;
                        err -= 2 * dx;
                    }
                    err += 2 * dy;
                    x0 += sx;
                }
            }
            else
            {
                int err = 2 * dx - dy;
                for (int i = 0; i <= dy; i++)
                {
                    bmp.SetPixel(x0, y0, argb);
                    if (err > 0)
                    {
                        x0 += sx;
                        err -= 2 * dy;
                    }
                    err += 2 * dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>
        /// Rysowanie linii przerywanej algorytmem Bresenhama (np. wielobok Béziera).
        /// </summary>
        public static void DrawDashedLineBresenham(DirectBitmap bmp, int x0, int y0, int x1, int y1, Color color, int dashLength = 5)
        {
            int argb = color.ToArgb();
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int stepCount = 0;

            if (dx >= dy)
            {
                int err = 2 * dy - dx;
                for (int i = 0; i <= dx; i++)
                {
                    if ((stepCount / dashLength) % 2 == 0)
                        bmp.SetPixel(x0, y0, argb);

                    if (err > 0)
                    {
                        y0 += sy;
                        err -= 2 * dx;
                    }
                    err += 2 * dy;
                    x0 += sx;
                    stepCount++;
                }
            }
            else
            {
                int err = 2 * dx - dy;
                for (int i = 0; i <= dy; i++)
                {
                    if ((stepCount / dashLength) % 2 == 0)
                        bmp.SetPixel(x0, y0, argb);

                    if (err > 0)
                    {
                        x0 += sx;
                        err -= 2 * dy;
                    }
                    err += 2 * dx;
                    y0 += sy;
                    stepCount++;
                }
            }
        }

        /// <summary>
        /// Algorytm przyrostowy (Forward Differencing) rysowania krzywej Béziera 3. stopnia
        /// po przejściu z bazy Bernsteina do bazy potęgowej: P(t) = A3*t^3 + A2*t^2 + A1*t + A0.
        /// W pętli wykonywane jest wyłącznie dodawanie.
        /// </summary>
        public static void DrawCubicBezierForwardDiff(
            DirectBitmap bmp,
            Graphics g,
            bool useBresenham,
            PointF p0,
            PointF p1,
            PointF p2,
            PointF p3,
            Color color,
            int steps = 160)
        {
            double d = 1.0 / steps;
            double d2 = d * d;
            double d3 = d2 * d;

            // Współczynniki wielomianu w bazie potęgowej:
            // A0 = P0
            // A1 = 3*(P1 - P0)
            // A2 = 3*(P0 - 2*P1 + P2)
            // A3 = -P0 + 3*P1 - 3*P2 + P3
            double a0x = p0.X;
            double a0y = p0.Y;

            double a1x = 3.0 * (p1.X - p0.X);
            double a1y = 3.0 * (p1.Y - p0.Y);

            double a2x = 3.0 * (p0.X - 2.0 * p1.X + p2.X);
            double a2y = 3.0 * (p0.Y - 2.0 * p1.Y + p2.Y);

            double a3x = -p0.X + 3.0 * p1.X - 3.0 * p2.X + p3.X;
            double a3y = -p0.Y + 3.0 * p1.Y - 3.0 * p2.Y + p3.Y;

            // Warunki początkowe różnic skończonych (Forward Differences) dla t = 0:
            double x = a0x;
            double y = a0y;

            double dx = a3x * d3 + a2x * d2 + a1x * d;
            double dy = a3y * d3 + a2y * d2 + a1y * d;

            double d2x = 6.0 * a3x * d3 + 2.0 * a2x * d2;
            double d2y = 6.0 * a3y * d3 + 2.0 * a2y * d2;

            double d3x = 6.0 * a3x * d3;
            double d3y = 6.0 * a3y * d3;

            using Pen? pen = useBresenham ? null : new Pen(color, 1.5f);

            double prevX = x;
            double prevY = y;

            for (int i = 0; i < steps; i++)
            {
                x += dx;
                y += dy;
                dx += d2x;
                dy += d2y;
                d2x += d3x;
                d2y += d3y;

                if (useBresenham)
                {
                    DrawLineBresenham(
                        bmp,
                        (int)Math.Round(prevX),
                        (int)Math.Round(prevY),
                        (int)Math.Round(x),
                        (int)Math.Round(y),
                        color);
                }
                else
                {
                    g.DrawLine(pen!, (float)prevX, (float)prevY, (float)x, (float)y);
                }

                prevX = x;
                prevY = y;
            }
        }

        /// <summary>
        /// Wyznacza parametry geometryczne łuku okręgu z uwzględnieniem klasy ciągłości G0 lub G1.
        /// </summary>
        public static ArcGeometry ComputeArcGeometry(PolygonShape poly, Edge arcEdge, int recursionDepth = 0)
        {
            PointF a = new PointF((float)arcEdge.Start.X, (float)arcEdge.Start.Y);
            PointF b = new PointF((float)arcEdge.End.X, (float)arcEdge.End.Y);

            double chordDx = b.X - a.X;
            double chordDy = b.Y - a.Y;
            double chordLen = Math.Sqrt(chordDx * chordDx + chordDy * chordDy);

            if (chordLen < 1e-4)
            {
                return new ArcGeometry { IsLineDegenerate = true };
            }

            bool startG1 = arcEdge.Start.Continuity == ContinuityType.G1;
            bool endG1 = !startG1 && (arcEdge.End.Continuity == ContinuityType.G1);

            // Gdy oba końce mają G0 -> półokrąg o promieniu R = długość krawędzi / 2
            if (!startG1 && !endG1)
            {
                PointF mid = new PointF((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
                float radius = (float)(chordLen * 0.5);
                float startAngle = (float)Math.Atan2(a.Y - mid.Y, a.X - mid.X);
                return new ArcGeometry
                {
                    IsLineDegenerate = false,
                    Center = mid,
                    Radius = radius,
                    StartAngleRad = startAngle,
                    SweepAngleRad = MathF.PI
                };
            }

            // Wyznaczenie wektora stycznego na końcu posiadającym więz G1
            PointF anchor = startG1 ? a : b;
            PointF other = startG1 ? b : a;
            (double tx, double ty) = GetDesiredArcTangent(poly, arcEdge, startG1, recursionDepth);

            double tLen = Math.Sqrt(tx * tx + ty * ty);
            if (tLen < 1e-6)
            {
                return new ArcGeometry { IsLineDegenerate = true };
            }
            tx /= tLen;
            ty /= tLen;

            // Wektor normalny do stycznej w punkcie styku: n = (-ty, tx)
            double nx = -ty;
            double ny = tx;

            // Środek okręgu C = anchor + s * n, przy warunku |C - anchor|^2 = |C - other|^2
            double vx = other.X - anchor.X;
            double vy = other.Y - anchor.Y;
            double denom = 2.0 * (vx * nx + vy * ny);

            if (Math.Abs(denom) < 1e-3)
            {
                // Styczna współliniowa z cięciwą -> promień dąży do nieskończoności (odcinek)
                return new ArcGeometry { IsLineDegenerate = true };
            }

            double s = (vx * vx + vy * vy) / denom;
            double cx = anchor.X + s * nx;
            double cy = anchor.Y + s * ny;
            double r = Math.Abs(s);

            if (r > 15000.0)
            {
                return new ArcGeometry { IsLineDegenerate = true };
            }

            double angleAnchor = Math.Atan2(anchor.Y - cy, anchor.X - cx);
            double angleOther = Math.Atan2(other.Y - cy, other.X - cx);

            // Kierunek stycznej przy obrocie dodatnim (CCW) w punkcie anchor to (-sin(angleAnchor), cos(angleAnchor))
            double ccwTx = -Math.Sin(angleAnchor);
            double ccwTy = Math.Cos(angleAnchor);

            // Sprawdzamy, czy obrót od anchor do other powinien być zgodny z kierunkiem stycznej tx, ty
            bool goCCWFromAnchor = (ccwTx * tx + ccwTy * ty) >= 0;

            double sweepFromAnchor = angleOther - angleAnchor;
            if (goCCWFromAnchor)
            {
                while (sweepFromAnchor <= 0) sweepFromAnchor += 2.0 * Math.PI;
            }
            else
            {
                while (sweepFromAnchor >= 0) sweepFromAnchor -= 2.0 * Math.PI;
            }

            float finalStartAngle = startG1 ? (float)angleAnchor : (float)angleOther;
            float finalSweep = startG1 ? (float)sweepFromAnchor : (float)(-sweepFromAnchor);

            return new ArcGeometry
            {
                IsLineDegenerate = false,
                Center = new PointF((float)cx, (float)cy),
                Radius = (float)r,
                StartAngleRad = finalStartAngle,
                SweepAngleRad = finalSweep
            };
        }

        /// <summary>
        /// Zwraca jednostkowy wektor styczny wychodzący z wierzchołka (Start lub End) do wnętrza łuku,
        /// wynikający z sąsiedniej krawędzi (odcinka, krzywej Béziera lub drugiego łuku).
        /// </summary>
        private static (double tx, double ty) GetDesiredArcTangent(
            PolygonShape poly,
            Edge arcEdge,
            bool atStart,
            int recursionDepth)
        {
            var (prevEdge, nextEdge) = poly.GetAdjacentEdges(arcEdge);
            Edge neighbor = atStart ? prevEdge : nextEdge;
            Vertex shared = atStart ? arcEdge.Start : arcEdge.End;

            if (neighbor.Type == EdgeType.Line)
            {
                Vertex far = atStart ? neighbor.Start : neighbor.End;
                return (shared.X - far.X, shared.Y - far.Y);
            }
            else if (neighbor.Type == EdgeType.Bezier)
            {
                ControlPoint cp = atStart ? neighbor.CP2 : neighbor.CP1;
                return (shared.X - cp.X, shared.Y - cp.Y);
            }
            else // neighbor.Type == EdgeType.Arc (Dwa łuki obok siebie z G1)
            {
                if (recursionDepth < 1)
                {
                    ArcGeometry neighborArc = ComputeArcGeometry(poly, neighbor, recursionDepth + 1);
                    if (!neighborArc.IsLineDegenerate)
                    {
                        // Środki obu okręgów i wspólny wierzchołek leżą na jednej prostej -> wspólna styczna prostopadła do promienia
                        double rx = shared.X - neighborArc.Center.X;
                        double ry = shared.Y - neighborArc.Center.Y;
                        double dir = neighborArc.SweepAngleRad >= 0 ? 1.0 : -1.0;
                        if (!atStart) dir = -dir;
                        return (-ry * dir, rx * dir);
                    }
                }
                Vertex far = atStart ? neighbor.Start : neighbor.End;
                return (shared.X - far.X, shared.Y - far.Y);
            }
        }

        public static void DrawArc(
            DirectBitmap bmp,
            Graphics g,
            bool useBresenham,
            PolygonShape poly,
            Edge arcEdge,
            Color color)
        {
            ArcGeometry geom = ComputeArcGeometry(poly, arcEdge);
            if (geom.IsLineDegenerate)
            {
                if (useBresenham)
                {
                    DrawLineBresenham(
                        bmp,
                        (int)Math.Round(arcEdge.Start.X),
                        (int)Math.Round(arcEdge.Start.Y),
                        (int)Math.Round(arcEdge.End.X),
                        (int)Math.Round(arcEdge.End.Y),
                        color);
                }
                else
                {
                    using Pen pen = new Pen(color, 1.5f);
                    g.DrawLine(pen, (float)arcEdge.Start.X, (float)arcEdge.Start.Y, (float)arcEdge.End.X, (float)arcEdge.End.Y);
                }
                return;
            }

            int steps = Math.Clamp((int)(geom.Radius * Math.Abs(geom.SweepAngleRad) / 3.0f), 40, 240);
            double dAngle = geom.SweepAngleRad / steps;
            double angle = geom.StartAngleRad;

            double prevX = geom.Center.X + geom.Radius * Math.Cos(angle);
            double prevY = geom.Center.Y + geom.Radius * Math.Sin(angle);

            using Pen? gdiPen = useBresenham ? null : new Pen(color, 1.5f);

            for (int i = 1; i <= steps; i++)
            {
                angle += dAngle;
                double curX = geom.Center.X + geom.Radius * Math.Cos(angle);
                double curY = geom.Center.Y + geom.Radius * Math.Sin(angle);

                if (useBresenham)
                {
                    DrawLineBresenham(
                        bmp,
                        (int)Math.Round(prevX),
                        (int)Math.Round(prevY),
                        (int)Math.Round(curX),
                        (int)Math.Round(curY),
                        color);
                }
                else
                {
                    g.DrawLine(gdiPen!, (float)prevX, (float)prevY, (float)curX, (float)curY);
                }

                prevX = curX;
                prevY = curY;
            }
        }
    }
}