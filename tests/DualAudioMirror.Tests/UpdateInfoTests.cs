using DualAudioMirror.Update;
using Xunit;

namespace DualAudioMirror.Tests
{
    public class UpdateInfoTests
    {
        [Fact]
        public void AppVersion_IsNumericVersionWithoutBuildMetadata()
        {
            string version = UpdateInfo.AppVersion;

            Assert.False(string.IsNullOrEmpty(version));
            Assert.DoesNotContain("+", version);
            Assert.True(char.IsDigit(version[version.Length - 1]));
        }

        [Fact]
        public void AppVersion_ParsesAsSemVer()
        {
            Assert.True(SemVer.TryParse(UpdateInfo.AppVersion, out SemVer parsed));
            Assert.Null(parsed.PreRelease);
        }

        [Fact]
        public void RepositorySlug_MatchesGitHubRepository()
        {
            Assert.Equal("brunocsilva41/DualAudioMirror", UpdateInfo.RepositorySlug);
        }

        [Fact]
        public void LatestReleaseApiUrl_PointsToLatestReleaseEndpoint()
        {
            Assert.Equal(
                "https://api.github.com/repos/brunocsilva41/DualAudioMirror/releases/latest",
                UpdateInfo.LatestReleaseApiUrl);
        }

        [Fact]
        public void ReleasesUrl_PointsToRepositoryReleasesPage()
        {
            Assert.Equal("https://github.com/brunocsilva41/DualAudioMirror/releases", UpdateInfo.ReleasesUrl);
        }
    }
}
