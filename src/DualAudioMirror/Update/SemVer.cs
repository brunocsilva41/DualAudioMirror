using System;

namespace DualAudioMirror.Update
{
    public sealed class SemVer : IComparable<SemVer>
    {
        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string PreRelease { get; private set; }

        private SemVer(int major, int minor, int patch, string preRelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease;
        }

        public static bool TryParse(string input, out SemVer result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim();
            if (s[0] == 'v' || s[0] == 'V') s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            string pre = null;
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1);
                s = s.Substring(0, dash);
            }
            if (s.Length == 0) return false;
            string[] parts = s.Split('.');
            if (parts.Length > 3) return false;
            int major, minor = 0, patch = 0;
            if (!TryParseNumber(parts[0], out major)) return false;
            if (parts.Length > 1 && !TryParseNumber(parts[1], out minor)) return false;
            if (parts.Length > 2 && !TryParseNumber(parts[2], out patch)) return false;
            if (pre != null)
            {
                if (pre.Length == 0) return false;
                for (int i = 0; i < pre.Length; i++)
                {
                    char c = pre[i];
                    bool ok = (c >= '0' && c <= '9') ||
                              (c >= 'a' && c <= 'z') ||
                              (c >= 'A' && c <= 'Z') ||
                              c == '.';
                    if (!ok) return false;
                }
            }
            result = new SemVer(major, minor, patch, pre);
            return true;
        }

        private static bool TryParseNumber(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9') return false;
            }
            return int.TryParse(text, out value);
        }

        public int CompareTo(SemVer other)
        {
            if (ReferenceEquals(other, null)) return 1;
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            bool aHasPre = !string.IsNullOrEmpty(PreRelease);
            bool bHasPre = !string.IsNullOrEmpty(other.PreRelease);
            if (!aHasPre && !bHasPre) return 0;
            if (!aHasPre) return 1;
            if (!bHasPre) return -1;
            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        private static int ComparePreRelease(string a, string b)
        {
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int n = pa.Length < pb.Length ? pa.Length : pb.Length;
            for (int i = 0; i < n; i++)
            {
                int c = ComparePreReleaseSegment(pa[i], pb[i]);
                if (c != 0) return c;
            }
            return pa.Length.CompareTo(pb.Length);
        }

        private static int ComparePreReleaseSegment(string a, string b)
        {
            bool na = IsNumericSegment(a);
            bool nb = IsNumericSegment(b);
            if (na && nb) return CompareNumericString(a, b);
            if (na) return -1;
            if (nb) return 1;
            return string.CompareOrdinal(a, b);
        }

        private static bool IsNumericSegment(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }

        private static int CompareNumericString(string a, string b)
        {
            string x = a.TrimStart('0');
            string y = b.TrimStart('0');
            if (x.Length == 0) x = "0";
            if (y.Length == 0) y = "0";
            if (x.Length != y.Length) return x.Length.CompareTo(y.Length);
            return string.CompareOrdinal(x, y);
        }

        public static bool operator >(SemVer left, SemVer right)
        {
            if (ReferenceEquals(left, null)) return false;
            return left.CompareTo(right) > 0;
        }

        public static bool operator <(SemVer left, SemVer right)
        {
            if (ReferenceEquals(left, null)) return !ReferenceEquals(right, null);
            return left.CompareTo(right) < 0;
        }

        public static bool operator ==(SemVer left, SemVer right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null)) return false;
            return left.Equals(right);
        }

        public static bool operator !=(SemVer left, SemVer right)
        {
            return !(left == right);
        }

        public override bool Equals(object obj)
        {
            SemVer other = obj as SemVer;
            if (ReferenceEquals(other, null)) return false;
            return CompareTo(other) == 0;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Major;
                h = h * 31 + Minor;
                h = h * 31 + Patch;
                h = h * 31 + (PreRelease == null ? 0 : PreRelease.GetHashCode());
                return h;
            }
        }
    }
}
