using System.Reflection;

namespace DualAudioMirror.Update
{
    public static class UpdateInfo
    {
        public const string RepositoryOwner = "brunocsilva41";
        public const string RepositoryName = "DualAudioMirror";
        public const string ProductName = "DualAudioMirror";
        public const string WebsiteUrl = "https://github.com/brunocsilva41/DualAudioMirror";
        public const string ReleasesUrl = "https://github.com/brunocsilva41/DualAudioMirror/releases";
        public const string VbCablePageUrl = "https://vb-audio.com/Cable/";
        public const string VbCableDownloadUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip";

        public static string RepositorySlug
        {
            get { return RepositoryOwner + "/" + RepositoryName; }
        }

        public static string LatestReleaseApiUrl
        {
            get { return "https://api.github.com/repos/" + RepositorySlug + "/releases/latest"; }
        }

        public static string AppVersion
        {
            get
            {
                AssemblyInformationalVersionAttribute info =
                    Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string v = info != null ? info.InformationalVersion : null;
                if (string.IsNullOrEmpty(v)) return "0.0.0";
                int plus = v.IndexOf('+');
                return plus >= 0 ? v.Substring(0, plus) : v;
            }
        }
    }
}
