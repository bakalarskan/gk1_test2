using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PolygonEditor.Rendering;

namespace PolygonEditor.Model
{
    public enum ContinuityType
    {
        G0,
        G1,
        C1
    }

    public enum EdgeType
    {
        Line,
        Bezier,
        Arc
    }

    public class Vertex
    {
        public double X { get; set; }
        public double Y { get; set; }
        public ContinuityType Continuity { get; set; } = ContinuityType.G1; // Domyślnie G1 dla wariantu A–K
        public bool ContinuityExplicitlySet { get; set; } = false;

        public Vertex(double x, double y, ContinuityType continuity = ContinuityType.G1, bool explicitCont = false)
        {
            X = x;
            Y = y;
            Continuity = continuity;
            ContinuityExplicitlySet = explicitCont;
        }

        public PointF ToPointF() => new PointF((float)X, (float)Y);
    }

    public class ControlPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public Edge ParentEdge { get; }
        public bool IsFirst { get; }

        public ControlPoint(double x, double y, Edge parentEdge, bool isFirst)
        {
            X = x;
            Y = y;
            ParentEdge = parentEdge;
            IsFirst = isFirst;
        }

        public PointF ToPointF() => new PointF((float)X, (float)Y);
    }

    public class Edge
    {
        public Vertex Start { get; set; }
        public Vertex End { get; set; }
        public EdgeType Type { get; set; } = EdgeType.Line;

        public ControlPoint CP1 { get; set; }
        public ControlPoint CP2 { get; set; }

        // Ograniczenia geometryczne
        public double? FixedLength { get; set; }
        public List<int> ParallelGroupIds { get; } = new List<int>();

        public Edge(Vertex start, Vertex end)
        {
            Start = start;
            End = end;
            CP1 = new ControlPoint(0, 0, this, true);
            CP2 = new ControlPoint(0, 0, this, false);
            ResetControlPoints();
        }

        public double Length
        {
            get
            {
                double dx = End.X - Start.X;
                double dy = End.Y - Start.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }

        public PointF ChordMidpoint => new PointF(
            (float)((Start.X + End.X) * 0.5),
            (float)((Start.Y + End.Y) * 0.5));

        public PointF GetVisualMidpoint(PolygonShape poly)
        {
            if (Type == EdgeType.Bezier)
            {
                // Punkt krzywej Béziera dla t = 0.5
                float x = (float)(0.125 * Start.X + 0.375 * CP1.X + 0.375 * CP2.X + 0.125 * End.X);
                float y = (float)(0.125 * Start.Y + 0.375 * CP1.Y + 0.375 * CP2.Y + 0.125 * End.Y);
                return new PointF(x, y);
            }
            if (Type == EdgeType.Arc)
            {
                ArcGeometry geom = Rasterizer.ComputeArcGeometry(poly, this);
                if (!geom.IsLineDegenerate)
                {
                    double midAngle = geom.StartAngleRad + geom.SweepAngleRad * 0.5;
                    return new PointF(
                        geom.Center.X + geom.Radius * (float)Math.Cos(midAngle),
                        geom.Center.Y + geom.Radius * (float)Math.Sin(midAngle));
                }
            }
            return ChordMidpoint;
        }

        public void ResetControlPoints()
        {
            double dx = End.X - Start.X;
            double dy = End.Y - Start.Y;
            CP1.X = Start.X + dx / 3.0;
            CP1.Y = Start.Y + dy / 3.0;
            CP2.X = Start.X + 2.0 * dx / 3.0;
            CP2.Y = Start.Y + 2.0 * dy / 3.0;
        }
    }

    public class ParallelConstraint
    {
        public int GroupId { get; }
        public Edge EdgeA { get; }
        public Edge EdgeB { get; }

        public ParallelConstraint(int groupId, Edge edgeA, Edge edgeB)
        {
            GroupId = groupId;
            EdgeA = edgeA;
            EdgeB = edgeB;
        }
    }

    public class PolygonShape
    {
        public string Name { get; set; }
        public List<Vertex> Vertices { get; } = new List<Vertex>();
        public List<Edge> Edges { get; } = new List<Edge>();

        public PolygonShape(string name, IEnumerable<PointF> points)
        {
            Name = name;
            foreach (var pt in points)
            {
                Vertices.Add(new Vertex(pt.X, pt.Y));
            }
            RebuildEdgesFromVertices();
        }

        private void RebuildEdgesFromVertices()
        {
            Edges.Clear();
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vertex v1 = Vertices[i];
                Vertex v2 = Vertices[(i + 1) % Vertices.Count];
                Edges.Add(new Edge(v1, v2));
            }
        }

        public (Edge prevEdge, Edge nextEdge) GetAdjacentEdges(Edge edge)
        {
            int idx = Edges.IndexOf(edge);
            int prevIdx = (idx - 1 + Edges.Count) % Edges.Count;
            int nextIdx = (idx + 1) % Edges.Count;
            return (Edges[prevIdx], Edges[nextIdx]);
        }

        public (Edge incoming, Edge outgoing) GetIncidentEdges(Vertex v)
        {
            int idx = Vertices.IndexOf(v);
            Edge incoming = Edges[(idx - 1 + Edges.Count) % Edges.Count];
            Edge outgoing = Edges[idx];
            return (incoming, outgoing);
        }

        public bool HasAdjacentBezier(Vertex v)
        {
            var (inc, outg) = GetIncidentEdges(v);
            return inc.Type == EdgeType.Bezier || outg.Type == EdgeType.Bezier;
        }

        public bool HasAdjacentArc(Vertex v)
        {
            var (inc, outg) = GetIncidentEdges(v);
            return inc.Type == EdgeType.Arc || outg.Type == EdgeType.Arc;
        }

        public void Translate(double dx, double dy)
        {
            foreach (var v in Vertices)
            {
                v.X += dx;
                v.Y += dy;
            }
            foreach (var e in Edges)
            {
                if (e.Type == EdgeType.Bezier)
                {
                    e.CP1.X += dx;
                    e.CP1.Y += dy;
                    e.CP2.X += dx;
                    e.CP2.Y += dy;
                }
            }
        }
    }

    public class Scene
    {
        public List<PolygonShape> Polygons { get; } = new List<PolygonShape>();
        public List<ParallelConstraint> ParallelConstraints { get; } = new List<ParallelConstraint>();
        private int _nextParallelId = 1;

        public void Clear()
        {
            Polygons.Clear();
            ParallelConstraints.Clear();
            _nextParallelId = 1;
        }

        public PolygonShape? FindOwnerPolygon(Edge edge) =>
            Polygons.FirstOrDefault(p => p.Edges.Contains(edge));

        public PolygonShape? FindOwnerPolygon(Vertex vertex) =>
            Polygons.FirstOrDefault(p => p.Vertices.Contains(vertex));

        public bool AddParallelConstraint(Edge e1, Edge e2, out string message)
        {
            if (e1 == e2)
            {
                message = "Nie można dodać relacji równoległości krawędzi z samą sobą.";
                return false;
            }

            if (e1.Type != EdgeType.Line || e2.Type != EdgeType.Line)
            {
                message = "Relację równoległości można definiować tylko dla krawędzi będących odcinkami.";
                return false;
            }

            bool alreadyLinked = ParallelConstraints.Any(c =>
                (c.EdgeA == e1 && c.EdgeB == e2) || (c.EdgeA == e2 && c.EdgeB == e1));

            if (alreadyLinked)
            {
                message = "Te dwie krawędzie mają już wspólną relację równoległości.";
                return false;
            }

            int id = _nextParallelId++;
            var constraint = new ParallelConstraint(id, e1, e2);
            ParallelConstraints.Add(constraint);
            e1.ParallelGroupIds.Add(id);
            e2.ParallelGroupIds.Add(id);

            message = $"Dodano relację równoległości ||{id}.";
            return true;
        }

        public void RemoveAllConstraintsFromEdge(Edge edge)
        {
            edge.FixedLength = null;
            var toRemove = ParallelConstraints
                .Where(c => c.EdgeA == edge || c.EdgeB == edge)
                .ToList();

            foreach (var c in toRemove)
            {
                c.EdgeA.ParallelGroupIds.Remove(c.GroupId);
                c.EdgeB.ParallelGroupIds.Remove(c.GroupId);
                ParallelConstraints.Remove(c);
            }
        }

        /// <summary>
        /// Dodaje nowy wierzchołek w środku wybranej krawędzi, usuwając wszystkie jej ograniczenia
        /// oraz aktualizując topologię sąsiednich krawędzi.
        /// </summary>
        public Vertex SplitEdge(PolygonShape poly, Edge edge)
        {
            RemoveAllConstraintsFromEdge(edge);

            PointF mid = edge.ChordMidpoint;
            Vertex newVertex = new Vertex(mid.X, mid.Y, ContinuityType.G1, false);

            int edgeIdx = poly.Edges.IndexOf(edge);
            int startVertexIdx = poly.Vertices.IndexOf(edge.Start);

            poly.Vertices.Insert(startVertexIdx + 1, newVertex);

            Edge e1 = new Edge(edge.Start, newVertex);
            Edge e2 = new Edge(newVertex, edge.End);

            poly.Edges.RemoveAt(edgeIdx);
            poly.Edges.Insert(edgeIdx, e2);
            poly.Edges.Insert(edgeIdx, e1);

            return newVertex;
        }

        /// <summary>
        /// Usuwa wierzchołek z wielokąta (jeśli ma więcej niż 3 wierzchołki),
        /// czyszcząc ograniczenia usuwanych krawędzi i łącząc sąsiednie wierzchołki nowym odcinkiem.
        /// </summary>
        public bool RemoveVertex(PolygonShape poly, Vertex v, out string error)
        {
            if (poly.Vertices.Count <= 3)
            {
                error = "Nie można usunąć wierzchołka – wielokąt musi posiadać co najmniej 3 wierzchołki.";
                return false;
            }

            int vIdx = poly.Vertices.IndexOf(v);
            int prevEdgeIdx = (vIdx - 1 + poly.Edges.Count) % poly.Edges.Count;
            int nextEdgeIdx = vIdx;

            Edge incoming = poly.Edges[prevEdgeIdx];
            Edge outgoing = poly.Edges[nextEdgeIdx];

            Vertex prevVertex = incoming.Start;
            Vertex nextVertex = outgoing.End;

            RemoveAllConstraintsFromEdge(incoming);
            RemoveAllConstraintsFromEdge(outgoing);

            Edge mergedEdge = new Edge(prevVertex, nextVertex);

            poly.Vertices.RemoveAt(vIdx);
            poly.Edges[prevEdgeIdx] = mergedEdge;
            poly.Edges.RemoveAt(nextEdgeIdx);

            error = string.Empty;
            return true;
        }
    }
}