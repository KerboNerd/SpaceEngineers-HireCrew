using Xunit;

namespace HireCrew.Logic.Tests
{
    public class CrewKeyBindRulesTests
    {
        [Fact]
        public void ShouldToggle_when_new_press_and_chat_closed()
        {
            Assert.True(CrewKeyBindRules.ShouldToggleOpenCrewUi(true, false));
        }

        [Fact]
        public void ShouldNotToggle_when_chat_open()
        {
            Assert.False(CrewKeyBindRules.ShouldToggleOpenCrewUi(true, true));
        }

        [Fact]
        public void ShouldNotToggle_when_not_new_press()
        {
            Assert.False(CrewKeyBindRules.ShouldToggleOpenCrewUi(false, false));
        }
    }
}
