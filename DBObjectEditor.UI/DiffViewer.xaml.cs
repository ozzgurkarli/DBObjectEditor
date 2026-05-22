using DBObjectEditor.Common.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DBObjectEditor.UI
{
    public partial class DiffViewer : Window
    {
        private List<SPDiffModel> _spListesi;

        // Hangi modele ait hangi RichTextBox olduğunu tutuyoruz ki onaylarken içindeki metni okuyabilelim
        private Dictionary<SPDiffModel, RichTextBox> _rtbYeniListesi = new Dictionary<SPDiffModel, RichTextBox>();

        public DiffViewer(List<SPDiffModel> spListesi)
        {
            InitializeComponent();
            _spListesi = spListesi;
            SekmeleriOlustur();
        }

        private void SekmeleriOlustur()
        {
            foreach (var sp in _spListesi)
            {
                TabItem tabItem = new TabItem { Header = sp.SpAd };

                // Ana Grid (Sol, Splitter, Sağ)
                Grid mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // SOL PANE (Eski Kod)
                Grid gridEski = new Grid();
                gridEski.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                gridEski.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                TextBlock txtEski = new TextBlock { Text = "Mevcut SP Kodu (Silinenler Kırmızı)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
                RichTextBox rtbEski = new RichTextBox { FontFamily = new FontFamily("Consolas"), FontSize = 13, IsReadOnly = true, Background = Brushes.WhiteSmoke };

                Grid.SetRow(txtEski, 0);
                Grid.SetRow(rtbEski, 1);
                gridEski.Children.Add(txtEski);
                gridEski.Children.Add(rtbEski);
                Grid.SetColumn(gridEski, 0);
                mainGrid.Children.Add(gridEski);

                // ORTA SPLITTER
                GridSplitter splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.LightGray };
                Grid.SetColumn(splitter, 1);
                mainGrid.Children.Add(splitter);

                // SAĞ PANE (Yeni Kod)
                Grid gridYeni = new Grid();
                gridYeni.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                gridYeni.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                TextBlock txtYeni = new TextBlock { Text = "Üretilen Yeni SP Kodu (Eklenenler Yeşil)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
                RichTextBox rtbYeni = new RichTextBox { FontFamily = new FontFamily("Consolas"), FontSize = 13, IsReadOnly = false, Background = Brushes.White };

                Grid.SetRow(txtYeni, 0);
                Grid.SetRow(rtbYeni, 1);
                gridYeni.Children.Add(txtYeni);
                gridYeni.Children.Add(rtbYeni);
                Grid.SetColumn(gridYeni, 2);
                mainGrid.Children.Add(gridYeni);

                // PARAGRAFLARI DOLDUR VE RENKLENDİR
                Paragraph paraEski = new Paragraph();
                rtbEski.Document.Blocks.Add(paraEski);
                MetniDoldurVeRenklendir(paraEski, sp.EskiKod, sp.YeniKod, Brushes.MistyRose, true);

                Paragraph paraYeni = new Paragraph();
                rtbYeni.Document.Blocks.Add(paraYeni);
                MetniDoldurVeRenklendir(paraYeni, sp.YeniKod, sp.EskiKod, Brushes.LightGreen, false);

                // Sekmeyi tabControl'e ekle ve referansı sakla
                tabItem.Content = mainGrid;
                tabSpler.Items.Add(tabItem);

                _rtbYeniListesi.Add(sp, rtbYeni);
            }
        }

        private void MetniDoldurVeRenklendir(Paragraph paragraph, string kaynak, string kiyas, SolidColorBrush farkRengi, bool ustunuCiz)
        {
            if (string.IsNullOrEmpty(kaynak) || string.IsNullOrEmpty(kiyas)) return;

            var kaynakSatirlar = kaynak.Replace("\r", "").Split('\n');
            var kiyasSatirlar = new HashSet<string>(kiyas.Replace("\r", "").Split('\n').Select(s => s.Trim()));

            paragraph.Inlines.Clear();

            foreach (var satir in kaynakSatirlar)
            {
                var run = new Run(satir + Environment.NewLine);

                if (!string.IsNullOrWhiteSpace(satir) && !kiyasSatirlar.Contains(satir.Trim()))
                {
                    run.Background = farkRengi;
                    if (ustunuCiz)
                    {
                        run.TextDecorations = TextDecorations.Strikethrough;
                    }
                }

                paragraph.Inlines.Add(run);
            }
        }

        private void BtnOnay_Click(object sender, RoutedEventArgs e)
        {
            // Kullanıcının sekmeler üzerinde yaptığı manuel son düzeltmeleri okuyup modele işliyoruz
            foreach (var item in _rtbYeniListesi)
            {
                SPDiffModel model = item.Key;
                RichTextBox rtb = item.Value;

                TextRange textRange = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
                model.DuzenlenmisYeniKod = textRange.Text.TrimEnd('\r', '\n');
            }

            this.DialogResult = true;
        }

        private void BtnIptal_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}