using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DBObjectEditor.Common.DTO;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using DBObjectEditor.Common.Enum;

namespace DBObjectEditor.Application
{
    public class Helper
    {
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            BaseAddress = new Uri("https://models.github.ai/inference/")
        };


        public static async Task<SPDiffModel> UretVeModeleCevirAsync(string apiKey, string model, string spAd, string mevcutSpKodu, List<string> eklenenParametreler, List<string> eklenenKolonlar, ObjectTypes objeTuru)
        {
            string uretilenSql = await CopilotIleSqlUret(apiKey, model, spAd, mevcutSpKodu, eklenenParametreler, eklenenKolonlar, objeTuru);

            return new SPDiffModel
            {
                SpAd = spAd,
                EskiKod = mevcutSpKodu,
                YeniKod = uretilenSql,
                DuzenlenmisYeniKod = uretilenSql
            };
        }

        /// <summary>
        /// api cagirilip sp duzenlenir
        /// </summary>
        /// <param name="apiKey"></param>
        /// <param name="model"></param>
        /// <param name="spAd"></param>
        /// <param name="mevcutSpKodu"></param>
        /// <param name="eklenenParametreler"></param>
        /// <param name="eklenenKolonlar"></param>
        /// <returns></returns>
        public static async Task<string> CopilotIleSqlUret(string apiKey, string model, string spAd, string mevcutSpKodu, List<string> eklenenParametreler, List<string> eklenenKolonlar, ObjectTypes objeTuru)
        {
            string strObjeTuru = string.Empty;
            string ozelIstenenGuncellemeler = string.Empty;
            string ozelIstenenKurallar = string.Empty;

            if (objeTuru == ObjectTypes.TriggerUpdate || objeTuru == ObjectTypes.TriggerDelete)
            {
                strObjeTuru = "Trigger";

                ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler,
                    "- <YENI_KOLONLAR> içindeki alanları Trigger gövdesindeki 'INSERT INTO' kolon listesine ekle.\n");

                ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler,
                    "- VALUES kısmına bu kolonları eklerken, mevcut koddaki prefix kullanımını (örneğin loglama için :OLD.KOLON_ADI veya :NEW.KOLON_ADI) birebir devam ettir.\n");

                ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler,
                    "- DİKKAT: INSERT INTO ve VALUES bloklarındaki sıralamanın birbiriyle BİREBİR eşleştiğinden emin ol.\n");

                ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler,
                    "- KRİTİK: Eğer VALUES listesinin sonunda sistemsel/sabit atamalar (Örn: APP_CALLERINFORMATION, APP_DATE(), 1 vb.) varsa, yeni kolonları kesinlikle en sona DEĞİL, tablodan gelen kolonların bittiği yere, yani sabit değerlerden hemen önceye ekle.\n");
            }
            else
            {
                strObjeTuru = "Stored Procedure (SP)";

                if (objeTuru == ObjectTypes.Insert) // Kendi enum/sabit tipinize göre ayarlayın
                {
                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- <YENI_KOLONLAR> içindeki alanları, gövdedeki 'INSERT INTO' komutunun kolon listesine (parantez içine) ekle.\n");
                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- Eklediğin bu yeni kolonlara karşılık gelen parametreleri, 'INSERT INTO' daki sırayı BİREBİR koruyarak 'VALUES' komutunun listesine ekle.\n");
                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- DİKKAT: INSERT INTO içindeki kolon sırası ile VALUES içindeki parametre sırası kesinlikle birbiriyle eşleşmeli ve yapısal bütünlük bozulmamalıdır.\n");
                }
                else if (objeTuru == ObjectTypes.Update) // Kendi enum/sabit tipinize göre ayarlayın
                {
                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- <YENI_KOLONLAR> içindeki alanları, gövdedeki 'UPDATE' komutunun 'SET' bloğuna ekle.\n");

                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- Eklenen her bir yeni kolon için, <YENI_PARAMETRELER> içindeki ilgili parametreyi eşleştirerek atamasını yap (Örn: KOLON_ADI = p_PARAMETRE_ADI).\n");

                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- DİKKAT: 'SET' bloğunun en sonuna yeni alanları eklerken, bir önceki mevcut satırın sonuna virgül (,) koymayı KESİNLİKLE unutma. Sentaks hatası olmamalıdır.\n");

                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- DİKKAT: Mevcut 'SET' atamalarındaki boşluk/hizalama (indentation) yapısını yeni eklediğin kolonlar için de BİREBİR koru.\n");

                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- KRİTİK: 'UPDATE' komutunun 'WHERE' koşullarına kesinlikle dokunma, sadece 'SET' kısmını güncelle.\n");
                }
                else
                {
                    ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- <YENI_KOLONLAR> içindeki alanları SP içindeki ilgili SELECT sorgularına/OUT imlasına ekle.\n");

                }

                if (objeTuru == ObjectTypes.List)
                {
                    ozelIstenenGuncellemeler = "- Yeni eklenen IN parametrelerini içerideki ana sorgunun WHERE koşuluna (yapıyı bozmadan, örn: AND KOLON = P_PARAM) mutlaka dahil et.\n";
                }

                ozelIstenenGuncellemeler = string.Concat(ozelIstenenGuncellemeler, "- <YENI_PARAMETRELER> içindeki parametreleri Stored Procedure (SP)'nin imza (signature) kısmına uygun veri tipleriyle ekle.\n");
            }

            string prompt = $@"GÖREV:
            Sen uzman bir Oracle PL/SQL geliştiricisisin. Aşağıda verilen mevcut {strObjeTuru}, belirtilen girdilere göre güncelleyip yeniden oluşturman gerekiyor.

            İSTENEN GÜNCELLEMELER:
            {ozelIstenenGuncellemeler}

            KATI FORMAT KURALLARI (BUNLARA KESİNLİKLE UYULACAK):
            - Scriptin Oracle'da doğrudan derlenebilir ve commitlenebilir olması için gerekli olan DDL komutlarını (örneğin mevcut değilse 'CREATE OR REPLACE PROCEDURE ...', 'ALTER ...' vb.) kodun en başına mutlaka ekle.
            - Sadece istenen parametre ve kolon eklemelerini yap.
            - MEVCUT_KOD'un gövdesini (boşluklar, alt satıra geçişler, büyük/küçük harf kullanımı, mevcut yorum satırları dahil) BİREBİR KORU.
            - Kodun yapısını 'güzelleştirmeye' çalışma, ilgisiz satır düzeltmeleri veya formatlama (indentation) KESİNLİKLE YAPMA.
            - Selamlama, onay, açıklama KESİNLİKLE YAZMA.Markdown formatı KESİNLİKLE KULLANMA.
            -Scriptin en sonuna derleme için '/' karakterini ekle.
            {ozelIstenenKurallar}

            GİRDİLER:
            <{strObjeTuru}_ADI>{spAd}</{strObjeTuru}_ADI>

            <YENI_PARAMETRELER>
            {string.Join(", ", eklenenParametreler)}
            </YENI_PARAMETRELER>

            <YENI_KOLONLAR>
            {string.Join(", ", eklenenKolonlar)}
            </YENI_KOLONLAR>

            <MEVCUT_KOD>
            {mevcutSpKodu}
            </MEVCUT_KOD>

            <!-- REQ_ID: {Guid.NewGuid()} -->";

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                        new { role = "user", content = prompt }
                    },
                temperature = 0.0 // halusinasyonu onlemek icin
            };

            string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            request.Content = content;

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new Exception($"ERROR API: {response.StatusCode} - {error}");
            }

            string responseString = await response.Content.ReadAsStringAsync();
            var jsonResponse = JObject.Parse(responseString);

            string uretilenScript = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();

            if (string.IsNullOrEmpty(uretilenScript))
            {
                throw new Exception("AI response alınamadı.");
            }

            return Helper.AiSqlTemizle(uretilenScript);
        }


        // AI'dan gelen metni OracleCommand'in anlayacağı saf SQL'e çeviren yardımcı metot
        public static string AiSqlTemizle(string hamSql)
        {
            if (string.IsNullOrWhiteSpace(hamSql)) return hamSql;

            // 1. Markdown etiketlerini temizle
            string temiz = hamSql.Replace("```sql", "").Replace("```SQL", "").Replace("```", "");
            temiz = temiz.Trim();

            // 2. En sondaki "/" işaretini temizle (Oracle.ManagedDataAccess sondaki slash'i sevmez)
            if (temiz.EndsWith("/"))
            {
                temiz = temiz.Substring(0, temiz.Length - 1).Trim();
            }

            return temiz;
        }

        public static List<SPAnalizSonuc> RoslynAnalizEt(string sourceCode)
        {
            var results = new List<SPAnalizSonuc>();
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetCompilationUnitRoot();

            // 1. Dosyadaki TÜM metotları bul
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            // 2. Her bir metodu kendi içinde ayrı ayrı incele
            foreach (var method in methods)
            {
                var executeInvocation = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault(inv => inv.Expression is MemberAccessExpressionSyntax ma
                                        && (ma.Name.Identifier.Text == "ExecuteDataSet" || ma.Name.Identifier.Text == "ExecuteNonQuery"));

                // Eğer bu metot veritabanına gitmiyorsa (ExecuteDataSet yoksa) atla
                if (executeInvocation == null || executeInvocation.ArgumentList.Arguments.Count == 0)
                    continue;

                var spAd = (executeInvocation.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax)?.Token.ValueText;
                if (string.IsNullOrEmpty(spAd)) continue;

                var result = new SPAnalizSonuc
                {
                    MetodAd = method.Identifier.Text,
                    SPAd = spAd,
                    ObjeTuru = AyarlaObjeTuru(spAd)
                };

                var inParameters = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(inv => inv.Expression is MemberAccessExpressionSyntax ma
                               && ma.Name.Identifier.Text == "AddInParameter");

                foreach (var param in inParameters)
                {
                    if (param.ArgumentList.Arguments.Count > 0)
                    {
                        var paramName = (param.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax)?.Token.ValueText;
                        if (!string.IsNullOrEmpty(paramName))
                            result.InParametreler.Add(paramName);
                    }
                }

                var rowAccesses = method.DescendantNodes()
                    .OfType<ElementAccessExpressionSyntax>()
                    .Where(ea => ea.Expression is IdentifierNameSyntax id && id.Identifier.Text == "row");

                result.OutSutunlar = rowAccesses
                    .Select(ea => (ea.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax)?.Token.ValueText)
                    .Where(col => !string.IsNullOrEmpty(col))
                    .Distinct()
                    .ToList();

                results.Add(result);
            }

            return results;
        }

        public static ObjectTypes AyarlaObjeTuru(string SPAd)
        {
            switch(SPAd.Substring(4, 1))
            {
                case "I":
                    return ObjectTypes.Insert;
                case "U":
                    return ObjectTypes.Update;
                case "S":
                    return ObjectTypes.Select;
                case "L":
                    return ObjectTypes.List;
                case "D":
                    return ObjectTypes.Delete;
                case "T":
                    switch (SPAd.Substring(6, 1))
                    {
                        case "U":
                            return ObjectTypes.TriggerUpdate;
                        case "D":
                            return ObjectTypes.TriggerDelete;
                        default:
                            return ObjectTypes.None;
                    }
                default:
                    return ObjectTypes.None;
            }

        }
    }
}
