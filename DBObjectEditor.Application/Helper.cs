using DBObjectEditor.Common.DTO;
using DBObjectEditor.Common.Enum;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.CompilerServices.RuntimeHelpers;

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
            string tipeOzelKurallar = string.Empty;

            var formatliParametreler = eklenenParametreler.Select(p =>
            {
                string temizParam = p.Trim();
                if (!temizParam.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
                {
                    temizParam = "p_" + temizParam;
                }
                return temizParam
                    .Replace("ı", "I")
                    .Replace("i", "I")
                    .Replace("ş", "S")
                    .Replace("ğ", "G")
                    .Replace("ü", "U")
                    .Replace("ö", "O")
                    .Replace("ç", "C")
                    .ToUpperInvariant();
            }).ToList();

            if (objeTuru == ObjectTypes.TriggerUpdate || objeTuru == ObjectTypes.TriggerDelete)
            {
                strObjeTuru = "Trigger";
                tipeOzelKurallar = @"
                - HEDEF 1: Koddaki 'INSERT INTO [TABLO_ADI]_H' bloğunu bul. SADECE <YENI_KOLONLAR> listesindeki alanları bu parantezin içindeki listeye ekle. (DİKKAT: Kolon isimlerinin başına KESİNLİKLE 'p_' veya 'P_' öneki EKLEME!)
                - HEDEF 2: 'VALUES' bloğunu bul. Eklediğin kolonların karşılıklarını ':OLD.KOLON_ADI' formatında VALUES parantezinin içine ekle. (Örnek Hatalı Kullanım: :OLD.P_TESTDOSYAADI -> YASAK. Örnek Doğru Kullanım: :OLD.TESTDOSYAADI -> ZORUNLU).
                - YASAK 1: Trigger'larda dışarıdan parametre olmaz. Bu yüzden <YENI_PARAMETRELER> listesini KESİNLİKLE DİKKATE ALMA ve koda hiçbir parametre ekleme.
                - YASAK 2: Trigger'ın DECLARE, BEGIN veya diğer mantıksal bloklarına KESİNLİKLE DOKUNMA.";
            }
            else if (objeTuru == ObjectTypes.Update)
            {
                strObjeTuru = "Update Stored Procedure";
                tipeOzelKurallar = @"
                - HEDEF 1: <YENI_PARAMETRELER>'i SP imza (signature) kısmına ekle.
                - HEDEF 2: Koddaki 'UPDATE [TABLO_ADI] SET' bloğunu bul.
                - HEDEF 3: <YENI_KOLONLAR>'ı <YENI_PARAMETRELER> ile eşleştir (Örn: KOLON = p_PARAM). Bu eşleşmeleri SET bloğundaki MEVCUT ATAMALARIN EN SONUNA, 'WHERE' komutundan hemen önceye ekle.
                - KURAL: Eklediğin ilk alanın başına veya mevcut son satırın sonuna virgül (,) koymayı unutma.";
            }
            else if (objeTuru == ObjectTypes.Insert)
            {
                strObjeTuru = "Insert Stored Procedure";
                tipeOzelKurallar = @"
                - HEDEF 1: <YENI_PARAMETRELER>'i SP imza (signature) kısmına ekle.
                - HEDEF 2: Koddaki 'INSERT INTO [TABLO_ADI]' parantezi içindeki kolon listesinin EN SONUNA <YENI_KOLONLAR>'ı ekle.
                - HEDEF 3: 'VALUES' parantezi içindeki listenin EN SONUNA <YENI_PARAMETRELER>'i ekle.
                - KURAL: Kolon sırası ile parametre sırasının birebir eşleştiğinden emin ol.";
            }
            else if (objeTuru == ObjectTypes.List)
            {
                strObjeTuru = "List (Dynamic Query) Stored Procedure";
                tipeOzelKurallar = @"
                - HEDEF 1: <YENI_PARAMETRELER>'i SP imza kısmına ekle.
                - HEDEF 2: Koddaki dinamik sorgu metnini (v_Query := 'SELECT ...') bul. <YENI_KOLONLAR>'ı FROM'dan hemen önce SELECT listesine ekle.
                - HEDEF 3: Eğer yeni parametreler varsa, bunları IF(p_PARAM IS NOT NULL) mantığıyla MEVCUT KODDAKİ gibi WHERE bloğuna dinamik olarak ekle.
                - YASAK: Tek tırnak (') kullanımında mevcut yapıyı bozma.";
            }
            else if (objeTuru == ObjectTypes.Select)
            {
                strObjeTuru = "Select Stored Procedure";
                tipeOzelKurallar = @"
                - HEDEF 1: <YENI_PARAMETRELER>'i SP imza kısmına ekle (Eğer yeni arama parametresi istenmişse).
                - HEDEF 2: Koddaki 'OPEN p_RC FOR SELECT' bloğunu bul. <YENI_KOLONLAR>'ı FROM'dan hemen önce SELECT listesine ekle.
                - YASAK: Eğer SELECT * kullanılıyorsa kolon eklemeye çalışma, dokunma.";
            }

            string evrenselKurallar = @"
            - SENİN ROLÜN: Sen bir 'Kod Birleştirici (Text Merger)' motorusun. Amacın yeni alanları mevcut koda, mevcut dokuyu ZERRE KADAR bozmadan enjekte etmektir.
            - YORUM YASAĞI (ÇOK KRİTİK): Ürettiğin koda KESİNLİKLE yeni bir yorum satırı (--, /*...*/) EKLEME. '-- yeni eklendi', '-- added here' gibi açıklamalar YASAKTIR.
            - MEVCUT YORUMLARI KORUMA: Koddaki mevcut /* CREATED BY ... */, /* DESCRIPTION ... */ veya diğer tüm yorum blokları SİLİNMEYECEK, birebir bırakılacak.
            - YAPISAL KORUMA: Eklemeleri yaptığın yerler hariç, kodun geri kalanındaki boşlukları (indentation), satır atlamalarını ve casing (büyük/küçük harf) yapısını BİREBİR KORU. Kodu formatlamaya veya güzelleştirmeye çalışma.
            - ÇIKTI FORMATI: Sadece ve sadece derlenebilir PL/SQL kodunu ver. Kod bloğu haricinde 'İşte kodunuz', 'Başarıyla güncellendi' gibi hiçbir selamlama veya onay cümlesi YAZMA.
            - BİTİŞ KURALI: Scriptin en sonuna tek başına '/' karakterini mutlaka koy.";

            string prompt = $@"
            Aşağıdaki {strObjeTuru} objesi için belirtilen eklemeleri yap.

            [EVRENSEL KURALLAR]
            {evrenselKurallar}

            [TİPE ÖZEL ENJEKSİYON HEDEFLERİ]
            {tipeOzelKurallar}

            <YENI_PARAMETRELER>
            {string.Join("\n", formatliParametreler)}
            </YENI_PARAMETRELER>

            <YENI_KOLONLAR>
            {string.Join("\n", eklenenKolonlar)}
            </YENI_KOLONLAR>

            <MEVCUT_KOD>
            {mevcutSpKodu}
            </MEVCUT_KOD>";

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

            // 1. Markdown etiketlerini Regex ile TEK SEFERDE ve KÖKTEN temizle.
            // @"```(?i)[a-z]*" -> 3 backtick ile başlayan ve ardından gelen tüm harfleri (sql, plsql, oracle vb.) siler.
            string temiz = Regex.Replace(hamSql, @"```(?i)[a-z]*", "");
            temiz = temiz.Trim();

            // 2. Bir önceki hatadan veya AI'dan ötürü en başta 'plsql', 'sql' gibi kelimeler kaldıysa onları da uçur.
            // ^(?i)(plsql|sql|oracle)\s+ -> En baştaki kelimeleri büyük/küçük harf duyarsız arar.
            temiz = Regex.Replace(temiz, @"^(?i)(plsql|sql|oracle)\s+", "");
            temiz = temiz.Trim();

            // 3. Sondaki execute '/' karakterini uçur.
            if (temiz.EndsWith("/"))
            {
                temiz = temiz.Substring(0, temiz.Length - 1).Trim();
            }

            // 4. DDL Başlığı Kontrolü
            if (!temiz.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                temiz = "CREATE OR REPLACE " + temiz;
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
                .Where(ea =>
                    (ea.Expression is IdentifierNameSyntax id && id.Identifier.Text == "row")
                    ||
                    (ea.Expression is ElementAccessExpressionSyntax innerEa
                        && innerEa.Expression is MemberAccessExpressionSyntax memberAccess
                        && memberAccess.Expression is IdentifierNameSyntax tableId
                        && tableId.Identifier.Text == "table"
                        && memberAccess.Name.Identifier.Text == "Rows")
                );

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
            switch (SPAd.Substring(4, 1))
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
