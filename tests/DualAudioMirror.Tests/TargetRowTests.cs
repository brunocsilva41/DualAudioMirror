using System.ComponentModel;
using Xunit;

namespace DualAudioMirror.Tests
{
    public class TargetRowTests
    {
        [Fact]
        public void DelayMs_NewRow_DefaultsToZero()
        {
            var row = new TargetRow();

            Assert.Equal("0", row.DelayText);
            Assert.Equal(0, row.DelayMs);
        }

        [Theory]
        [InlineData("abc", 0)]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData("-5", 0)]
        [InlineData("9999", 400)]
        [InlineData("401", 400)]
        [InlineData("400", 400)]
        [InlineData("250", 250)]
        [InlineData("  120  ", 120)]
        public void DelayMs_TextIsClampedOrInvalid_ReturnsSafeValue(string text, int expected)
        {
            var row = new TargetRow { DelayText = text };

            Assert.Equal(expected, row.DelayMs);
        }

        [Fact]
        public void NormalizeDelay_TooHighValue_RewritesTextToLimit()
        {
            var row = new TargetRow { DelayText = "700" };

            row.NormalizeDelay();

            Assert.Equal("400", row.DelayText);
            Assert.Equal(400, row.DelayMs);
        }

        [Fact]
        public void NormalizeDelay_NegativeValue_RewritesTextToZero()
        {
            var row = new TargetRow { DelayText = "-5" };

            row.NormalizeDelay();

            Assert.Equal("0", row.DelayText);
        }

        [Fact]
        public void NormalizeDelay_NonNumericValue_RewritesTextToZero()
        {
            var row = new TargetRow { DelayText = "abc" };

            row.NormalizeDelay();

            Assert.Equal("0", row.DelayText);
        }

        [Fact]
        public void NormalizeDelay_ValidValue_KeepsTextUnchanged()
        {
            var row = new TargetRow { DelayText = "150" };

            row.NormalizeDelay();

            Assert.Equal("150", row.DelayText);
        }

        [Fact]
        public void Selected_SetToDifferentValue_RaisesPropertyChanged()
        {
            var row = new TargetRow();
            int raised = 0;
            ((INotifyPropertyChanged)row).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TargetRow.Selected)) raised++;
            };

            row.Selected = true;

            Assert.Equal(1, raised);
        }

        [Fact]
        public void Selected_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            var row = new TargetRow();
            int raised = 0;
            ((INotifyPropertyChanged)row).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TargetRow.Selected)) raised++;
            };

            row.Selected = row.Selected;

            Assert.Equal(0, raised);
        }

        [Fact]
        public void DelayText_SetToDifferentValue_RaisesPropertyChanged()
        {
            var row = new TargetRow();
            int raised = 0;
            ((INotifyPropertyChanged)row).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TargetRow.DelayText)) raised++;
            };

            row.DelayText = "123";

            Assert.Equal(1, raised);
        }

        [Fact]
        public void DelayText_SetToSameValue_DoesNotRaisePropertyChanged()
        {
            var row = new TargetRow();
            int raised = 0;
            ((INotifyPropertyChanged)row).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TargetRow.DelayText)) raised++;
            };

            row.DelayText = row.DelayText;

            Assert.Equal(0, raised);
        }

        [Fact]
        public void PropertyChanged_SelectedAndDelayText_UseTheirOwnPropertyNames()
        {
            var row = new TargetRow();
            var raised = new System.Collections.Generic.List<string>();
            ((INotifyPropertyChanged)row).PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            row.Selected = true;
            row.DelayText = "10";

            Assert.Equal(new[] { nameof(TargetRow.Selected), nameof(TargetRow.DelayText) }, raised);
        }
    }
}
