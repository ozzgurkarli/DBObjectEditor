using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using DBObjectEditor.Common.DTO;
using Newtonsoft.Json.Linq;
using Oracle.ManagedDataAccess.Client;
using LibGit2Sharp;
using DBObjectEditor.UI;

namespace DBObjectEditor.Application
{
    class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            BaseAddress = new Uri("https://models.github.ai/inference/")
        };

        static async Task<int> Main(string[] args)
        {
            #region config

            var builder = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            IConfiguration config = builder.Build();

            OracleConfiguration.TnsAdmin = @"D:\oracle\product\19.0.0\client_1\network\admin";

            #endregion config

            try
            {
                #region debug mode

                Console.WriteLine($"[DEBUG] Process ID: {Process.GetCurrentProcess().Id} - SpAutoModifier");
                Console.WriteLine("[DEBUG] Visual Studio'dan 'Attach' bekleniyor...");

                while (!Debugger.IsAttached)
                {
                    Thread.Sleep(1000);
                }

                Debugger.Break();

                #endregion debug mode

                string repoYolu = args[0];
                GlobalSettings.SetOwnerValidation(false);

                using (var repo = new Repository(repoYolu))
                {
                    var yeniCommit = repo.Head.Tip;
                    var eskiCommit = yeniCommit.Parents.FirstOrDefault();

                    if (eskiCommit == null)
                    {
                        Console.WriteLine("Bu repodaki ilk commit, kıyaslanacak geçmiş yok.");
                        return 0;
                    }

                    List<SPDiffModel> bekleyenGuncellemeler = new List<SPDiffModel>();
                    List<Task<SPDiffModel>> aiTasks = new List<Task<SPDiffModel>>();

                    var commitFarklar = repo.Diff.Compare<TreeChanges>(eskiCommit.Tree, yeniCommit.Tree);
                    var duzenlenenDosyalar = commitFarklar.Modified.Where(c => c.Path.StartsWith("Code/Peak/App/Entity/Peak.App.Entity") && c.Path.EndsWith(".cs"));

                    foreach (var change in duzenlenenDosyalar)
                    {
                        var oldBlob = (Blob)eskiCommit.Tree[change.Path].Target;
                        string eskiKod = oldBlob.GetContentText();

                        var newBlob = (Blob)yeniCommit.Tree[change.Path].Target;
                        string yeniKod = newBlob.GetContentText();

                        List<SPAnalizSonuc> eskiAnalizler = Helper.RoslynAnalizEt(eskiKod);
                        List<SPAnalizSonuc> yeniAnalizler = Helper.RoslynAnalizEt(yeniKod);

                        foreach (SPAnalizSonuc yeniAnaliz in yeniAnalizler.Where(x => x.ObjeTuru == Common.Enum.ObjectTypes.Update).ToList())
                        {
                            var eskiAnaliz = eskiAnalizler.FirstOrDefault(m => m.MetodAd == yeniAnaliz.MetodAd);

                            string spAdUpdateTrigger = yeniAnaliz.SPAd.Replace("_U_", "_TRU_");
                            string spAdDeleteTrigger = yeniAnaliz.SPAd.Replace("_U_", "_TRD_");

                            eskiAnalizler.Add(new SPAnalizSonuc
                            {
                                MetodAd = eskiAnaliz.MetodAd,
                                SPAd = spAdUpdateTrigger,
                                ObjeTuru = Helper.AyarlaObjeTuru(spAdUpdateTrigger),
                                InParametreler = eskiAnaliz.InParametreler,
                                OutSutunlar = eskiAnaliz.OutSutunlar
                            });
                            yeniAnalizler.Add(new SPAnalizSonuc {
                                MetodAd = yeniAnaliz.MetodAd,
                                SPAd = spAdUpdateTrigger,
                                ObjeTuru = Helper.AyarlaObjeTuru(spAdUpdateTrigger),
                                InParametreler = yeniAnaliz.InParametreler,
                                OutSutunlar = yeniAnaliz.OutSutunlar
                            });

                            eskiAnalizler.Add(new SPAnalizSonuc
                            {
                                MetodAd = eskiAnaliz.MetodAd,
                                SPAd = spAdDeleteTrigger,
                                ObjeTuru = Helper.AyarlaObjeTuru(spAdDeleteTrigger),
                                InParametreler = eskiAnaliz.InParametreler,
                                OutSutunlar = eskiAnaliz.OutSutunlar
                            });
                            yeniAnalizler.Add(new SPAnalizSonuc
                            {
                                MetodAd = yeniAnaliz.MetodAd,
                                SPAd = spAdDeleteTrigger,
                                ObjeTuru = Helper.AyarlaObjeTuru(spAdDeleteTrigger),
                                InParametreler = yeniAnaliz.InParametreler,
                                OutSutunlar = yeniAnaliz.OutSutunlar
                            });
                        }

                        foreach (var yeniMetot in yeniAnalizler)
                        {
                            var eskiMetot = eskiAnalizler.FirstOrDefault(m => m.SPAd == yeniMetot.SPAd);
                            if (eskiMetot == null) continue;

                            var eklenenParametreler = yeniMetot.InParametreler.Except(eskiMetot.InParametreler).ToList();
                            var eklenenKolonlar = yeniMetot.OutSutunlar.Except(eskiMetot.OutSutunlar).ToList();

                            var siraliEklenenParametreler = eklenenParametreler.Select(p => {
                                int index = yeniMetot.InParametreler.IndexOf(p);
                                return index > 0
                                    ? $"{p} [HEDEF_KONUM: {yeniMetot.InParametreler[index - 1]} parametresinden hemen sonra]"
                                    : $"{p} [HEDEF_KONUM: İlk sıraya]";
                            }).ToList();

                            var siraliEklenenKolonlar = eklenenKolonlar.Select(k => {
                                int index = yeniMetot.OutSutunlar.IndexOf(k);
                                return index > 0
                                    ? $"{k} [HEDEF_KONUM: {yeniMetot.OutSutunlar[index - 1]} kolonundan hemen sonra]"
                                    : $"{k} [HEDEF_KONUM: İlk sıraya]";
                            }).ToList();

                            if (eklenenParametreler.Any() || eklenenKolonlar.Any())
                            {
                                string mevcutSpKodu = Oracle.OracleMevcutSpGetir(config["Settings:OracleConnectionString"], yeniMetot.SPAd);

                                aiTasks.Add(Helper.UretVeModeleCevirAsync(
                                    config["Settings:CopilotApiKey"],
                                    config["Settings:SelectedModel"],
                                    yeniMetot.SPAd,
                                    mevcutSpKodu,
                                    siraliEklenenParametreler, 
                                    siraliEklenenKolonlar,  
                                    yeniMetot.ObjeTuru));
                            }
                        }
                    }

                    if (aiTasks.Any())
                    {
                        SPDiffModel[] sonuclar = await Task.WhenAll(aiTasks);
                        bekleyenGuncellemeler.AddRange(sonuclar);
                    }

                    if (bekleyenGuncellemeler.Any())
                    {
                        bool onayDurumu = false;

                        Thread staThread = new Thread(() =>
                        {
                            var window = new DiffViewer(bekleyenGuncellemeler);

                            bool? result = window.ShowDialog();
                            if (result == true)
                            {
                                onayDurumu = true;
                            }
                        });

                        staThread.SetApartmentState(ApartmentState.STA);
                        staThread.Start();
                        staThread.Join();

                        if (onayDurumu)
                        {
                            foreach (var guncelleme in bekleyenGuncellemeler)
                            {
                                Oracle.OracleSpGuncelle(config["Settings:OracleConnectionString"], guncelleme.DuzenlenmisYeniKod);
                            }
                            Console.WriteLine($"{bekleyenGuncellemeler.Count} adet SP başarıyla güncellendi.");
                        }
                        else
                        {
                            Console.WriteLine("SP toplu update işlemi kullanıcı tarafından iptal edildi.");
                            return 1;
                        }
                    }
                }

                //return 0;
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hata Mesajı: {ex.Message}");
                Console.WriteLine(ex.StackTrace);

                return 1;
            }
        }

    }
}
