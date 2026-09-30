using DualAudioMirror.Update;
using Xunit;

namespace DualAudioMirror.Tests
{
    public class SemVerTests
    {
        [Theory]
        [InlineData("1.2.3", 1, 2, 3, null)]
        [InlineData("v1.2.3", 1, 2, 3, null)]
        [InlineData("V1.2.3", 1, 2, 3, null)]
        [InlineData("  1.2.3  ", 1, 2, 3, null)]
        [InlineData("1.2", 1, 2, 0, null)]
        [InlineData("1", 1, 0, 0, null)]
        [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
        [InlineData("v1.2.3-rc.1", 1, 2, 3, "rc.1")]
        [InlineData("1.2.3+5", 1, 2, 3, null)]
        [InlineData("1.2.3-beta.1+build.7", 1, 2, 3, "beta.1")]
        public void TryParse_ValidInput_ParsesComponents(string input, int major, int minor, int patch, string preRelease)
        {
            bool ok = SemVer.TryParse(input, out SemVer result);

            Assert.True(ok);
            Assert.NotNull(result);
            Assert.Equal(major, result.Major);
            Assert.Equal(minor, result.Minor);
            Assert.Equal(patch, result.Patch);
            Assert.Equal(preRelease, result.PreRelease);
        }

        [Theory]
        [InlineData((string)null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("1.2.3.4")]
        [InlineData(".")]
        [InlineData("1.")]
        [InlineData(".1.2")]
        [InlineData("-1.2.3")]
        [InlineData("1.2.3-")]
        [InlineData("1.2.3-beta!")]
        [InlineData("1.2.x")]
        [InlineData("v")]
        public void TryParse_InvalidInput_ReturnsFalse(string input)
        {
            bool ok = SemVer.TryParse(input, out SemVer result);

            Assert.False(ok);
            Assert.Null(result);
        }

        [Fact]
        public void CompareTo_OrdersByMajorMinorPatch()
        {
            SemVer.TryParse("1.0.0", out SemVer v100);
            SemVer.TryParse("1.0.1", out SemVer v101);
            SemVer.TryParse("1.1.0", out SemVer v110);
            SemVer.TryParse("2.0.0", out SemVer v200);

            Assert.True(v100 < v101);
            Assert.True(v101 < v110);
            Assert.True(v110 < v200);
            Assert.True(v200 > v100);
            Assert.True(v100.CompareTo(v101) < 0);
            Assert.True(v200.CompareTo(v110) > 0);
        }

        [Fact]
        public void CompareTo_ReleaseIsGreaterThanPreRelease()
        {
            SemVer.TryParse("1.0.0", out SemVer release);
            SemVer.TryParse("1.0.0-rc.1", out SemVer preRelease);

            Assert.True(release > preRelease);
            Assert.True(preRelease < release);
            Assert.True(release.CompareTo(preRelease) > 0);
            Assert.False(release.Equals(preRelease));
        }

        [Fact]
        public void CompareTo_OrdersPreReleaseNumericallyWhenSegmentIsNumeric()
        {
            SemVer.TryParse("1.0.0-alpha", out SemVer alpha);
            SemVer.TryParse("1.0.0-beta", out SemVer beta);
            SemVer.TryParse("1.0.0-rc.2", out SemVer rc2);
            SemVer.TryParse("1.0.0-rc.10", out SemVer rc10);

            Assert.True(alpha < beta);
            Assert.True(beta < rc2);
            Assert.True(rc2 < rc10);
        }

        [Fact]
        public void CompareTo_NumericSegmentIsLowerThanAlphanumericSegment()
        {
            SemVer.TryParse("1.0.0-2", out SemVer numeric);
            SemVer.TryParse("1.0.0-alpha", out SemVer alpha);

            Assert.True(numeric < alpha);
        }

        [Fact]
        public void CompareTo_ShorterPreReleaseComesFirstWhenPrefixMatches()
        {
            SemVer.TryParse("1.0.0-alpha", out SemVer alpha);
            SemVer.TryParse("1.0.0-alpha.1", out SemVer alpha1);

            Assert.True(alpha < alpha1);
        }

        [Fact]
        public void Equals_SameVersionWithDifferentPrefixOrBuildMetadata_AreEqual()
        {
            SemVer.TryParse("1.2.3", out SemVer plain);
            SemVer.TryParse("v1.2.3", out SemVer prefixed);
            SemVer.TryParse("1.2.3+5", out SemVer withBuild);

            Assert.True(plain.Equals(prefixed));
            Assert.True(plain == withBuild);
            Assert.True(plain.Equals(withBuild));
            Assert.Equal(plain.GetHashCode(), prefixed.GetHashCode());
            Assert.Equal(plain.GetHashCode(), withBuild.GetHashCode());
        }

        [Fact]
        public void Equals_DifferentVersions_AreNotEqual()
        {
            SemVer.TryParse("1.0.0-a", out SemVer a);
            SemVer.TryParse("1.0.0-b", out SemVer b);
            SemVer.TryParse("1.0.1", out SemVer next);

            Assert.True(a != b);
            Assert.False(a.Equals(b));
            Assert.False(a.Equals(next));
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void CompareTo_NullOther_ReturnsPositive()
        {
            SemVer.TryParse("1.0.0", out SemVer version);

            Assert.True(version.CompareTo(null) > 0);
            Assert.True(version > (SemVer)null);
            Assert.False(version < (SemVer)null);
        }

        [Fact]
        public void TryParse_SucceedsForEverySupportedPreReleaseOrderingCase()
        {
            Assert.True(SemVer.TryParse("1.0.0-alpha", out _));
            Assert.True(SemVer.TryParse("1.0.0-alpha.1", out _));
            Assert.True(SemVer.TryParse("1.0.0-0.3.7", out _));
        }
    }
}
