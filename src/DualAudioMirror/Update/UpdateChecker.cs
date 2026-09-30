using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DualAudioMirror.Update
{
    public sealed class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseNotes { get; set; }
        public string InstallerAssetUrl { get; set; }
        public string ChecksumsAssetUrl { get; set; }
        public string Error { get; set; }
    }

    public sealed class UpdateChecker
    {
        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DualAudioMirror/" + UpdateInfo.AppVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
        {
            UpdateCheckResult result = new UpdateCheckResult
            {
                UpdateAvailable = false,
                LatestVersion = "",
                ReleaseNotes = "",
                InstallerAssetUrl = "",
                ChecksumsAssetUrl = "",
                Error = ""
            };

            try
            {
                using (HttpResponseMessage response = await Http.GetAsync(UpdateInfo.LatestReleaseApiUrl, ct))
                {
                    HttpStatusCode status = response.StatusCode;
                    if (status == HttpStatusCode.NotFound)
                    {
                        result.Error = "Nenhuma release encontrada no repositório (404).";
                        return result;
                    }
                    if (status == HttpStatusCode.Forbidden || status == HttpStatusCode.TooManyRequests)
                    {
                        result.Error = "O GitHub limitou as consultas no momento (rate limit). Tente novamente mais tarde.";
                        return result;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Error = "O GitHub respondeu com erro HTTP " + (int)status + ".";
                        return result;
                    }

                    string json = await response.Content.ReadAsStringAsync(ct);

                    string tagName = "";
                    string body = "";
                    string installerUrl = "";
                    string checksumsUrl = "";

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                        {
                            result.Error = "Resposta inválida recebida do GitHub.";
                            return result;
                        }
                        if (root.TryGetProperty("tag_name", out JsonElement tagEl) && tagEl.ValueKind == JsonValueKind.String)
                            tagName = tagEl.GetString() ?? "";
                        if (root.TryGetProperty("body", out JsonElement bodyEl) && bodyEl.ValueKind == JsonValueKind.String)
                            body = bodyEl.GetString() ?? "";

                        if (root.TryGetProperty("assets", out JsonElement assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement asset in assetsEl.EnumerateArray())
                            {
                                string name = "";
                                string url = "";
                                if (asset.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                    name = nameEl.GetString() ?? "";
                                if (asset.TryGetProperty("browser_download_url", out JsonElement urlEl) && urlEl.ValueKind == JsonValueKind.String)
                                    url = urlEl.GetString() ?? "";
                                if (name.Length == 0 || url.Length == 0) continue;
                                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                    name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0)
                                    installerUrl = url;
                                if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                                    checksumsUrl = url;
                            }
                        }
                    }

                    string latest = tagName.Trim();
                    if (latest.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                        latest = latest.Substring(1);
                    result.LatestVersion = latest;
                    result.ReleaseNotes = ExtractNotes(body, latest);
                    result.InstallerAssetUrl = installerUrl;
                    result.ChecksumsAssetUrl = checksumsUrl;

                    if (latest.Length == 0)
                    {
                        result.Error = "A release mais recente não possui tag de versão.";
                        return result;
                    }

                    SemVer latestVer, currentVer;
                    if (!SemVer.TryParse(latest, out latestVer))
                    {
                        result.Error = "Não foi possível interpretar a versão \"" + tagName + "\".";
                        return result;
                    }
                    if (!SemVer.TryParse(UpdateInfo.AppVersion, out currentVer))
                    {
                        result.Error = "Não foi possível interpretar a versão instalada.";
                        return result;
                    }

                    if (latestVer.CompareTo(currentVer) <= 0) return result;

                    string ignored = (AppSettings.Current.IgnoredVersion ?? "").Trim();
                    if (ignored.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                        ignored = ignored.Substring(1);
                    if (ignored.Length > 0 && string.Equals(ignored, latest, StringComparison.OrdinalIgnoreCase))
                        return result;

                    if (installerUrl.Length == 0)
                    {
                        result.Error = "A release mais recente não possui instalador (.exe).";
                        return result;
                    }

                    result.UpdateAvailable = true;
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                result.Error = ct.IsCancellationRequested
                    ? "Verificação cancelada."
                    : "Tempo esgotado ao verificar as atualizações.";
            }
            catch (JsonException)
            {
                result.Error = "Resposta inválida recebida do GitHub.";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        private static string ExtractNotes(string body, string version)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            string normalized = body.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("###", StringComparison.Ordinal)) continue;
                string heading = line.Substring(3).Trim();
                if (heading.Length == 0) continue;
                string firstToken = heading.Split(' ')[0];
                if (IsSameVersion(firstToken, version))
                {
                    start = i + 1;
                    break;
                }
            }
            if (start >= 0)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("#")) break;
                    sb.AppendLine(lines[i]);
                }
                string section = sb.ToString().Trim();
                if (section.Length > 0) return section;
            }
            string text = body.Trim();
            if (text.Length > 800) text = text.Substring(0, 800).TrimEnd() + "...";
            return text;
        }

        private static bool IsSameVersion(string token, string version)
        {
            if (string.IsNullOrEmpty(token)) return false;
            string t = token.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1);
            string v = (version ?? "").Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            if (string.Equals(t, v, StringComparison.OrdinalIgnoreCase)) return true;
            SemVer a, b;
            if (SemVer.TryParse(t, out a) && SemVer.TryParse(v, out b)) return a.CompareTo(b) == 0;
            return false;
        }
    }
}
