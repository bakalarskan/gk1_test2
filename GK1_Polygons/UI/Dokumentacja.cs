using System.Drawing;
using System.Windows.Forms;

namespace PolygonEditor.UI
{
    public sealed class HelpForm : Form
    {
        public HelpForm()
        {
            Text = "Dokumentacja – Edytor wielokątów i wielokątów krzywoliniowych";
            Size = new Size(760, 620);
            StartPosition = FormStartPosition.CenterParent;

            RichTextBox rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Padding = new Padding(12),
                Text = GetDocumentationText()
            };

            Controls.Add(rtb);
        }

        private static string GetDocumentationText() =>
@"DOKUMENTACJA APLIKACJI – EDYTOR WIELOKĄTÓW (WARIANT A–K)
====================================================================

1. INSTRUKCJA OBSŁUGI (MYSZ I KLAWISZOLOGIA)
--------------------------------------------------------------------
• Lewy Przycisk Myszy (LPM) – przeciąganie wierzchołka:
  Najedź na wierzchołek (niebieski okrąg) i przeciągnij go. Solwer w czasie rzeczywistym zachowuje ograniczenia długości, równoległości oraz ciągłości G1/C1.

• Lewy Przycisk Myszy (LPM) – przeciąganie punktu kontrolnego Béziera:
  Najedź na punkt kontrolny (pomarańczowy kwadrat) i przeciągnij go. Wierzchołki tego segmentu Béziera pozostają nieruchome, natomiast sąsiednie punkty kontrolne lub sąsiednie krawędzie dopasowują się zgodnie z klasą ciągłości (G0 / G1 / C1).

• Lewy Przycisk Myszy (LPM) – przesuwanie całego wielokąta:
  Złap lewym przyciskiem myszy dowolną krawędź lub wnętrze wielokąta (bądź przytrzymaj klawisz Shift podczas przeciągania) i przesuń kursor.

• Prawy Przycisk Myszy (PPM) na KRAWĘDZI – menu kontekstowe krawędzi:
  - Przełączanie typu: Odcinek / Segment Béziera 3. stopnia / Łuk okręgu.
  - Dodaj wierzchołek na środku krawędzi (usuwa ograniczenia dzielonej krawędzi).
  - Ustaw zadaną długość krawędzi (domyślnie aktualna długość).
  - Dodaj relację równoległości (po kliknięciu wybierz LPM drugą krawędź dowolnego wielokąta).
  - Usuń wszystkie ograniczenia z wybranej krawędzi.

• Prawy Przycisk Myszy (PPM) na WIERZCHOŁKU – menu kontekstowe wierzchołka:
  - Zmiana klasy ciągłości: G0 (ciągłość położenia), G1 (ciągłość kierunku stycznej), C1 (ciągłość wektora stycznego).
  - Usuń wierzchołek (blokowane, gdy wielokąt ma minimalną liczbę 3 wierzchołków).

• Klawisz ESC:
  - Anuluje tryb wybierania drugiej krawędzi do relacji równoległości.

2. OPIS PRZYJĘTYCH ZAŁOŻEŃ (WARIANT A–K)
--------------------------------------------------------------------
• Ograniczenie relacyjne par krawędzi: RÓWNOLEGŁOŚĆ (||), działająca również pomiędzy krawędziami różnych wielokątów.
• Domyślna klasa ciągłości po przełączeniu krawędzi na segment Béziera: G1 (o ile ciągłość w wierzchołku nie została wcześniej jawnie ustawiona przez użytkownika).
• Łuk okręgu obsługuje wyłącznie klasy G0 oraz G1. Tylko jeden koniec łuku może mieć ciągłość G1 – włączenie G1 na jednym końcu automatycznie ustawia G0 na drugim końcu. Gdy oba końce mają G0, łuk jest półokręgiem o promieniu R = długość cięciwy / 2.

3. MODEL DANYCH I ARCHITEKTURA
--------------------------------------------------------------------
• Model (PolygonEditor.Model):
  - Scene: przechowuje kolekcję wielokątów (PolygonShape) oraz globalny rejestr więzów równoległości (ParallelConstraint).
  - PolygonShape: przechowuje cykliczną listę wierzchołków (Vertex) oraz krawędzi (Edge).
  - Edge: przechowuje referencje na wierzchołki Start i End, dwa punkty kontrolne (CP1, CP2), typ krawędzi (Line, Bezier, Arc), opcjonalną stałą długość (FixedLength) oraz identyfikatory grup równoległości (ParallelGroupIds).
• Renderer (PolygonEditor.Rendering):
  - DirectBitmap: bezpośredni bufor pamięci (int[] Bits przypięty przez GCHandleType.Pinned), całkowicie eliminujący wolne wywołania SetPixel.

4. ZAIMPLEMENTOWANE ALGORYTMY
--------------------------------------------------------------------
• Algorytm Bresenhama:
  Pełna implementacja całkowitoliczbowa we wszystkich 8 oktantach zapisująca kolor bezpośrednio do tablicy Bits[y * Width + x].
• Przyrostowy algorytm Béziera (Forward Differencing):
  Przelicza punkty kontrolne z bazy Bernsteina do bazy potęgowej (A0, A1, A2, A3), wyznacza różnice skończone ΔP, Δ²P, Δ³P dla kroku d = 1/160 i generuje kolejne punkty krzywej w pętli wyłącznie za pomocą operacji dodawania.
• Iteracyjny solwer relaksacyjny (ConstraintSolver):
  Wykonuje 28 sub-iteracji przy każdej zmianie położenia, rzutując ograniczenia równoległości, stałej długości oraz ciągłości G1/C1 z ochroną przed kolapsem sąsiednich krawędzi równoległych.";
    }
}