using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using PolygonEditor.Model;
using PolygonEditor.Rendering;
using PolygonEditor.Solver;

namespace PolygonEditor.UI
{
    public sealed class MainForm : Form
    {
        private readonly Scene _scene = new Scene();
        private DirectBitmap _canvasBuffer;
        private readonly PictureBox _canvasBox;
        private readonly RadioButton _rbBresenham;
        private readonly RadioButton _rbLibrary;
        private readonly Label _statusLabel;

        // Stan interakcji myszy
        private Vertex? _draggedVertex;
        private ControlPoint? _draggedControlPoint;
        private PolygonShape? _draggedPolygon;
        private Point _lastMousePos;

        // Tryb wyboru drugiej krawędzi dla relacji równoległości
        private Edge? _pendingParallelFirstEdge;

        // Elementy pod kursorem (hover)
        private Vertex? _hoveredVertex;
        private ControlPoint? _hoveredControlPoint;
        private Edge? _hoveredEdge;

        private const float HitRadius = 9.0f;

        public MainForm()
        {
            Text = "Zadanie 1 – Edytor wielokątów i wielokątów krzywoliniowych (Wariant A–K: Równoległość + G1)";
            Size = new Size(1240, 820);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            // Górny pasek narzędziowy
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(242, 244, 248),
                Padding = new Padding(10, 8, 10, 8)
            };

            Label lblAlgo = new Label
            {
                Text = "Algorytm rysowania odcinków:",
                AutoSize = true,
                Location = new Point(12, 14),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            _rbBresenham = new RadioButton
            {
                Text = "Własny algorytm Bresenhama (DirectBitmap)",
                AutoSize = true,
                Location = new Point(220, 12),
                Checked = true,
                Font = new Font("Segoe UI", 9.5f)
            };

            _rbLibrary = new RadioButton
            {
                Text = "Algorytm biblioteczny (Graphics.DrawLine)",
                AutoSize = true,
                Location = new Point(515, 12),
                Font = new Font("Segoe UI", 9.5f)
            };

            _rbBresenham.CheckedChanged += (s, e) => RenderScene();
            _rbLibrary.CheckedChanged += (s, e) => RenderScene();

            Button btnResetScene = new Button
            {
                Text = "Resetuj scenę początkową",
                Location = new Point(810, 8),
                Size = new Size(180, 30),
                Font = new Font("Segoe UI", 9f)
            };
            btnResetScene.Click += (s, e) =>
            {
                _pendingParallelFirstEdge = null;
                LoadInitialScene();
                SetStatus("Przywrócono domyślną scenę początkową.");
                RenderScene();
            };

            Button btnHelp = new Button
            {
                Text = "Dokumentacja / Pomoc",
                Location = new Point(1000, 8),
                Size = new Size(175, 30),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnHelp.Click += (s, e) =>
            {
                using var help = new HelpForm();
                help.ShowDialog(this);
            };

            topPanel.Controls.AddRange(new Control[] { lblAlgo, _rbBresenham, _rbLibrary, btnResetScene, btnHelp });

            // Dolny pasek stanu
            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Color.FromArgb(230, 234, 240)
            };

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = new Font("Segoe UI", 9f),
                Text = "Gotowy. LPM: przesuwanie wierzchołków / punktów kontrolnych / wielokątów | PPM: menu kontekstowe krawędzi lub wierzchołka."
            };
            bottomPanel.Controls.Add(_statusLabel);

            // Obszar rysowania
            _canvasBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            _canvasBuffer = new DirectBitmap(Width, Height);
            _canvasBox.Image = _canvasBuffer.Bitmap;

            _canvasBox.MouseDown += OnCanvasMouseDown;
            _canvasBox.MouseMove += OnCanvasMouseMove;
            _canvasBox.MouseUp += OnCanvasMouseUp;
            _canvasBox.Resize += OnCanvasResize;
            KeyDown += OnFormKeyDown;

            Controls.Add(_canvasBox);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);

            LoadInitialScene();
            RenderScene();
        }

        /// <summary>
        /// Predefiniowana scena początkowa zawierająca dwa wielokąty, krzywą Béziera, łuk okręgu,
        /// ograniczenie stałej długości oraz relacje równoległości (w tym między dwoma różnymi wielokątami).
        /// Dodanie kolejnych wielokątów sprowadza się do utworzenia obiektu PolygonShape i dodania go do _scene.Polygons.
        /// </summary>
        public void LoadInitialScene()
        {
            _scene.Clear();

            // Wielokąt 1 (pięciokąt po lewej stronie)
            var poly1 = new PolygonShape("Wielokąt 1", new[]
            {
                new PointF(140, 180),
                new PointF(420, 180),
                new PointF(500, 360),
                new PointF(340, 520),
                new PointF(140, 420)
            });

            // Wielokąt 2 (czworokąt po prawej stronie)
            var poly2 = new PolygonShape("Wielokąt 2", new[]
            {
                new PointF(680, 200),
                new PointF(980, 200),
                new PointF(1040, 460),
                new PointF(660, 460)
            });

            _scene.Polygons.Add(poly1);
            _scene.Polygons.Add(poly2);

            // 1. Zamiana krawędzi (1 -> 2) w Wielokącie 1 na krzywą Béziera 3. stopnia (domyślna ciągłość G1)
            ConstraintSolver.SetEdgeType(_scene, poly1, poly1.Edges[1], EdgeType.Bezier);
            poly1.Edges[1].CP1.X = 520;
            poly1.Edges[1].CP1.Y = 180;
            poly1.Edges[1].CP2.X = 595;
            poly1.Edges[1].CP2.Y = 290;

            // 2. Zamiana krawędzi (1 -> 2) w Wielokącie 2 na łuk okręgu z ciągłością G1
            ConstraintSolver.SetEdgeType(_scene, poly2, poly2.Edges[1], EdgeType.Arc);
            ConstraintSolver.SetVertexContinuity(_scene, poly2, poly2.Vertices[1], ContinuityType.G1, out _);

            // 3. Ograniczenie zadanej stałej długości na górnej krawędzi Wielokąta 1
            poly1.Edges[0].FixedLength = 280.0;

            // 4. Relacja równoległości wewnątrz Wielokąta 2 (górna i dolna krawędź)
            _scene.AddParallelConstraint(poly2.Edges[0], poly2.Edges[2], out _);

            // 5. Relacja równoległości POMIĘDZY różnymi wielokątami (lewa krawędź Wielokąta 1 i lewa krawędź Wielokąta 2)
            _scene.AddParallelConstraint(poly1.Edges[4], poly2.Edges[3], out _);

            ConstraintSolver.SolveScene(_scene);
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private void OnFormKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _pendingParallelFirstEdge != null)
            {
                _pendingParallelFirstEdge = null;
                SetStatus("Anulowano dodawanie relacji równoległości.");
                RenderScene();
            }
        }

        private void OnCanvasResize(object? sender, EventArgs e)
        {
            if (_canvasBox.Width <= 0 || _canvasBox.Height <= 0) return;
            var oldBuffer = _canvasBuffer;
            _canvasBuffer = new DirectBitmap(_canvasBox.Width, _canvasBox.Height);
            _canvasBox.Image = _canvasBuffer.Bitmap;
            oldBuffer?.Dispose();
            RenderScene();
        }

        private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
        {
            _lastMousePos = e.Location;

            // Obsługa wyboru drugiej krawędzi dla relacji równoległości
            if (_pendingParallelFirstEdge != null && e.Button == MouseButtons.Left)
            {
                var (_, clickedEdge) = HitTestEdge(e.Location);
                if (clickedEdge != null)
                {
                    if (_scene.AddParallelConstraint(_pendingParallelFirstEdge, clickedEdge, out string msg))
                    {
                        ConstraintSolver.SolveScene(_scene, preferredEdge: _pendingParallelFirstEdge);
                    }
                    _pendingParallelFirstEdge = null;
                    SetStatus(msg);
                    RenderScene();
                    return;
                }
                else
                {
                    _pendingParallelFirstEdge = null;
                    SetStatus("Anulowano wybór drugiej krawędzi (kliknięto poza krawędzią).");
                    RenderScene();
                    return;
                }
            }

            if (e.Button == MouseButtons.Left)
            {
                // Jeśli trzymany jest Shift, wymuszamy przesuwanie całego wielokąta
                if (ModifierKeys.HasFlag(Keys.Shift))
                {
                    _draggedPolygon = HitTestPolygon(e.Location);
                    return;
                }

                // 1. Sprawdzenie punktów kontrolnych Béziera
                var cp = HitTestControlPoint(e.Location);
                if (cp != null)
                {
                    _draggedControlPoint = cp;
                    return;
                }

                // 2. Sprawdzenie wierzchołków wielokątów
                var v = HitTestVertex(e.Location);
                if (v != null)
                {
                    _draggedVertex = v;
                    return;
                }

                // 3. Przesuwanie całego wielokąta (kliknięcie na krawędź lub wnętrze)
                _draggedPolygon = HitTestPolygon(e.Location);
            }
            else if (e.Button == MouseButtons.Right)
            {
                _pendingParallelFirstEdge = null;

                var v = HitTestVertex(e.Location);
                if (v != null)
                {
                    var ownerPoly = _scene.FindOwnerPolygon(v)!;
                    ShowVertexContextMenu(ownerPoly, v, e.Location);
                    return;
                }

                var (edgePoly, edge) = HitTestEdge(e.Location);
                if (edge != null && edgePoly != null)
                {
                    ShowEdgeContextMenu(edgePoly, edge, e.Location);
                }
            }
        }

        private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
        {
            int dx = e.X - _lastMousePos.X;
            int dy = e.Y - _lastMousePos.Y;

            if (_draggedControlPoint != null)
            {
                // Przesuwany punkt kontrolny zawsze podąża za kursorem myszy
                _draggedControlPoint.X = e.X;
                _draggedControlPoint.Y = e.Y;

                ConstraintSolver.SolveScene(
                    _scene,
                    pinnedVertex: null,
                    activeControlPoint: _draggedControlPoint,
                    preferredEdge: _draggedControlPoint.ParentEdge);

                _lastMousePos = e.Location;
                RenderScene();
                return;
            }

            if (_draggedVertex != null)
            {
                // Przesuwany wierzchołek zawsze podąża za kursorem myszy
                _draggedVertex.X = e.X;
                _draggedVertex.Y = e.Y;

                ConstraintSolver.SolveScene(
                    _scene,
                    pinnedVertex: _draggedVertex,
                    activeControlPoint: null,
                    preferredEdge: null);

                // Upewniamy się, że wierzchołek pod kursorem zachowuje dokładną pozycję myszy
                _draggedVertex.X = e.X;
                _draggedVertex.Y = e.Y;

                var poly = _scene.FindOwnerPolygon(_draggedVertex);
                if (poly != null)
                {
                    ConstraintSolver.EnforceVertexContinuityFromVertex(poly, _draggedVertex, _draggedVertex);
                }

                _lastMousePos = e.Location;
                RenderScene();
                return;
            }

            if (_draggedPolygon != null)
            {
                _draggedPolygon.Translate(dx, dy);
                _lastMousePos = e.Location;
                RenderScene();
                return;
            }

            // Aktualizacja podświetlenia pod kursorem
            var prevV = _hoveredVertex;
            var prevCp = _hoveredControlPoint;
            var prevE = _hoveredEdge;

            _hoveredControlPoint = HitTestControlPoint(e.Location);
            _hoveredVertex = _hoveredControlPoint == null ? HitTestVertex(e.Location) : null;
            _hoveredEdge = (_hoveredControlPoint == null && _hoveredVertex == null)
                ? HitTestEdge(e.Location).edge
                : null;

            if (prevV != _hoveredVertex || prevCp != _hoveredControlPoint || prevE != _hoveredEdge)
            {
                RenderScene();
            }

            _lastMousePos = e.Location;
        }

        private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
        {
            _draggedVertex = null;
            _draggedControlPoint = null;
            _draggedPolygon = null;
        }

        private void ShowVertexContextMenu(PolygonShape poly, Vertex v, Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem header = new ToolStripMenuItem($"Wierzchołek ({(int)v.X}, {(int)v.Y}) – Ciągłość: {v.Continuity}")
            {
                Enabled = false
            };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            foreach (ContinuityType ct in Enum.GetValues(typeof(ContinuityType)))
            {
                var item = new ToolStripMenuItem($"Ustaw ciągłość {ct}")
                {
                    Checked = v.Continuity == ct
                };
                item.Click += (s, ev) =>
                {
                    if (!ConstraintSolver.SetVertexContinuity(_scene, poly, v, ct, out string msg))
                    {
                        MessageBox.Show(msg, "Ograniczenie ciągłości", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (!string.IsNullOrEmpty(msg))
                    {
                        SetStatus(msg);
                    }
                    else
                    {
                        SetStatus($"Ustawiono ciągłość {ct} w wybranym wierzchołku.");
                    }
                    RenderScene();
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var deleteItem = new ToolStripMenuItem("Usuń wierzchołek");
            deleteItem.Click += (s, ev) =>
            {
                if (!_scene.RemoveVertex(poly, v, out string err))
                {
                    MessageBox.Show(err, "Operacja zablokowana", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    ConstraintSolver.SolveScene(_scene);
                    SetStatus("Usunięto wierzchołek i zaktualizowano relacje sąsiednich krawędzi.");
                    RenderScene();
                }
            };
            menu.Items.Add(deleteItem);

            menu.Show(_canvasBox, location);
        }

        private void ShowEdgeContextMenu(PolygonShape poly, Edge edge, Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem header = new ToolStripMenuItem($"{poly.Name} – Krawędź ({edge.Type}, dł. {edge.Length:F1})")
            {
                Enabled = false
            };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            // Przełączanie typu krawędzi
            var itemLine = new ToolStripMenuItem("Zwykły odcinek (Line)") { Checked = edge.Type == EdgeType.Line };
            itemLine.Click += (s, ev) => SwitchEdgeType(poly, edge, EdgeType.Line);

            var itemBezier = new ToolStripMenuItem("Segment Béziera 3. stopnia") { Checked = edge.Type == EdgeType.Bezier };
            itemBezier.Click += (s, ev) => SwitchEdgeType(poly, edge, EdgeType.Bezier);

            var itemArc = new ToolStripMenuItem("Łuk okręgu (Arc)") { Checked = edge.Type == EdgeType.Arc };
            itemArc.Click += (s, ev) => SwitchEdgeType(poly, edge, EdgeType.Arc);

            menu.Items.AddRange(new ToolStripItem[] { itemLine, itemBezier, itemArc, new ToolStripSeparator() });

            // Dodawanie wierzchołka na środku krawędzi
            var itemSplit = new ToolStripMenuItem("Dodaj wierzchołek w środku krawędzi");
            itemSplit.Click += (s, ev) =>
            {
                _scene.SplitEdge(poly, edge);
                ConstraintSolver.SolveScene(_scene);
                SetStatus("Dodano wierzchołek na środku krawędzi i usunięto ograniczenia dzielonej krawędzi.");
                RenderScene();
            };
            menu.Items.Add(itemSplit);
            menu.Items.Add(new ToolStripSeparator());

            // Ograniczenia geometryczne (dostępne dla odcinków)
            var itemLength = new ToolStripMenuItem("Ograniczenie: Zadana długość krawędzi...")
            {
                Enabled = edge.Type == EdgeType.Line
            };
            itemLength.Click += (s, ev) =>
            {
                using var dlg = new LengthDialog(edge.FixedLength ?? edge.Length);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    edge.FixedLength = dlg.SelectedLength;
                    ConstraintSolver.SolveScene(_scene, preferredEdge: edge);
                    SetStatus($"Ustawiono stałą długość krawędzi L = {dlg.SelectedLength:F1}.");
                    RenderScene();
                }
            };

            var itemParallel = new ToolStripMenuItem("Ograniczenie: Dodaj równoległość (wybierz 2. krawędź)...")
            {
                Enabled = edge.Type == EdgeType.Line
            };
            itemParallel.Click += (s, ev) =>
            {
                _pendingParallelFirstEdge = edge;
                SetStatus("TRYB RÓWNOLEGŁOŚCI: Kliknij lewym przyciskiem myszy na drugą krawędź (również z innego wielokąta). ESC anuluje.");
                RenderScene();
            };

            var itemClearConstraints = new ToolStripMenuItem("Usuń wszystkie ograniczenia z tej krawędzi")
            {
                Enabled = edge.FixedLength.HasValue || edge.ParallelGroupIds.Count > 0
            };
            itemClearConstraints.Click += (s, ev) =>
            {
                _scene.RemoveAllConstraintsFromEdge(edge);
                SetStatus("Usunięto ograniczenia geometryczne z wybranej krawędzi.");
                RenderScene();
            };

            menu.Items.AddRange(new ToolStripItem[] { itemLength, itemParallel, itemClearConstraints });
            menu.Show(_canvasBox, location);
        }

        private void SwitchEdgeType(PolygonShape poly, Edge edge, EdgeType targetType)
        {
            string info = ConstraintSolver.SetEdgeType(_scene, poly, edge, targetType);
            if (!string.IsNullOrEmpty(info))
            {
                SetStatus(info);
                MessageBox.Show(info, "Informacja o ciągłości", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                SetStatus($"Zmieniono typ krawędzi na: {targetType}.");
            }
            RenderScene();
        }

        #region Hit-Testing

        private ControlPoint? HitTestControlPoint(Point pt)
        {
            foreach (var poly in _scene.Polygons)
            {
                foreach (var edge in poly.Edges)
                {
                    if (edge.Type != EdgeType.Bezier) continue;
                    if (Distance(pt.X, pt.Y, edge.CP1.X, edge.CP1.Y) <= HitRadius) return edge.CP1;
                    if (Distance(pt.X, pt.Y, edge.CP2.X, edge.CP2.Y) <= HitRadius) return edge.CP2;
                }
            }
            return null;
        }

        private Vertex? HitTestVertex(Point pt)
        {
            foreach (var poly in _scene.Polygons)
            {
                foreach (var v in poly.Vertices)
                {
                    if (Distance(pt.X, pt.Y, v.X, v.Y) <= HitRadius)
                        return v;
                }
            }
            return null;
        }

        private (PolygonShape? poly, Edge? edge) HitTestEdge(Point pt)
        {
            foreach (var poly in _scene.Polygons)
            {
                foreach (var edge in poly.Edges)
                {
                    if (IsPointNearEdge(poly, edge, pt, HitRadius))
                        return (poly, edge);
                }
            }
            return (null, null);
        }

        private PolygonShape? HitTestPolygon(Point pt)
        {
            var (edgePoly, _) = HitTestEdge(pt);
            if (edgePoly != null) return edgePoly;

            foreach (var poly in _scene.Polygons)
            {
                if (IsPointInsidePolygon(poly, pt))
                    return poly;
            }
            return null;
        }

        private static bool IsPointNearEdge(PolygonShape poly, Edge edge, Point pt, double tolerance)
        {
            if (edge.Type == EdgeType.Line)
            {
                return DistancePointToSegment(pt.X, pt.Y, edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y) <= tolerance;
            }
            else if (edge.Type == EdgeType.Bezier)
            {
                const int samples = 40;
                double prevX = edge.Start.X;
                double prevY = edge.Start.Y;
                for (int i = 1; i <= samples; i++)
                {
                    double t = (double)i / samples;
                    double u = 1.0 - t;
                    double x = u * u * u * edge.Start.X + 3 * u * u * t * edge.CP1.X + 3 * u * t * t * edge.CP2.X + t * t * t * edge.End.X;
                    double y = u * u * u * edge.Start.Y + 3 * u * u * t * edge.CP1.Y + 3 * u * t * t * edge.CP2.Y + t * t * t * edge.End.Y;
                    if (DistancePointToSegment(pt.X, pt.Y, prevX, prevY, x, y) <= tolerance)
                        return true;
                    prevX = x;
                    prevY = y;
                }
                return false;
            }
            else // Arc
            {
                ArcGeometry geom = Rasterizer.ComputeArcGeometry(poly, edge);
                if (geom.IsLineDegenerate)
                    return DistancePointToSegment(pt.X, pt.Y, edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y) <= tolerance;

                const int samples = 40;
                double dAngle = geom.SweepAngleRad / samples;
                double angle = geom.StartAngleRad;
                double prevX = geom.Center.X + geom.Radius * Math.Cos(angle);
                double prevY = geom.Center.Y + geom.Radius * Math.Sin(angle);
                for (int i = 1; i <= samples; i++)
                {
                    angle += dAngle;
                    double x = geom.Center.X + geom.Radius * Math.Cos(angle);
                    double y = geom.Center.Y + geom.Radius * Math.Sin(angle);
                    if (DistancePointToSegment(pt.X, pt.Y, prevX, prevY, x, y) <= tolerance)
                        return true;
                    prevX = x;
                    prevY = y;
                }
                return false;
            }
        }

        private static bool IsPointInsidePolygon(PolygonShape poly, Point pt)
        {
            bool inside = false;
            int n = poly.Vertices.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly.Vertices[i].X, yi = poly.Vertices[i].Y;
                double xj = poly.Vertices[j].X, yj = poly.Vertices[j].Y;

                bool intersect = ((yi > pt.Y) != (yj > pt.Y)) &&
                                 (pt.X < (xj - xi) * (pt.Y - yi) / (yj - yi + 1e-12) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private static double Distance(double x1, double y1, double x2, double y2) =>
            Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

        private static double DistancePointToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-9) return Distance(px, py, x1, y1);
            double t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lenSq, 0.0, 1.0);
            return Distance(px, py, x1 + t * dx, y1 + t * dy);
        }

        #endregion

        #region Rendering

        private void RenderScene()
        {
            if (_canvasBuffer.Disposed) return;

            _canvasBuffer.Clear(Color.White);
            bool useBresenham = _rbBresenham.Checked;

            using Graphics g = Graphics.FromImage(_canvasBuffer.Bitmap);
            g.SmoothingMode = useBresenham ? SmoothingMode.None : SmoothingMode.AntiAlias;

            using Font labelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using Font contFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            using Pen libraryLinePen = new Pen(Color.FromArgb(35, 45, 65), 1.8f);
            using Pen hoveredEdgePen = new Pen(Color.FromArgb(0, 120, 215), 2.5f);

            foreach (var poly in _scene.Polygons)
            {
                // 1. Rysowanie krawędzi
                foreach (var edge in poly.Edges)
                {
                    bool isSelectedOrHovered = (edge == _hoveredEdge) || (edge == _pendingParallelFirstEdge);
                    Color edgeColor = edge == _pendingParallelFirstEdge
                        ? Color.Crimson
                        : (isSelectedOrHovered ? Color.FromArgb(0, 120, 215) : Color.FromArgb(35, 45, 65));

                    if (edge.Type == EdgeType.Line)
                    {
                        if (useBresenham)
                        {
                            Rasterizer.DrawLineBresenham(
                                _canvasBuffer,
                                (int)Math.Round(edge.Start.X),
                                (int)Math.Round(edge.Start.Y),
                                (int)Math.Round(edge.End.X),
                                (int)Math.Round(edge.End.Y),
                                edgeColor);
                        }
                        else
                        {
                            g.DrawLine(
                                isSelectedOrHovered ? hoveredEdgePen : libraryLinePen,
                                (float)edge.Start.X,
                                (float)edge.Start.Y,
                                (float)edge.End.X,
                                (float)edge.End.Y);
                        }
                    }
                    else if (edge.Type == EdgeType.Bezier)
                    {
                        // Wielobok kontrolny Béziera rysowany linią przerywaną
                        Color polyColor = Color.FromArgb(150, 160, 175);
                        Rasterizer.DrawDashedLineBresenham(
                            _canvasBuffer,
                            (int)Math.Round(edge.Start.X), (int)Math.Round(edge.Start.Y),
                            (int)Math.Round(edge.CP1.X), (int)Math.Round(edge.CP1.Y),
                            polyColor);
                        Rasterizer.DrawDashedLineBresenham(
                            _canvasBuffer,
                            (int)Math.Round(edge.CP1.X), (int)Math.Round(edge.CP1.Y),
                            (int)Math.Round(edge.CP2.X), (int)Math.Round(edge.CP2.Y),
                            polyColor);
                        Rasterizer.DrawDashedLineBresenham(
                            _canvasBuffer,
                            (int)Math.Round(edge.CP2.X), (int)Math.Round(edge.CP2.Y),
                            (int)Math.Round(edge.End.X), (int)Math.Round(edge.End.Y),
                            polyColor);

                        // Krzywa Béziera rysowana algorytmem przyrostowym (Forward Differencing)
                        Rasterizer.DrawCubicBezierForwardDiff(
                            _canvasBuffer,
                            g,
                            useBresenham,
                            edge.Start.ToPointF(),
                            edge.CP1.ToPointF(),
                            edge.CP2.ToPointF(),
                            edge.End.ToPointF(),
                            edge == _hoveredEdge ? Color.FromArgb(0, 120, 215) : Color.DarkMagenta);
                    }
                    else if (edge.Type == EdgeType.Arc)
                    {
                        Rasterizer.DrawArc(
                            _canvasBuffer,
                            g,
                            useBresenham,
                            poly,
                            edge,
                            edge == _hoveredEdge ? Color.FromArgb(0, 120, 215) : Color.Teal);
                    }

                    // 2. Wizualizacja aktywnych ograniczeń w pobliżu środka krawędzi
                    DrawEdgeConstraints(g, poly, edge, labelFont);
                }

                // 3. Rysowanie punktów kontrolnych Béziera
                foreach (var edge in poly.Edges.Where(e => e.Type == EdgeType.Bezier))
                {
                    DrawControlPoint(g, edge.CP1, "CP1", contFont);
                    DrawControlPoint(g, edge.CP2, "CP2", contFont);
                }

                // 4. Rysowanie wierzchołków głównych oraz oznaczeń klas ciągłości
                foreach (var v in poly.Vertices)
                {
                    bool isHovered = v == _hoveredVertex || v == _draggedVertex;
                    float r = isHovered ? 7.0f : 5.5f;
                    Brush fill = isHovered ? Brushes.DodgerBlue : Brushes.RoyalBlue;

                    g.FillEllipse(fill, (float)v.X - r, (float)v.Y - r, 2 * r, 2 * r);
                    g.DrawEllipse(Pens.White, (float)v.X - r, (float)v.Y - r, 2 * r, 2 * r);

                    // Wyświetlanie klasy ciągłości na wierzchołkach sąsiadujących z krzywą lub łukiem
                    if (poly.HasAdjacentBezier(v) || poly.HasAdjacentArc(v))
                    {
                        string badge = v.Continuity.ToString();
                        g.DrawString(badge, contFont, Brushes.DarkSlateBlue, (float)v.X + 8, (float)v.Y - 16);
                    }
                }
            }

            _canvasBox.Invalidate();
        }

        private void DrawControlPoint(Graphics g, ControlPoint cp, string tag, Font font)
        {
            bool isHovered = cp == _hoveredControlPoint || cp == _draggedControlPoint;
            float s = isHovered ? 6.5f : 5.0f;
            Brush brush = isHovered ? Brushes.OrangeRed : Brushes.DarkOrange;

            g.FillRectangle(brush, (float)cp.X - s, (float)cp.Y - s, 2 * s, 2 * s);
            g.DrawRectangle(Pens.Black, (float)cp.X - s, (float)cp.Y - s, 2 * s, 2 * s);
        }

        private static void DrawEdgeConstraints(Graphics g, PolygonShape poly, Edge edge, Font font)
        {
            bool hasLength = edge.FixedLength.HasValue;
            bool hasParallel = edge.ParallelGroupIds.Count > 0;
            if (!hasLength && !hasParallel) return;

            string text = string.Empty;
            if (hasLength)
                text += $"L:{edge.FixedLength!.Value:F0} ";
            if (hasParallel)
                text += string.Join(" ", edge.ParallelGroupIds.Select(id => $"||{id}"));

            text = text.Trim();
            PointF mid = edge.GetVisualMidpoint(poly);
            SizeF size = g.MeasureString(text, font);

            RectangleF box = new RectangleF(
                mid.X - size.Width * 0.5f - 4,
                mid.Y - size.Height * 0.5f - 2,
                size.Width + 8,
                size.Height + 4);

            using Brush bgBrush = new SolidBrush(Color.FromArgb(235, 255, 250, 205));
            using Pen borderPen = new Pen(Color.Goldenrod, 1f);

            g.FillRectangle(bgBrush, box);
            g.DrawRectangle(borderPen, box.X, box.Y, box.Width, box.Height);
            g.DrawString(text, font, Brushes.Maroon, box.X + 4, box.Y + 2);
        }

        #endregion
    }
}