using System;
using System.Collections.Generic;
using System.Linq;
using PolygonEditor.Model;

namespace PolygonEditor.Solver
{
    public static class ConstraintSolver
    {
        private const int DefaultIterations = 28;
        private const double Epsilon = 1e-6;

        /// <summary>
        /// Przełącza typ krawędzi z uwzględnieniem wszystkich reguł ciągłości:
        /// - Domyślna klasa ciągłości G1 przy włączeniu Béziera (o ile nie ustawiono jawnie wcześniej).
        /// - Automatyczna degradacja C1 -> G1 przy włączeniu łuku.
        /// - Wzajemne wykluczanie G1 na obu końcach łuku.
        /// </summary>
        public static string SetEdgeType(Scene scene, PolygonShape poly, Edge edge, EdgeType newType)
        {
            string info = string.Empty;
            if (edge.Type == newType) return info;

            // Przełączenie na krzywą (Bézier / Łuk) usuwa ograniczenia liniowe przypisane do odcinka
            if (newType != EdgeType.Line)
            {
                scene.RemoveAllConstraintsFromEdge(edge);
            }

            edge.Type = newType;

            if (newType == EdgeType.Bezier)
            {
                edge.ResetControlPoints();

                // Wariant A–K: domyślnie G1, chyba że użytkownik jawnie określił ciągłość wcześniej
                if (!edge.Start.ContinuityExplicitlySet)
                    edge.Start.Continuity = ContinuityType.G1;
                if (!edge.End.ContinuityExplicitlySet)
                    edge.End.Continuity = ContinuityType.G1;

                // Jeśli sąsiaduje z łukiem, nie może mieć C1
                if (poly.HasAdjacentArc(edge.Start) && edge.Start.Continuity == ContinuityType.C1)
                    edge.Start.Continuity = ContinuityType.G1;
                if (poly.HasAdjacentArc(edge.End) && edge.End.Continuity == ContinuityType.C1)
                    edge.End.Continuity = ContinuityType.G1;

                EnforceVertexContinuityFromVertex(poly, edge.Start);
                EnforceVertexContinuityFromVertex(poly, edge.End);
            }
            else if (newType == EdgeType.Arc)
            {
                // Łuk nie obsługuje klasy C1 -> degradacja do G1
                if (edge.Start.Continuity == ContinuityType.C1)
                {
                    edge.Start.Continuity = ContinuityType.G1;
                    info = "Ciągłość C1 na początku łuku została automatycznie zmieniona na G1 (łuk obsługuje tylko G0 i G1).";
                }
                if (edge.End.Continuity == ContinuityType.C1)
                {
                    edge.End.Continuity = ContinuityType.G1;
                    info = "Ciągłość C1 na końcu łuku została automatycznie zmieniona na G1 (łuk obsługuje tylko G0 i G1).";
                }

                // Ścisła reguła wzajemnego wykluczania G1 na końcach łuku: tylko jeden koniec może mieć G1
                if (edge.Start.Continuity == ContinuityType.G1 && edge.End.Continuity == ContinuityType.G1)
                {
                    edge.End.Continuity = ContinuityType.G0;
                    if (string.IsNullOrEmpty(info))
                        info = "Tylko jeden koniec łuku może mieć ciągłość G1 – drugi koniec ustawiono na G0.";
                }
            }

            SolveScene(scene, pinnedVertex: null, activeControlPoint: null, preferredEdge: edge);
            return info;
        }

        /// <summary>
        /// Ustawia klasę ciągłości w wierzchołku z walidacją ograniczeń łuku i Béziera.
        /// </summary>
        public static bool SetVertexContinuity(Scene scene, PolygonShape poly, Vertex v, ContinuityType target, out string message)
        {
            message = string.Empty;
            var (incoming, outgoing) = poly.GetIncidentEdges(v);

            bool hasArc = incoming.Type == EdgeType.Arc || outgoing.Type == EdgeType.Arc;
            if (hasArc && target == ContinuityType.C1)
            {
                message = "Wierzchołek sąsiadujący z łukiem okręgu nie może mieć ciągłości C1 (dozwolone tylko G0 lub G1).";
                return false;
            }

            v.Continuity = target;
            v.ContinuityExplicitlySet = true;

            // Reguła wzajemnego wykluczania G1 dla łuku: ustawienie G1 na jednym końcu wymusza G0 na drugim końcu łuku
            if (target == ContinuityType.G1)
            {
                if (incoming.Type == EdgeType.Arc && incoming.Start.Continuity == ContinuityType.G1)
                {
                    incoming.Start.Continuity = ContinuityType.G0;
                    message = "Drugi koniec sąsiadującego łuku został automatycznie przełączony na G0.";
                }
                if (outgoing.Type == EdgeType.Arc && outgoing.End.Continuity == ContinuityType.G1)
                {
                    outgoing.End.Continuity = ContinuityType.G0;
                    message = "Drugi koniec sąsiadującego łuku został automatycznie przełączony na G0.";
                }
            }

            EnforceVertexContinuityFromVertex(poly, v);
            SolveScene(scene, pinnedVertex: v, activeControlPoint: null, preferredEdge: null);
            return true;
        }

        /// <summary>
        /// Główna pętla relaksacyjna solwera ograniczeń i ciągłości.
        /// </summary>
        public static void SolveScene(
            Scene scene,
            Vertex? pinnedVertex = null,
            ControlPoint? activeControlPoint = null,
            Edge? preferredEdge = null)
        {
            // Jeśli przeciągany jest punkt kontrolny Béziera, najpierw aplikujemy jego bezpośredni wpływ na sąsiada
            if (activeControlPoint != null)
            {
                var ownerPoly = scene.FindOwnerPolygon(activeControlPoint.ParentEdge);
                if (ownerPoly != null)
                {
                    ApplyControlPointMoveContinuity(ownerPoly, activeControlPoint);
                }
            }

            for (int iter = 0; iter < DefaultIterations; iter++)
            {
                // 1. Ograniczenia równoległości (w tym pomiędzy różnymi wielokątami)
                foreach (var pc in scene.ParallelConstraints)
                {
                    SolveParallelPair(scene, pc, pinnedVertex, preferredEdge, iter == 0);
                }

                // 2. Ograniczenia zadanej długości krawędzi
                foreach (var poly in scene.Polygons)
                {
                    foreach (var edge in poly.Edges)
                    {
                        if (edge.FixedLength.HasValue)
                        {
                            SolveFixedLength(edge, edge.FixedLength.Value, pinnedVertex);
                        }
                    }

                    // 3. Zabezpieczenie przed kolapsem sąsiednich krawędzi równoległych tego samego wielokąta
                    PreventAdjacentParallelCollapse(poly, pinnedVertex);
                }

                // 4. Więzy ciągłości G1 / C1 w wierzchołkach
                foreach (var poly in scene.Polygons)
                {
                    foreach (var v in poly.Vertices)
                    {
                        if (activeControlPoint != null)
                        {
                            Vertex touchedV = activeControlPoint.IsFirst
                                ? activeControlPoint.ParentEdge.Start
                                : activeControlPoint.ParentEdge.End;
                            if (v == touchedV)
                            {
                                ApplyControlPointMoveContinuity(poly, activeControlPoint);
                                continue;
                            }
                        }
                        EnforceVertexContinuityFromVertex(poly, v, pinnedVertex);
                    }
                }
            }
        }

        /// <summary>
        /// Obsługuje ruch punktu kontrolnego Béziera:
        /// - NIE przesuwa wierzchołków danego segmentu Béziera.
        /// - Aktualizuje przeciwległy punkt kontrolny (dla dwóch sąsiednich Bézierów)
        ///   lub obraca sąsiednią krawędź liniową wokół nieruchomego wierzchołka styku.
        /// </summary>
        public static void ApplyControlPointMoveContinuity(PolygonShape poly, ControlPoint movedCp)
        {
            Edge bezierEdge = movedCp.ParentEdge;
            Vertex sharedVertex = movedCp.IsFirst ? bezierEdge.Start : bezierEdge.End;

            if (sharedVertex.Continuity == ContinuityType.G0)
                return;

            var (incoming, outgoing) = poly.GetIncidentEdges(sharedVertex);
            Edge otherEdge = movedCp.IsFirst ? incoming : outgoing;

            double vx = sharedVertex.X - movedCp.X;
            double vy = sharedVertex.Y - movedCp.Y;
            double distCp = Math.Sqrt(vx * vx + vy * vy);

            if (distCp < Epsilon)
            {
                vx = 1.0;
                vy = 0.0;
                distCp = 1.0;
            }

            double ux = vx / distCp;
            double uy = vy / distCp;

            if (otherEdge.Type == EdgeType.Bezier)
            {
                ControlPoint otherCp = movedCp.IsFirst ? otherEdge.CP2 : otherEdge.CP1;
                double targetDist;

                if (sharedVertex.Continuity == ContinuityType.C1)
                {
                    // C1: wierzchołek jest dokładnie środkiem odcinka łączącego oba punkty kontrolne
                    targetDist = distCp;
                }
                else
                {
                    // G1: zachowujemy dotychczasową odległość drugiego punktu kontrolnego od wierzchołka
                    double odx = otherCp.X - sharedVertex.X;
                    double ody = otherCp.Y - sharedVertex.Y;
                    targetDist = Math.Sqrt(odx * odx + ody * ody);
                    if (targetDist < 5.0) targetDist = 40.0;
                }

                otherCp.X = sharedVertex.X + ux * targetDist;
                otherCp.Y = sharedVertex.Y + uy * targetDist;
            }
            else if (otherEdge.Type == EdgeType.Line)
            {
                // Sąsiednia krawędź jest odcinkiem: wierzchołek sharedVertex pozostaje nieruchomy,
                // natomiast drugi koniec odcinka ustawia się współliniowo.
                Vertex farVertex = movedCp.IsFirst ? otherEdge.Start : otherEdge.End;
                double currentEdgeLen = otherEdge.FixedLength ?? otherEdge.Length;
                if (currentEdgeLen < 5.0) currentEdgeLen = 60.0;

                if (sharedVertex.Continuity == ContinuityType.C1)
                {
                    if (otherEdge.FixedLength.HasValue)
                    {
                        // Jeśli krawędź ma stałą długość L i ciągłość C1, długość L jest nadrzędna,
                        // a punkt kontrolny dopasowuje swoją odległość do L / 3
                        double fixedL = otherEdge.FixedLength.Value;
                        farVertex.X = sharedVertex.X + ux * fixedL;
                        farVertex.Y = sharedVertex.Y + uy * fixedL;
                        double cpLen = fixedL / 3.0;
                        movedCp.X = sharedVertex.X - ux * cpLen;
                        movedCp.Y = sharedVertex.Y - uy * cpLen;
                    }
                    else
                    {
                        double targetLen = 3.0 * distCp;
                        farVertex.X = sharedVertex.X + ux * targetLen;
                        farVertex.Y = sharedVertex.Y + uy * targetLen;
                    }
                }
                else // G1
                {
                    farVertex.X = sharedVertex.X + ux * currentEdgeLen;
                    farVertex.Y = sharedVertex.Y + uy * currentEdgeLen;
                }
            }
        }

        /// <summary>
        /// Wymusza ciągłość G1/C1 w wierzchołku podczas ruchu wierzchołków lub relaksacji.
        /// </summary>
        public static void EnforceVertexContinuityFromVertex(PolygonShape poly, Vertex v, Vertex? pinnedVertex = null)
        {
            if (v.Continuity == ContinuityType.G0)
                return;

            var (incoming, outgoing) = poly.GetIncidentEdges(v);

            // Przypadek 1: Obie krawędzie to segmenty Béziera
            if (incoming.Type == EdgeType.Bezier && outgoing.Type == EdgeType.Bezier)
            {
                ControlPoint cpIn = incoming.CP2;
                ControlPoint cpOut = outgoing.CP1;

                double dInX = v.X - cpIn.X;
                double dInY = v.Y - cpIn.Y;
                double lenIn = Math.Sqrt(dInX * dInX + dInY * dInY);

                double dOutX = cpOut.X - v.X;
                double dOutY = cpOut.Y - v.Y;
                double lenOut = Math.Sqrt(dOutX * dOutX + dOutY * dOutY);

                if (lenIn < Epsilon) { dInX = 1; dInY = 0; lenIn = 40; }
                if (lenOut < Epsilon) { dOutX = 1; dOutY = 0; lenOut = 40; }

                // Uśredniony kierunek stycznej
                double tx = (dInX / lenIn) + (dOutX / lenOut);
                double ty = (dInY / lenIn) + (dOutY / lenOut);
                double tLen = Math.Sqrt(tx * tx + ty * ty);

                if (tLen < Epsilon)
                {
                    tx = dInX / lenIn;
                    ty = dInY / lenIn;
                }
                else
                {
                    tx /= tLen;
                    ty /= tLen;
                }

                if (v.Continuity == ContinuityType.C1)
                {
                    double avgLen = 0.5 * (lenIn + lenOut);
                    cpIn.X = v.X - tx * avgLen;
                    cpIn.Y = v.Y - ty * avgLen;
                    cpOut.X = v.X + tx * avgLen;
                    cpOut.Y = v.Y + ty * avgLen;
                }
                else // G1
                {
                    cpIn.X = v.X - tx * lenIn;
                    cpIn.Y = v.Y - ty * lenIn;
                    cpOut.X = v.X + tx * lenOut;
                    cpOut.Y = v.Y + ty * lenOut;
                }
            }
            // Przypadek 2: Incoming to Odcinek, Outgoing to Bézier
            else if (incoming.Type == EdgeType.Line && outgoing.Type == EdgeType.Bezier)
            {
                AlignBezierControlPointToLine(incoming, outgoing.CP1, v, isLineIncoming: true);
            }
            // Przypadek 3: Incoming to Bézier, Outgoing to Odcinek
            else if (incoming.Type == EdgeType.Bezier && outgoing.Type == EdgeType.Line)
            {
                AlignBezierControlPointToLine(outgoing, incoming.CP2, v, isLineIncoming: false);
            }
            // Przypadek 4: Bézier sąsiadujący z Łukiem (G1)
            else if (incoming.Type == EdgeType.Arc && outgoing.Type == EdgeType.Bezier)
            {
                AlignBezierToArc(poly, incoming, outgoing.CP1, v, arcIsIncoming: true);
            }
            else if (incoming.Type == EdgeType.Bezier && outgoing.Type == EdgeType.Arc)
            {
                AlignBezierToArc(poly, outgoing, incoming.CP2, v, arcIsIncoming: false);
            }
        }

        private static void AlignBezierControlPointToLine(Edge lineEdge, ControlPoint cp, Vertex shared, bool isLineIncoming)
        {
            Vertex otherLineVertex = isLineIncoming ? lineEdge.Start : lineEdge.End;
            double dx = shared.X - otherLineVertex.X;
            double dy = shared.Y - otherLineVertex.Y;
            double lineLen = Math.Sqrt(dx * dx + dy * dy);

            if (lineLen < Epsilon) return;

            double ux = dx / lineLen;
            double uy = dy / lineLen;

            double cpDx = cp.X - shared.X;
            double cpDy = cp.Y - shared.Y;
            double cpDist = Math.Sqrt(cpDx * cpDx + cpDy * cpDy);
            if (cpDist < 5.0) cpDist = lineLen / 3.0;

            double targetDist = (shared.Continuity == ContinuityType.C1) ? (lineLen / 3.0) : cpDist;
            cp.X = shared.X + ux * targetDist;
            cp.Y = shared.Y + uy * targetDist;
        }

        private static void AlignBezierToArc(PolygonShape poly, Edge arcEdge, ControlPoint cp, Vertex shared, bool arcIsIncoming)
        {
            // Jeśli łuk ma G1 na tym wierzchołku, styczna łuku podąża za punktem kontrolnym Béziera (automatycznie w Rasterizerze).
            // Jeśli łuk ma tu G0, a wierzchołek ma G1, dopasowujemy punkt kontrolny Béziera do stycznej łuku:
            var geom = Rendering.Rasterizer.ComputeArcGeometry(poly, arcEdge, recursionDepth: 1);
            if (geom.IsLineDegenerate) return;

            double rx = shared.X - geom.Center.X;
            double ry = shared.Y - geom.Center.Y;
            double rLen = Math.Sqrt(rx * rx + ry * ry);
            if (rLen < Epsilon) return;

            double sign = geom.SweepAngleRad >= 0 ? 1.0 : -1.0;
            if (!arcIsIncoming) sign = -sign;

            double tx = (-ry / rLen) * sign;
            double ty = (rx / rLen) * sign;

            double cpDist = Math.Sqrt((cp.X - shared.X) * (cp.X - shared.X) + (cp.Y - shared.Y) * (cp.Y - shared.Y));
            if (cpDist < 5.0) cpDist = 40.0;

            cp.X = shared.X + tx * cpDist;
            cp.Y = shared.Y + ty * cpDist;
        }

        private static void SolveFixedLength(Edge edge, double targetLength, Vertex? pinnedVertex)
        {
            double dx = edge.End.X - edge.Start.X;
            double dy = edge.End.Y - edge.Start.Y;
            double currentLen = Math.Sqrt(dx * dx + dy * dy);

            if (currentLen < Epsilon)
            {
                dx = 1.0;
                dy = 0.0;
                currentLen = 1.0;
            }

            double ux = dx / currentLen;
            double uy = dy / currentLen;
            double diff = currentLen - targetLength;

            bool startPinned = edge.Start == pinnedVertex;
            bool endPinned = edge.End == pinnedVertex;

            if (startPinned && !endPinned)
            {
                edge.End.X = edge.Start.X + ux * targetLength;
                edge.End.Y = edge.Start.Y + uy * targetLength;
            }
            else if (endPinned && !startPinned)
            {
                edge.Start.X = edge.End.X - ux * targetLength;
                edge.Start.Y = edge.End.Y - uy * targetLength;
            }
            else if (!startPinned && !endPinned)
            {
                edge.Start.X += ux * (diff * 0.5);
                edge.Start.Y += uy * (diff * 0.5);
                edge.End.X -= ux * (diff * 0.5);
                edge.End.Y -= uy * (diff * 0.5);
            }
        }

        private static void SolveParallelPair(
            Scene scene,
            ParallelConstraint pc,
            Vertex? pinnedVertex,
            Edge? preferredEdge,
            bool isFirstIter)
        {
            Edge eA = pc.EdgeA;
            Edge eB = pc.EdgeB;

            double ax = eA.End.X - eA.Start.X;
            double ay = eA.End.Y - eA.Start.Y;
            double lenA = Math.Sqrt(ax * ax + ay * ay);

            double bx = eB.End.X - eB.Start.X;
            double by = eB.End.Y - eB.Start.Y;
            double lenB = Math.Sqrt(bx * bx + by * by);

            if (lenA < Epsilon || lenB < Epsilon) return;

            double uax = ax / lenA;
            double uay = ay / lenA;
            double ubx = bx / lenB;
            double uby = by / lenB;

            // Zachowujemy zwrot (lub przeciwny zwrot), chyba że są to dwie sąsiednie krawędzie tego samego wielokąta
            bool areAdjacent = eA.End == eB.Start || eB.End == eA.Start || eA.Start == eB.Start || eA.End == eB.End;
            double dot = uax * ubx + uay * uby;

            if (!areAdjacent && dot < 0)
            {
                ubx = -ubx;
                uby = -uby;
            }
            else if (areAdjacent)
            {
                // Dla krawędzi sąsiadujących (np. eA: V0->V1, eB: V1->V2) wymuszamy zgodny zwrot,
                // aby zapobiec zapadnięciu się krawędzi w odcinek o zerowym kącie (180 stopni zwrotu).
                if (eA.End == eB.Start || eB.End == eA.Start)
                {
                    if (dot < 0)
                    {
                        // Nie odwracamy wektora docelowego - wymuszamy rozprostowanie zamiast złożenia
                    }
                }
            }

            bool aPinned = eA.Start == pinnedVertex || eA.End == pinnedVertex || eA == preferredEdge;
            bool bPinned = eB.Start == pinnedVertex || eB.End == pinnedVertex || eB == preferredEdge;

            double tx, ty;
            if (aPinned && !bPinned)
            {
                tx = uax;
                ty = uay;
                RotateEdgeToDirection(eB, tx, ty, lenB, pinnedVertex, !areAdjacent && (ax * bx + ay * by < 0));
            }
            else if (bPinned && !aPinned)
            {
                tx = ubx;
                ty = uby;
                RotateEdgeToDirection(eA, tx, ty, lenA, pinnedVertex, !areAdjacent && (ax * bx + ay * by < 0));
            }
            else
            {
                tx = uax + ubx;
                ty = uay + uby;
                double tLen = Math.Sqrt(tx * tx + ty * ty);
                if (tLen < Epsilon)
                {
                    tx = -uay;
                    ty = uax;
                }
                else
                {
                    tx /= tLen;
                    ty /= tLen;
                }

                RotateEdgeToDirection(eA, tx, ty, lenA, pinnedVertex, false);
                RotateEdgeToDirection(eB, tx, ty, lenB, pinnedVertex, !areAdjacent && (ax * bx + ay * by < 0));
            }
        }

        private static void RotateEdgeToDirection(
            Edge edge,
            double dirX,
            double dirY,
            double currentLen,
            Vertex? pinnedVertex,
            bool flipDirection)
        {
            if (flipDirection)
            {
                dirX = -dirX;
                dirY = -dirY;
            }

            double targetLen = edge.FixedLength ?? currentLen;

            if (edge.Start == pinnedVertex)
            {
                edge.End.X = edge.Start.X + dirX * targetLen;
                edge.End.Y = edge.Start.Y + dirY * targetLen;
            }
            else if (edge.End == pinnedVertex)
            {
                edge.Start.X = edge.End.X - dirX * targetLen;
                edge.Start.Y = edge.End.Y - dirY * targetLen;
            }
            else
            {
                double midX = 0.5 * (edge.Start.X + edge.End.X);
                double midY = 0.5 * (edge.Start.Y + edge.End.Y);
                edge.Start.X = midX - 0.5 * dirX * targetLen;
                edge.Start.Y = midY - 0.5 * dirY * targetLen;
                edge.End.X = midX + 0.5 * dirX * targetLen;
                edge.End.Y = midY + 0.5 * dirY * targetLen;
            }
        }

        private static void PreventAdjacentParallelCollapse(PolygonShape poly, Vertex? pinnedVertex)
        {
            int n = poly.Edges.Count;
            for (int i = 0; i < n; i++)
            {
                Edge e1 = poly.Edges[i];
                Edge e2 = poly.Edges[(i + 1) % n];

                if (e1.ParallelGroupIds.Count == 0 || e2.ParallelGroupIds.Count == 0)
                    continue;

                bool shareGroup = e1.ParallelGroupIds.Intersect(e2.ParallelGroupIds).Any();
                if (!shareGroup) continue;

                double dx1 = e1.End.X - e1.Start.X;
                double dy1 = e1.End.Y - e1.Start.Y;
                double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

                double dx2 = e2.End.X - e2.Start.X;
                double dy2 = e2.End.Y - e2.Start.Y;
                double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

                if (len1 < Epsilon || len2 < Epsilon) continue;

                // Jeśli wektory dwóch kolejnych krawędzi są skierowane przeciwnie (składają się na siebie),
                // odbijamy drugi koniec krawędzi e2, aby zachować niezdegenerowaną topologię
                double dot = (dx1 * dx2 + dy1 * dy2) / (len1 * len2);
                if (dot < -0.5)
                {
                    double ux = dx1 / len1;
                    double uy = dy1 / len1;
                    double targetL2 = e2.FixedLength ?? len2;
                    if (e2.End != pinnedVertex)
                    {
                        e2.End.X = e2.Start.X + ux * targetL2;
                        e2.End.Y = e2.Start.Y + uy * targetL2;
                    }
                    else if (e1.Start != pinnedVertex)
                    {
                        double targetL1 = e1.FixedLength ?? len1;
                        e1.Start.X = e1.End.X - (-ux) * targetL1;
                        e1.Start.Y = e1.End.Y - (-uy) * targetL1;
                    }
                }
            }
        }
    }
}