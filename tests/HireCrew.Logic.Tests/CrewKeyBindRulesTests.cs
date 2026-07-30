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

        [Fact]
        public void ShouldHandleBind_matches_open_ui_gate()
        {
            Assert.True(CrewKeyBindRules.ShouldHandleBind(true, false));
            Assert.False(CrewKeyBindRules.ShouldHandleBind(true, true));
            Assert.False(CrewKeyBindRules.ShouldHandleBind(false, false));
        }

        [Fact]
        public void ShouldRecallRole_when_any_on_mission()
        {
            Assert.True(CrewKeyBindRules.ShouldRecallRole(true));
            Assert.False(CrewKeyBindRules.ShouldRecallRole(false));
        }

        [Fact]
        public void FormatRoleDispatchSummary_sent_recall_none()
        {
            Assert.Equal("Construction: sent 3", CrewKeyBindRules.FormatRoleDispatchSummary("Construction", false, 3));
            Assert.Equal("Salvage: recalling 2", CrewKeyBindRules.FormatRoleDispatchSummary("Salvage", true, 2));
            Assert.Equal("Construction: none ready", CrewKeyBindRules.FormatRoleDispatchSummary("Construction", false, 0));
            Assert.Equal("Salvage: none ready", CrewKeyBindRules.FormatRoleDispatchSummary("Salvage", true, 0));
        }
    }
}
